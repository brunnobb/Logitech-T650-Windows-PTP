using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;
using T650Bridge.Gesture;
using T650Bridge.Hidpp;
using T650Bridge.Ptp;
using T650Bridge.Touch;
using T650Bridge.UI;

namespace T650Bridge;

class Program
{
    static void Main(string[] args)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.1.2";

#if FLAVOR_ADMIN
        const bool compiledAsAdmin = true;
#else
        const bool compiledAsAdmin = false;
#endif

        bool adminMode = compiledAsAdmin || args.Contains("--admin") || args.Contains("--standalone");
        bool isElevated = IsElevated();

        if (adminMode && !isElevated)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = Environment.ProcessPath ?? "T650Bridge.exe",
                    UseShellExecute = true,
                    Verb = "runas",
                    Arguments = string.Join(" ", args)
                };
                Process.Start(psi);
                return;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[Security] Elevation prompt declined ({ex.Message}). Continuing as standard user.");
                Console.WriteLine("[Security] Note: Mouse movement will freeze over Admin windows due to Windows UIPI.");
                Console.ResetColor();
            }
        }

        string edition = adminMode ? "Standalone Admin Edition" : "Precision Touchpad (PTP) Edition";

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("===============================================================");
        Console.WriteLine($" Logitech T650 Bridge v{version} - {edition} ");
        Console.WriteLine("===============================================================");
        Console.ResetColor();

        Console.WriteLine($"[Version] Bridge Daemon v{version} ({edition})");
        Console.WriteLine($"[Security] Elevation: {(isElevated ? "Administrator (UIPI Bypass Active)" : "Standard User (Medium Integrity)")}");
        if (!isElevated)
        {
            Console.WriteLine("[Security] Notice: Run as Administrator or install Virtual PTP Driver to control Admin windows.");
        }
        bool enableGestures = !args.Contains("--no-gesture");
        bool enableNotifications = args.Contains("--notifications") || args.Contains("--balloon");
        bool verboseDiag = args.Contains("--diag") || args.Contains("--test") || (!args.Contains("--silent") && !args.Contains("--tray"));

        Console.WriteLine($"[Config] Gestures Enabled: {enableGestures}");
        Console.WriteLine($"[Config] Desktop Notifications: {(enableNotifications ? "Enabled" : "Disabled (Quiet)")}");
        const string MutexName = @"Global\LogitechT650Bridge_SingleInstanceMutex";
        using var singleMutex = new Mutex(true, MutexName, out bool isOnlyInstance);
        if (!isOnlyInstance)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\n[Notice] Another instance of Logitech T650 Bridge is already running. Exiting.");
            Console.ResetColor();
            return;
        }

        AttachToInteractiveDesktop();

        bool hasDesktop = GetCursorPos(out var curPos);
        Console.WriteLine($"[Desktop] Interactive winsta0 access: {(hasDesktop ? $"OK (Cursor at {curPos.X},{curPos.Y})" : "Limited")}");

        if (args.Contains("--test-only")) return;

        Console.WriteLine("Press Ctrl+C to terminate.\n");

        using var receiver = new UnifyingReceiver();
        var assembler = new FrameAssembler();
        var gestureEngine = new PrecisionGestureEngine();

        receiver.LogMessage += msg => Console.WriteLine($"[Receiver] {msg}");
        receiver.ReportReceived += report => assembler.ProcessRawReport(report);

        gestureEngine.GestureTriggered += gesture =>
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\n>>> {gesture} <<<");
            Console.ResetColor();
        };

        using var ptpDriver = new PtpDriverClient();
        bool ptpConnected = ptpDriver.Connect();
        if (ptpConnected)
        {
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine("[Tier 2] Connected to Virtual PTP Driver! Injecting directly into Windows Touch Stack.");
            Console.ResetColor();
        }
        else
        {
            Console.WriteLine("[Tier 1] Virtual PTP Driver not detected. Running high-performance software gesture engine.");
        }

        long frameIndex = 0;
        var sw = Stopwatch.StartNew();

        assembler.FrameReady += frame =>
        {
            frameIndex++;
            ushort scanTime = (ushort)((sw.ElapsedTicks * 10_000) / Stopwatch.Frequency);

            // 1. Build standard Microsoft PTP Report
            var ptpReport = PtpTouchReport.FromTouchFrame(frame, scanTime);

            // 2. Inject to Driver (Tier 2) or fallback to software gesture engine (Tier 1)
            if (ptpDriver.IsConnected)
            {
                ptpDriver.InjectReport(ptpReport);
            }
            else if (enableGestures)
            {
                gestureEngine.ProcessFrame(frame);
            }

            // 3. Diagnostic console output
            if (verboseDiag)
            {
                var touching = frame.Contacts.Where(c => c.IsTouching).ToList();
                string contactsInfo = touching.Count == 0 
                    ? "None (Lifted)" 
                    : string.Join(", ", touching.Select(c => $"F{c.Id}:({c.X,4},{c.Y,4})"));

                string clickInfo = frame.PhysicalButtonPressed ? "[CLICK]" : "       ";

                Console.Write($"\r[Frame #{frameIndex:D5}] Active: {touching.Count}/5 {clickInfo} | Contacts: {contactsInfo,-45}");
            }
        };

        bool silentStartup = args.Contains("--silent") || args.Contains("--tray") || args.Contains("--minimized");
        bool autoHide = !args.Contains("--console") && !args.Contains("--no-hide");

        using var tray = new TrayIconManager(adminMode, isElevated);
        tray.EnableNotifications = enableNotifications;
        tray.UpdateState(DeviceConnectionState.SearchingForReceiver, isPaused: !enableGestures);

        if (silentStartup)
        {
            tray.HideConsole(notifyUser: false);
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        tray.ToggleStartStop += () =>
        {
            enableGestures = !enableGestures;
            string stateMsg = enableGestures ? "Active (Gestures Running)" : "Paused (Standard Mouse Only)";
            Console.WriteLine($"\n[Tray] Gestures toggled -> {stateMsg}");
            tray.UpdateState(receiver.State, isPaused: !enableGestures);
        };

        tray.RequestRewake += () =>
        {
            Console.WriteLine("\n[Tray] Re-sending raw multi-touch unlock packet to T650...");
            receiver.UnlockRawMode();
            tray.ShowBalloon("Logitech T650", "Re-sent raw touch mode unlock command to pad.");
        };

        tray.RequestExit += () =>
        {
            Console.WriteLine("\n[Tray] Exit requested from taskbar menu.");
            cts.Cancel();
        };

        bool hasEverStreamed = false;

        receiver.StateChanged += state =>
        {
            tray.UpdateState(state, isPaused: !enableGestures);

            switch (state)
            {
                case DeviceConnectionState.SearchingForReceiver:
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("\n[Receiver] Waiting for Logitech Unifying receiver to be plugged in...");
                    Console.ResetColor();
                    tray.ShowBalloon("Logitech T650", "Waiting for Logitech Unifying receiver USB dongle.", System.Windows.Forms.ToolTipIcon.Warning);
                    break;

                case DeviceConnectionState.ReceiverConnectedWaitingForPad:
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.WriteLine("\n[Receiver] Unifying receiver ready! Waiting for T650 Touchpad to power on...");
                    Console.ResetColor();
                    tray.ShowBalloon("Logitech T650", "Unifying receiver connected. Turn on your T650 touchpad.", System.Windows.Forms.ToolTipIcon.Info);
                    break;

                case DeviceConnectionState.TouchpadActive:
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("\n[Active] T650 Multi-touch stream is live! Try touching, scrolling, or swiping on the pad.\n");
                    Console.ResetColor();

                    if (!hasEverStreamed)
                    {
                        hasEverStreamed = true;
                        if (autoHide && !silentStartup && !tray.IsConsoleHidden)
                        {
                            Console.WriteLine("[Tray] Pad connected and active! Minimizing console to system tray...");
                            Thread.Sleep(600);
                            tray.HideConsole(notifyUser: true);
                        }
                        else
                        {
                            tray.ShowBalloon("Logitech T650 Active", "Multi-touch gestures ready. Right-click taskbar icon to manage.", System.Windows.Forms.ToolTipIcon.Info);
                        }
                    }
                    break;
            }
        };

        receiver.StartSupervisor();

        Console.WriteLine("[Tray] Taskbar notification icon active. Right-click icon for start/stop & controls.");
        Console.WriteLine("[Keys] Press 'r' anytime to re-wake/unlock | Press 'h' to hide/show console | Press 'q' to exit.\n");

        bool canReadKey = false;
        try
        {
            canReadKey = !Console.IsInputRedirected;
        }
        catch
        {
            canReadKey = false;
        }

        while (!cts.IsCancellationRequested)
        {
            if (canReadKey)
            {
                try
                {
                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(intercept: true);
                        if (key.Key == ConsoleKey.R)
                        {
                            Console.WriteLine("\n[User] Re-sending raw multi-touch unlock packet to T650...");
                            receiver.UnlockRawMode();
                        }
                        else if (key.Key == ConsoleKey.H)
                        {
                            tray.ToggleConsoleWindow();
                        }
                        else if (key.Key == ConsoleKey.Q)
                        {
                            cts.Cancel();
                            break;
                        }
                    }
                }
                catch (InvalidOperationException)
                {
                    canReadKey = false;
                }
            }
            Thread.Sleep(50);
        }

        Console.WriteLine("\nShutting down bridge daemon...");
    }

    private static void AttachToInteractiveDesktop()
    {
        try
        {
            const uint WINSTA_ALL_ACCESS = 0x37F;
            const uint DESKTOP_ALL_ACCESS = 0x1FF;

            IntPtr hWinSta = OpenWindowStation("winsta0", false, WINSTA_ALL_ACCESS);
            if (hWinSta != IntPtr.Zero)
            {
                SetProcessWindowStation(hWinSta);
                IntPtr hDesktop = OpenDesktop("Default", 0, false, DESKTOP_ALL_ACCESS);
                if (hDesktop != IntPtr.Zero)
                {
                    SetThreadDesktop(hDesktop);
                }
            }
        }
        catch { }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenWindowStation(string lpszWinSta, bool fInherit, uint dwDesiredAccess);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessWindowStation(IntPtr hWinSta);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenDesktop(string lpszDesktop, uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetThreadDesktop(IntPtr hDesktop);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetCursorPos(int X, int Y);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetProcessWindowStation();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetThreadDesktop(uint dwThreadId);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    private static bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}
