using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using T650Bridge.Hidpp;

namespace T650Bridge.UI;

public class TrayIconManager : IDisposable
{
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool DestroyIcon(IntPtr handle);

    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;
    private const int SW_RESTORE = 9;

    private NotifyIcon? _notifyIcon;
    private ContextMenuStrip? _contextMenu;
    private ToolStripMenuItem? _statusItem;
    private ToolStripMenuItem? _startStopItem;
    private ToolStripMenuItem? _rewakeItem;
    private ToolStripMenuItem? _notificationsItem;
    private ToolStripMenuItem? _toggleConsoleItem;
    private ToolStripMenuItem? _exitItem;

    private Thread? _uiThread;
    private readonly AutoResetEvent _uiReady = new(false);

    private Icon? _iconActive;
    private Icon? _iconPaused;
    private Icon? _iconDisconnected;

    public bool IsActive { get; private set; } = true;
    public bool IsConsoleHidden { get; private set; } = false;
    public bool EnableNotifications { get; set; } = false;

    private readonly bool _isAdminMode;
    private readonly bool _isElevated;

    public event Action? ToggleStartStop;
    public event Action? RequestRewake;
    public event Action? RequestExit;

    public TrayIconManager(bool isAdminMode = false, bool isElevated = false)
    {
        _isAdminMode = isAdminMode;
        _isElevated = isElevated;

        _uiThread = new Thread(RunMessageLoop)
        {
            IsBackground = true,
            Name = "TrayIconThread"
        };
        _uiThread.SetApartmentState(ApartmentState.STA);
        _uiThread.Start();

        _uiReady.WaitOne(3000);
    }

    private SynchronizationContext? _syncContext;
    private Control? _invoker;

