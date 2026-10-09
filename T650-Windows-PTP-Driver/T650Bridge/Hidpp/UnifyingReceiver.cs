using HidSharp;
using System.Diagnostics;

namespace T650Bridge.Hidpp;

public enum DeviceConnectionState
{
    SearchingForReceiver,           // Dongle not plugged in (Red)
    ReceiverConnectedWaitingForPad, // Dongle in, T650 off / sleeping (Amber)
    TouchpadActive                  // Dongle in, T650 awake & streaming (Green)
}

public class UnifyingReceiver : IDisposable
{
    public const int LogitechVid = 0x046D;
    public const int UnifyingPid = 0xC52B;
    public const byte Swid = 0x0E; // Distinct software ID

    private HidDevice? _device;
    private HidStream? _stream;
    private CancellationTokenSource? _globalCts;
    private CancellationTokenSource? _streamCts;

    private Thread? _supervisorThread;
    private Thread? _readThread;
    private Thread? _heartbeatThread;
    private readonly AutoResetEvent _deviceEvent = new(false);
    private volatile bool _streamFaulted;

    public byte DeviceIndex { get; private set; } = 0x01;
    public byte RawXyFeatureIndex { get; private set; } = 0x0F;
    public DeviceConnectionState State { get; private set; } = DeviceConnectionState.SearchingForReceiver;
    public bool IsConnected => _stream != null && State != DeviceConnectionState.SearchingForReceiver;

    public event Action<DeviceConnectionState>? StateChanged;
    public event Action<byte[]>? ReportReceived;
    public event Action<string>? LogMessage;

    public UnifyingReceiver()
    {
        _globalCts = new CancellationTokenSource();
        DeviceList.Local.Changed += OnDeviceListChanged;
    }

    private void OnDeviceListChanged(object? sender, DeviceListChangedEventArgs e)
    {
        _deviceEvent.Set();
    }

    public void StartSupervisor()
    {
        if (_supervisorThread != null && _supervisorThread.IsAlive) return;

        _supervisorThread = new Thread(SupervisorLoop)
        {
            IsBackground = true,
            Name = "T650_SupervisorThread"
        };
        _supervisorThread.Start();
    }

    private void SetState(DeviceConnectionState newState)
    {
        if (State != newState)
        {
            State = newState;
            StateChanged?.Invoke(newState);
        }
    }

    private void SupervisorLoop()
    {
        Log("Hotplug supervisor started. Monitoring Logitech hardware endpoints...");

        while (_globalCts != null && !_globalCts.IsCancellationRequested)
        {
            try
            {
                if (_stream == null)
                {
                    if (TryOpenReceiver())
                    {
                        SetState(DeviceConnectionState.ReceiverConnectedWaitingForPad);
                        StartStreamThreads();

                        // Perform initial handshake query in background
                        ThreadPool.QueueUserWorkItem(_ =>
                        {
                            ResolveTouchpadFeature();
                            UnlockRawMode();
                        });
                    }
                    else
                    {
                        SetState(DeviceConnectionState.SearchingForReceiver);
                        // Wait for USB device connection event or 1500ms fallback timeout
                        _deviceEvent.WaitOne(1500);
                    }
                }
                else
                {
                    // Stream is currently open. Wait for stream fault or global cancellation
                    _deviceEvent.WaitOne(1500);

                    if (_streamFaulted)
                    {
                        Log("Receiver disconnected or stream failed. Closing connection and resuming search...");
                        TearDownStream();
                        SetState(DeviceConnectionState.SearchingForReceiver);
                        Thread.Sleep(500);
                    }
                }
            }
            catch (Exception ex)
            {
                if (_globalCts != null && !_globalCts.IsCancellationRequested)
                {
                    Log($"Supervisor error: {ex.Message}");
                    Thread.Sleep(1000);
                }
            }
        }
    }

