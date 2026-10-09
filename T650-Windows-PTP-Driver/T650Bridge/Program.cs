using System.Diagnostics;
using T650Bridge.Gesture;
using T650Bridge.Hidpp;
using T650Bridge.Ptp;
using T650Bridge.Touch;

namespace T650Bridge;

class Program
{
    static void Main(string[] args)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("===============================================================");
        Console.WriteLine(" Logitech T650 Windows Precision Touchpad (PTP) Bridge Daemon ");
        Console.WriteLine("===============================================================");
        Console.ResetColor();

        bool enableGestures = args.Contains("--gesture") || args.Length == 0;
        bool verboseDiag = args.Contains("--diag") || args.Contains("--test") || args.Length == 0;

        Console.WriteLine($"[Config] Gestures Enabled: {enableGestures}");
        Console.WriteLine($"[Config] Diagnostic Display: {verboseDiag}");

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

        long frameIndex = 0;
        var sw = Stopwatch.StartNew();

        assembler.FrameReady += frame =>
        {
            frameIndex++;
            ushort scanTime = (ushort)((sw.ElapsedTicks * 10_000) / Stopwatch.Frequency);

            // 1. Build standard Microsoft PTP Report
            var ptpReport = PtpTouchReport.FromTouchFrame(frame, scanTime);

            // 2. Process gestures if enabled
            if (enableGestures)
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

        if (!receiver.Connect())
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("\n[Error] Could not initialize connection to Logitech Unifying receiver.");
            Console.ResetColor();
            return;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("\n[Active] T650 Multi-touch stream is live! Try touching, scrolling, or swiping on the pad.\n");
        Console.ResetColor();

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        cts.Token.WaitHandle.WaitOne();

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
}