    private void RunMessageLoop()
    {
        _invoker = new Control();
        IntPtr forceHandle = _invoker.Handle; // Forces Win32 window handle creation immediately
        _syncContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(_syncContext);

        IconGenerator.EnsureIcoFileExists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "T650.ico"));

        using var bmpActive = IconGenerator.CreateTouchpadBitmap(32, Color.FromArgb(0, 220, 130), active: true);
        using var bmpPaused = IconGenerator.CreateTouchpadBitmap(32, Color.FromArgb(240, 180, 40), active: false);
        using var bmpDisconnected = IconGenerator.CreateTouchpadBitmap(32, Color.FromArgb(240, 70, 70), active: false);

        _iconActive = ConvertBitmapToIcon(bmpActive);
        _iconPaused = ConvertBitmapToIcon(bmpPaused);
        _iconDisconnected = ConvertBitmapToIcon(bmpDisconnected);

        _contextMenu = new ContextMenuStrip();
        
        // Header: Version & Edition Display
        var version = typeof(TrayIconManager).Assembly.GetName().Version?.ToString(3) ?? "1.1.2";
        string tag = _isAdminMode ? "[Admin]" : "[PTP]";
        var titleItem = new ToolStripMenuItem($"Logitech T650 Bridge v{version} {tag}")
        {
            Enabled = false,
            Font = new Font(Control.DefaultFont, FontStyle.Italic)
        };
        _contextMenu.Items.Add(titleItem);

        if (_isElevated)
        {
            var adminStatusItem = new ToolStripMenuItem("🛡️ Elevation: Administrator (UIPI Active)")
            {
                Enabled = false
            };
            _contextMenu.Items.Add(adminStatusItem);
        }
        else
        {
            var restartAdminItem = new ToolStripMenuItem("🛡️ Restart as Administrator", null, (s, e) =>
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = Environment.ProcessPath ?? "T650Bridge.exe",
                        UseShellExecute = true,
                        Verb = "runas",
                        Arguments = string.Join(" ", Environment.GetCommandLineArgs().Skip(1))
                    };
                    Process.Start(psi);
                    Environment.Exit(0);
                }
                catch { }
            });
            _contextMenu.Items.Add(restartAdminItem);
        }

        // 1. Status Display
        _statusItem = new ToolStripMenuItem("Status: Initializing...")
        {
            Enabled = false,
            Font = new Font(Control.DefaultFont, FontStyle.Bold)
        };
        _contextMenu.Items.Add(_statusItem);
        _contextMenu.Items.Add(new ToolStripSeparator());

        // 2. Start / Stop (Pause / Resume) Gestures
        _startStopItem = new ToolStripMenuItem("⏸ Pause Gestures (Stop)", null, (s, e) => ToggleStartStop?.Invoke());
        _contextMenu.Items.Add(_startStopItem);

        // 3. Re-wake / Re-unlock T650
        _rewakeItem = new ToolStripMenuItem("🔄 Re-wake & Unlock Pad", null, (s, e) => RequestRewake?.Invoke());
        _contextMenu.Items.Add(_rewakeItem);

        // 4. Desktop Notifications Toggle
        _notificationsItem = new ToolStripMenuItem(EnableNotifications ? "🔔 Desktop Notifications (Enabled)" : "🔕 Desktop Notifications (Muted)", null, (s, e) =>
        {
            EnableNotifications = !EnableNotifications;
            if (_notificationsItem != null)
            {
                _notificationsItem.Text = EnableNotifications ? "🔔 Desktop Notifications (Enabled)" : "🔕 Desktop Notifications (Muted)";
            }
        });
        _contextMenu.Items.Add(_notificationsItem);

        _contextMenu.Items.Add(new ToolStripSeparator());

        // 4. Toggle Console Window (Show / Hide)
        _toggleConsoleItem = new ToolStripMenuItem("🔲 Hide Console to Tray", null, (s, e) => ToggleConsoleWindow());
        _contextMenu.Items.Add(_toggleConsoleItem);

        _contextMenu.Items.Add(new ToolStripSeparator());

        // 5. Exit
        _exitItem = new ToolStripMenuItem("❌ Exit Bridge Daemon", null, (s, e) =>
        {
            RequestExit?.Invoke();
            Application.ExitThread();
        });
        _contextMenu.Items.Add(_exitItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = _iconActive,
            Text = "Logitech T650 - Active",
            Visible = true,
            ContextMenuStrip = _contextMenu
        };

        _notifyIcon.DoubleClick += (s, e) => ToggleConsoleWindow();

        _uiReady.Set();

        Application.Run();
    }

    private void PostToUiThread(Action action)
    {
        if (_syncContext != null)
        {
            _syncContext.Post(_ =>
            {
                try { action(); } catch { }
            }, null);
        }
        else if (_invoker != null && _invoker.IsHandleCreated)
        {
            _invoker.BeginInvoke(action);
        }
    }

    public void UpdateState(DeviceConnectionState state, bool isPaused)
    {
        PostToUiThread(() =>
        {
            string statusDesc;
            Icon targetIcon;
            string tooltip;

            switch (state)
            {
                case DeviceConnectionState.SearchingForReceiver:
                    statusDesc = "Waiting for Receiver...";
                    targetIcon = _iconDisconnected ?? SystemIcons.Application;
                    tooltip = "Logitech T650: Receiver Disconnected";
                    if (_rewakeItem != null) _rewakeItem.Enabled = false;
                    break;

                case DeviceConnectionState.ReceiverConnectedWaitingForPad:
                    statusDesc = "Receiver Connected (Turn On Pad)";
                    targetIcon = _iconPaused ?? SystemIcons.Application;
                    tooltip = "Logitech T650: Waiting for Pad";
                    if (_rewakeItem != null) _rewakeItem.Enabled = true;
                    break;

                case DeviceConnectionState.TouchpadActive:
                default:
                    if (isPaused)
                    {
                        statusDesc = "Paused (Gestures Off)";
                        targetIcon = _iconPaused ?? SystemIcons.Application;
                        tooltip = "Logitech T650: Paused";
                    }
                    else
                    {
                        statusDesc = "Active (Streaming)";
                        targetIcon = _iconActive ?? SystemIcons.Application;
                        tooltip = "Logitech T650: Active (Streaming)";
                    }
                    if (_rewakeItem != null) _rewakeItem.Enabled = true;
                    break;
            }

            if (_statusItem != null)
            {
                _statusItem.Text = $"Status: {statusDesc}";
            }

            if (_startStopItem != null)
            {
                _startStopItem.Text = isPaused ? "▶ Resume Gestures (Start)" : "⏸ Pause Gestures (Stop)";
            }

            if (_notifyIcon != null)
            {
                _notifyIcon.Icon = targetIcon;
                _notifyIcon.Text = SafeTooltipText(tooltip);
            }
        });
    }

    public void UpdateStatus(string statusText, bool isConnected, bool isStreaming, bool isPaused)
    {
        PostToUiThread(() =>
        {
            if (_statusItem != null)
            {
                _statusItem.Text = $"Status: {statusText}";
            }

            if (_startStopItem != null)
            {
                _startStopItem.Text = isPaused ? "▶ Resume Gestures (Start)" : "⏸ Pause Gestures (Stop)";
            }

            if (_notifyIcon != null)
            {
                if (!isConnected)
                {
                    _notifyIcon.Icon = _iconDisconnected;
                    _notifyIcon.Text = SafeTooltipText($"Logitech T650: {statusText}");
                }
                else if (isPaused)
                {
                    _notifyIcon.Icon = _iconPaused;
                    _notifyIcon.Text = SafeTooltipText($"Logitech T650: Paused ({statusText})");
                }
                else
                {
                    _notifyIcon.Icon = _iconActive;
                    _notifyIcon.Text = SafeTooltipText($"Logitech T650: Active ({statusText})");
                }
            }
        });
    }

    private static string SafeTooltipText(string text)
    {
        // NotifyIcon.Text Win32 API limit is 63 characters
        if (string.IsNullOrEmpty(text)) return "Logitech T650";
        return text.Length > 63 ? text.Substring(0, 60) + "..." : text;
    }

    public void ShowBalloon(string title, string message, ToolTipIcon icon = ToolTipIcon.Info, bool force = false)
    {
        if (!EnableNotifications && !force) return;

        PostToUiThread(() =>
        {
            _notifyIcon?.ShowBalloonTip(3000, title, message, icon);
        });
    }

    public void HideConsole(bool notifyUser = false)
    {
        IntPtr hWnd = GetConsoleWindow();
        if (hWnd == IntPtr.Zero) return;

        ShowWindow(hWnd, SW_HIDE);
        IsConsoleHidden = true;
        if (_toggleConsoleItem != null)
            _toggleConsoleItem.Text = "🔳 Show Console Window";

        if (notifyUser && EnableNotifications)
        {
            ShowBalloon("Logitech T650 Connected", "Gestures active! Minimized to tray. Double-click icon to open console.", ToolTipIcon.Info);
        }
    }

    public void ShowConsole()
    {
        IntPtr hWnd = GetConsoleWindow();
        if (hWnd == IntPtr.Zero) return;

        ShowWindow(hWnd, SW_RESTORE);
        ShowWindow(hWnd, SW_SHOW);
        IsConsoleHidden = false;
        if (_toggleConsoleItem != null)
            _toggleConsoleItem.Text = "🔲 Hide Console to Tray";
    }

    public void ToggleConsoleWindow()
    {
        IntPtr hWnd = GetConsoleWindow();
        if (hWnd == IntPtr.Zero) return;

        if (IsWindowVisible(hWnd))
        {
            HideConsole(notifyUser: true);
        }
        else
        {
            ShowConsole();
        }
    }

    private static Icon ConvertBitmapToIcon(Bitmap bmp)
    {
        IntPtr hIcon = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(hIcon).Clone();
        DestroyIcon(hIcon);
        return icon;
    }

    public void Dispose()
    {
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
        _contextMenu?.Dispose();
        _iconActive?.Dispose();
        _iconPaused?.Dispose();
        _iconDisconnected?.Dispose();
    }
}