    private bool TryOpenReceiver()
    {
        try
        {
            var devices = DeviceList.Local.GetHidDevices(LogitechVid, UnifyingPid).ToList();
            if (devices.Count == 0) return false;

            // Find Interface 2, Col02 (which accepts the 20-byte HID++ Output Reports)
            _device = devices.FirstOrDefault(d => 
                d.DevicePath.Contains("MI_02&Col02", StringComparison.OrdinalIgnoreCase)
            ) ?? devices.FirstOrDefault(d => 
                d.DevicePath.Contains("MI_02", StringComparison.OrdinalIgnoreCase) && d.GetMaxOutputReportLength() >= 20
            ) ?? devices.FirstOrDefault(d => d.GetMaxOutputReportLength() >= 20);

            if (_device == null) return false;

            if (!_device.TryOpen(out _stream))
            {
                return false;
            }

            _stream.ReadTimeout = 500;
            _stream.WriteTimeout = 500;
            _streamFaulted = false;

            Log($"Connected to Unifying receiver endpoint: {_device.DevicePath}");
            return true;
        }
        catch
        {
            _stream = null;
            _device = null;
            return false;
        }
    }

    private void StartStreamThreads()
    {
        _streamCts = new CancellationTokenSource();
        var ct = _streamCts.Token;

        _readThread = new Thread(() => ReadLoop(ct))
        {
            IsBackground = true,
            Name = "T650_ReadThread"
        };
        _readThread.Start();

        _heartbeatThread = new Thread(() => HeartbeatLoop(ct))
        {
            IsBackground = true,
            Name = "T650_HeartbeatThread"
        };
        _heartbeatThread.Start();
    }

    private void TearDownStream()
    {
        try
        {
            _streamCts?.Cancel();
            _stream?.Dispose();
        }
        catch { }
        finally
        {
            _stream = null;
            _device = null;
            _streamFaulted = false;
            _streamCts?.Dispose();
            _streamCts = null;
        }
    }

