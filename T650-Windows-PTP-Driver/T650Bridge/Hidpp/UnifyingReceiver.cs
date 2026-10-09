using HidSharp;
using System.Diagnostics;

namespace T650Bridge.Hidpp;

public class UnifyingReceiver : IDisposable
{
    public const int LogitechVid = 0x046D;
    public const int UnifyingPid = 0xC52B;
    public const byte Swid = 0x0E; // Distinct software ID

    private HidDevice? _device;
    private HidStream? _stream;
    private CancellationTokenSource? _cts;
    private Thread? _readThread;
    private Thread? _heartbeatThread;

    public byte DeviceIndex { get; private set; } = 0x01;
    public byte RawXyFeatureIndex { get; private set; } = 0x0F;
    public bool IsConnected => _stream != null;

    public event Action<byte[]>? ReportReceived;
    public event Action<string>? LogMessage;

    public bool Connect()
    {
        Log("Enumerating Logitech Unifying receiver endpoints...");
        var devices = DeviceList.Local.GetHidDevices(LogitechVid, UnifyingPid).ToList();
        
        // Find Interface 2, Col02 (which accepts the 20-byte HID++ Output Reports)
        _device = devices.FirstOrDefault(d => 
            d.DevicePath.Contains("MI_02&Col02", StringComparison.OrdinalIgnoreCase)
        ) ?? devices.FirstOrDefault(d => 
            d.DevicePath.Contains("MI_02", StringComparison.OrdinalIgnoreCase) && d.GetMaxOutputReportLength() >= 20
        ) ?? devices.FirstOrDefault(d => d.GetMaxOutputReportLength() >= 20);

        if (_device == null)
        {
            Log("Error: Could not locate Unifying Receiver Interface 2 with 20-byte output capability.");
            return false;
        }

        Log($"Found target endpoint: {_device.DevicePath}");
        if (!_device.TryOpen(out _stream))
        {
            Log("Error: Failed to open HID stream to Unifying receiver.");
            return false;
        }

        _stream.ReadTimeout = 500;
        _stream.WriteTimeout = 500;

        // Discover and unlock T650
        ResolveTouchpadFeature();
        UnlockRawMode();

        _cts = new CancellationTokenSource();
        _readThread = new Thread(ReadLoop) { IsBackground = true, Name = "T650_ReadThread" };
        _readThread.Start();

        _heartbeatThread = new Thread(HeartbeatLoop) { IsBackground = true, Name = "T650_HeartbeatThread" };
        _heartbeatThread.Start();

        Log("Receiver connection established and streaming active.");
        return true;
    }

    private void ResolveTouchpadFeature()
    {
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
                _stream!.Write(req);
                byte[] resp = new byte[32];
                int bytesRead = _stream.Read(resp, 0, resp.Length);
                if (bytesRead >= 7 && resp[0] == 0x11 && resp[1] == DeviceIndex && resp[2] == 0x00)
                {
                    RawXyFeatureIndex = resp[4];
                    Log($"Feature 0x6100 resolved at runtime to Index 0x{RawXyFeatureIndex:X2}!");
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
                _stream!.Write(unlock);
                Thread.Sleep(30);
            }
            catch (Exception ex)
            {
                Log($"Warning during unlock write: {ex.Message}");
            }
        }
    }

    private void HeartbeatLoop()
    {
        // Gentle heartbeat every 2.5 seconds to keep the 2.4 GHz radio from sleeping
        byte[] ping = new byte[20];
        ping[0] = 0x11;
        ping[1] = DeviceIndex;
        ping[2] = 0x00;
        ping[3] = Swid;

        while (_cts != null && !_cts.IsCancellationRequested)
        {
            try
            {
                Thread.Sleep(2500);
                if (_stream != null && IsConnected)
                {
                    _stream.Write(ping);
                }
            }
            catch
            {
                // Ignore transient write errors during heartbeat
            }
        }
    }

    private void ReadLoop()
    {
        byte[] buffer = new byte[64];
        while (_cts != null && !_cts.IsCancellationRequested)
        {
            try
            {
                int read = _stream!.Read(buffer, 0, buffer.Length);
                if (read > 0)
                {
                    byte[] report = new byte[read];
                    Array.Copy(buffer, report, read);
                    ReportReceived?.Invoke(report);
                }
            }
            catch (TimeoutException)
            {
                // Normal timeout on idle
            }
            catch (Exception ex)
            {
                if (_cts != null && !_cts.IsCancellationRequested)
                {
                    Log($"Read stream error: {ex.Message}");
                    Thread.Sleep(200);
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
        _cts?.Cancel();
        _stream?.Dispose();
        _cts?.Dispose();
    }
}