    public void ResolveTouchpadFeature()
    {
        if (_stream == null) return;
        Log("Resolving Feature 0x6100 (TOUCHPAD_RAW_XY)...");

        // Root feature index 0x00, Function 0 (getFeature), query 0x6100
        byte[] req = new byte[20];
        req[0] = 0x11;
        req[1] = DeviceIndex;
        req[2] = 0x00;
        req[3] = Swid;
        req[4] = 0x61;
        req[5] = 0x00;

        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (_stream == null) return;
                _stream.Write(req);
                byte[] resp = new byte[32];
                int bytesRead = _stream.Read(resp, 0, resp.Length);
                if (bytesRead >= 7 && resp[0] == 0x11 && resp[1] == DeviceIndex && resp[2] == 0x00)
                {
                    RawXyFeatureIndex = resp[4];
                    Log($"Feature 0x6100 resolved at runtime to Index 0x{RawXyFeatureIndex:X2}!");
                    SetState(DeviceConnectionState.TouchpadActive);
                    return;
                }
            }
            catch
            {
                Thread.Sleep(50);
            }
        }

        Log($"Notice: Auto-query timed out (device may be asleep). Falling back to standard Index 0x{RawXyFeatureIndex:X2}.");
    }

    public void UnlockRawMode()
    {
        if (_stream == null) return;

        Log($"Sending unlock packet: setRawReportState(0x05) to device {DeviceIndex}, feature 0x{RawXyFeatureIndex:X2}...");
        byte[] unlock = new byte[20];
        unlock[0] = 0x11;
        unlock[1] = DeviceIndex;
        unlock[2] = RawXyFeatureIndex;
        unlock[3] = (byte)((2 << 4) | Swid); // func 2 (setRawReportState) | SWID
        unlock[4] = 0x05; // 0x01 (enable raw) | 0x04 (enhanced sensitivity)

        for (int i = 0; i < 3; i++)
        {
            try
            {
                if (_stream == null) return;
                _stream.Write(unlock);
                Thread.Sleep(30);
            }
            catch (Exception ex)
            {
                if (_streamFaulted) return;
                Log($"Warning during unlock write: {ex.Message}");
            }
        }
    }

    private void HeartbeatLoop(CancellationToken ct)
    {
        // Periodic keep-alive packet every 2.0 seconds.
        // Continuously re-asserts setRawReportState(0x05) when device is active.
        byte[] keepAlive = new byte[20];
        keepAlive[0] = 0x11;
        keepAlive[1] = DeviceIndex;
        keepAlive[2] = RawXyFeatureIndex;
        keepAlive[3] = (byte)((2 << 4) | Swid); // func 2 (setRawReportState) | SWID
        keepAlive[4] = 0x05; // 0x01 (enable raw) | 0x04 (enhanced sensitivity)

        int consecutiveErrors = 0;

        while (!ct.IsCancellationRequested && _stream != null)
        {
            try
            {
                Thread.Sleep(2000);
                if (_stream != null && !_streamFaulted)
                {
                    _stream.Write(keepAlive);
                    consecutiveErrors = 0;
                }
            }
            catch (IOException)
            {
                consecutiveErrors++;
                if (consecutiveErrors >= 2)
                {
                    _streamFaulted = true;
                    _deviceEvent.Set();
                    break;
                }
            }
            catch
            {
                // Ignore transient write errors while pad is asleep/off
            }
        }
    }

    private void ReadLoop(CancellationToken ct)
    {
        byte[] buffer = new byte[64];
        while (!ct.IsCancellationRequested && _stream != null)
        {
            try
            {
                int read = _stream.Read(buffer, 0, buffer.Length);
                if (read > 0)
                {
                    byte[] report = new byte[read];
                    Array.Copy(buffer, report, read);

                    // 1. HID++ 1.0/DJ Wireless connection event (0x10, DeviceIndex, 0x41)
                    if (report[0] == 0x10 && report.Length >= 3 && report[2] == 0x41)
                    {
                        DeviceIndex = report[1];
                        Log($"[Receiver] Hardware wireless connect event (0x41) on Device #{DeviceIndex}! Re-activating Raw Touch Mode...");
                        SetState(DeviceConnectionState.TouchpadActive);
                        ThreadPool.QueueUserWorkItem(_ =>
                        {
                            Thread.Sleep(100);
                            UnlockRawMode();
                        });
                    }
                    // 2. HID++ 1.0/DJ Wireless link loss / power down (0x10, DeviceIndex, 0x42, status 0x01)
                    else if (report[0] == 0x10 && report.Length >= 4 && report[2] == 0x42 && report[3] == 0x01)
                    {
                        Log($"[Receiver] Hardware link loss detected (0x42) on Device #{report[1]}. Touchpad is standby/off.");
                        SetState(DeviceConnectionState.ReceiverConnectedWaitingForPad);
                    }
                    // 3. HID++ 2.0/Long root notification or feature response (0x11, DeviceIndex, 0x00)
                    else if (report[0] == 0x11 && report.Length >= 3 && report[2] == 0x00)
                    {
                        DeviceIndex = report[1];
                        if (State != DeviceConnectionState.TouchpadActive)
                        {
                            SetState(DeviceConnectionState.TouchpadActive);
                        }
                    }
                    // 4. Raw touch reports (0x11, DeviceIndex, RawXyFeatureIndex ...)
                    else if (report[0] == 0x11)
                    {
                        if (State != DeviceConnectionState.TouchpadActive)
                        {
                            SetState(DeviceConnectionState.TouchpadActive);
                        }
                    }

                    ReportReceived?.Invoke(report);
                }
            }
            catch (TimeoutException)
            {
                // Normal timeout on idle
            }
            catch (IOException ex) when (ex.Message.Contains("Operation failed after some time", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            {
                // Normal HidSharp timeout on idle
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                {
                    Log($"Read stream error (receiver unplugged?): {ex.Message}");
                    _streamFaulted = true;
                    _deviceEvent.Set();
                    break;
                }
            }
        }
    }

    private void Log(string message)
    {
        LogMessage?.Invoke(message);
    }

    public void Dispose()
    {
        DeviceList.Local.Changed -= OnDeviceListChanged;
        _globalCts?.Cancel();
        _deviceEvent.Set();

        TearDownStream();

        _globalCts?.Dispose();
        _deviceEvent.Dispose();
    }
}
