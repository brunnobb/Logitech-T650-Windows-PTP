using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace T650Bridge.Ptp;

public class PtpDriverClient : IDisposable
{
    // GUID_DEVINTERFACE_T650_VIRTUAL_PTP: {A50E5B44-5D88-4A56-BE34-F18A56DF762B}
    public static readonly Guid InterfaceGuid = new("A50E5B44-5D88-4A56-BE34-F18A56DF762B");

    private const string PipeName = "T650VirtualPtpPipe";
    private const uint IOCTL_PTP_INJECT_REPORT = 0x00222004; // CTL_CODE(FILE_DEVICE_UNKNOWN, 0x801, METHOD_BUFFERED, FILE_ANY_ACCESS)

    private NamedPipeClientStream? _pipeStream;
    private SafeFileHandle? _handle;

    public bool IsConnected => (_pipeStream != null && _pipeStream.IsConnected) || (_handle != null && !_handle.IsInvalid);

    private static void Log(string message, System.Diagnostics.EventLogEntryType type = System.Diagnostics.EventLogEntryType.Information)
    {
        Console.WriteLine($"[PTP] {message}");

        try
        {
            if (!System.Diagnostics.EventLog.SourceExists("Logitech T650 Bridge"))
            {
                try { System.Diagnostics.EventLog.CreateEventSource("Logitech T650 Bridge", "Application"); } catch { }
            }
            System.Diagnostics.EventLog.WriteEntry("Logitech T650 Bridge", $"[PTP] {message}", type);
        }
        catch
        {
            try { System.Diagnostics.EventLog.WriteEntry("Application", $"[Logitech T650 Bridge] {message}", type); } catch { }
        }

        try
        {
            Directory.CreateDirectory(@"C:\ProgramData\LogitechT650");
            File.AppendAllText(@"C:\ProgramData\LogitechT650\T650Bridge.log",
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}\r\n");
        }
        catch { }
    }

    public bool Connect()
    {
        // 1. First probe high-performance Named Pipe IPC
        Log("Probing for Virtual PTP Named Pipe (\\\\.\\pipe\\T650VirtualPtpPipe)...");
        try
        {
            var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.None);
            pipe.Connect(500); // 500ms timeout
            _pipeStream = pipe;
            Console.ForegroundColor = ConsoleColor.Green;
            Log("Successfully connected to Virtual PTP Driver via Named Pipe (\\\\.\\pipe\\T650VirtualPtpPipe)!");
            Console.ResetColor();
            return true;
        }
        catch (TimeoutException)
        {
            Log("Named Pipe probe timed out. Falling back to Device Interface...");
        }
        catch (FileNotFoundException)
        {
            Log("Named Pipe not found. Falling back to Device Interface...");
        }
        catch (Exception ex)
        {
            Log($"Named Pipe probe failed: {ex.Message}. Falling back to Device Interface...");
        }

        // 2. Fallback: probe SetupDi Device Interface and IOCTL handle
        Log($"Probing for Virtual PTP device interface {{{InterfaceGuid}}}...");
        try
        {
            string? devicePath = FindDevicePath(InterfaceGuid);
            if (string.IsNullOrEmpty(devicePath))
            {
                Log("Virtual PTP device interface was NOT found.", System.Diagnostics.EventLogEntryType.Warning);
                DiagnoseDeviceNode();
                return false;
            }

            Log($"Found device interface: {devicePath}");

            // Try opening with standard Read/Write, Write-only, and Query (0) access
            (uint Access, string Name)[] attempts = [
                (FileAccessGenericRead | FileAccessGenericWrite, "GENERIC_READ | GENERIC_WRITE"),
                (FileAccessGenericWrite, "GENERIC_WRITE"),
                (0, "QUERY_ACCESS (0 / DeviceControl)")
            ];

            foreach (var (access, modeName) in attempts)
            {
                Log($"Attempting CreateFile with mode '{modeName}'...");
                _handle = CreateFile(
                    devicePath,
                    access,
                    FileShareRead | FileShareWrite,
                    IntPtr.Zero,
                    CreationDispositionOpenExisting,
                    FileAttributeNormal,
                    IntPtr.Zero
                );

                if (!_handle.IsInvalid)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Log($"Successfully connected to Virtual PTP Driver via {modeName} (Handle: 0x{_handle.DangerousGetHandle():X})!");
                    Console.ResetColor();
                    return true;
                }

                int error = Marshal.GetLastWin32Error();
                Log($"CreateFile ({modeName}) failed with Win32 Error {error} (0x{error:X8}).", System.Diagnostics.EventLogEntryType.Warning);
                if (error == 5)
                {
                    Log("-> Access Denied (Error 5): Run as Administrator or check driver permissions.", System.Diagnostics.EventLogEntryType.Warning);
                }
                else if (error == 31)
                {
                    Log("-> Gen Failure (Error 31 / STATUS_UNSUCCESSFUL): Driver rejected create or device needs restart.", System.Diagnostics.EventLogEntryType.Warning);
                }
            }

            Log("All CreateFile access modes failed. Virtual PTP Driver could not be opened.", System.Diagnostics.EventLogEntryType.Error);
            return false;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Log($"Unexpected exception connecting to Virtual PTP driver: {ex.Message}", System.Diagnostics.EventLogEntryType.Error);
            Console.ResetColor();
            return false;
        }
    }

    public bool InjectReport(PtpTouchReport report)
    {
        if (_pipeStream != null && _pipeStream.IsConnected)
        {
            try
            {
                ReadOnlySpan<byte> reportBytes = MemoryMarshal.AsBytes(new ReadOnlySpan<PtpTouchReport>(in report));
                _pipeStream.Write(reportBytes);
                return true;
            }
            catch (Exception ex)
            {
                Log($"Named pipe write error: {ex.Message}", System.Diagnostics.EventLogEntryType.Warning);
                try { _pipeStream.Dispose(); } catch { }
                _pipeStream = null;
                return false;
            }
        }

        if (_handle != null && !_handle.IsInvalid)
        {
            int size = Marshal.SizeOf<PtpTouchReport>();
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(report, ptr, false);
                return DeviceIoControl(
                    _handle,
                    IOCTL_PTP_INJECT_REPORT,
                    ptr,
                    (uint)size,
                    IntPtr.Zero,
                    0,
                    out _,
                    IntPtr.Zero
                );
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        return false;
    }

    public void Dispose()
    {
        try { _pipeStream?.Dispose(); } catch { }
        _pipeStream = null;
        _handle?.Dispose();
        _handle = null;
    }

    #region Win32 Device Discovery & IOCTL
    private const uint FileAccessGenericRead = 0x80000000;
    private const uint FileAccessGenericWrite = 0x40000000;
    private const uint FileShareRead = 1;
    private const uint FileShareWrite = 2;
    private const uint CreationDispositionOpenExisting = 3;
    private const uint FileAttributeNormal = 0x80;

    private const int DIGCF_PRESENT = 0x00000002;
    private const int DIGCF_ALLCLASSES = 0x00000004;
    private const int DIGCF_DEVICEINTERFACE = 0x00000010;

    private const uint SPDRP_DEVICEDESC = 0x00000000;
    private const uint SPDRP_HARDWAREID = 0x00000001;
    private const uint SPDRP_FRIENDLYNAME = 0x0000000C;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        uint nInBufferSize,
        IntPtr lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(
        ref Guid ClassGuid,
        IntPtr Enumerator,
        IntPtr hwndParent,
        int Flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(
        IntPtr ClassGuid,
        string? Enumerator,
        IntPtr hwndParent,
        int Flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr DeviceInfoSet,
        IntPtr DeviceInfoData,
        ref Guid InterfaceClassGuid,
        int MemberIndex,
        ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(
        IntPtr DeviceInfoSet,
        ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData,
        IntPtr DeviceInterfaceDetailData,
        int DeviceInterfaceDetailDataSize,
        ref int RequiredSize,
        IntPtr DeviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(
        IntPtr DeviceInfoSet,
        int MemberIndex,
        ref SP_DEVINFO_DATA DeviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetupDiGetDeviceRegistryProperty(
        IntPtr DeviceInfoSet,
        ref SP_DEVINFO_DATA DeviceInfoData,
        uint Property,
        out uint PropertyRegDataType,
        byte[] PropertyBuffer,
        uint PropertyBufferSize,
        out uint RequiredSize);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool SetupDiGetDeviceInstanceId(
        IntPtr DeviceInfoSet,
        ref SP_DEVINFO_DATA DeviceInfoData,
        StringBuilder DeviceInstanceId,
        int DeviceInstanceIdSize,
        out int RequiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

    [DllImport("cfgmgr32.dll", SetLastError = true)]
    private static extern int CM_Get_DevNode_Status(
        out uint pulStatus,
        out uint pulProblemNumber,
        uint dnDevInst,
        uint ulFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA
    {
        public int cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    private static string? FindDevicePath(Guid guid)
    {
        IntPtr hDevInfo = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (hDevInfo == IntPtr.Zero || hDevInfo.ToInt64() == -1)
        {
            int err = Marshal.GetLastWin32Error();
            Console.WriteLine($"[PTP] SetupDiGetClassDevs failed with Win32 Error: {err} (0x{err:X8}).");
            return null;
        }

        try
        {
            var dia = new SP_DEVICE_INTERFACE_DATA();
            dia.cbSize = Marshal.SizeOf(dia);

            if (!SetupDiEnumDeviceInterfaces(hDevInfo, IntPtr.Zero, ref guid, 0, ref dia))
            {
                int err = Marshal.GetLastWin32Error();
                Console.WriteLine($"[PTP] SetupDiEnumDeviceInterfaces found no active interface for GUID {{{guid}}}. Win32 Error: {err} (0x{err:X8}).");
                return null;
            }

            int reqSize = 0;
            SetupDiGetDeviceInterfaceDetail(hDevInfo, ref dia, IntPtr.Zero, 0, ref reqSize, IntPtr.Zero);
            if (reqSize <= 0)
            {
                int err = Marshal.GetLastWin32Error();
                Console.WriteLine($"[PTP] SetupDiGetDeviceInterfaceDetail size probe failed. Win32 Error: {err}.");
                return null;
            }

            IntPtr pDetail = Marshal.AllocHGlobal(reqSize);
            try
            {
                // cbSize on x64 is 8 bytes, on x86 is 4 + SystemDefaultCharSize
                Marshal.WriteInt32(pDetail, IntPtr.Size == 8 ? 8 : 4 + Marshal.SystemDefaultCharSize);
                if (SetupDiGetDeviceInterfaceDetail(hDevInfo, ref dia, pDetail, reqSize, ref reqSize, IntPtr.Zero))
                {
                    IntPtr pPath = new(pDetail.ToInt64() + 4);
                    string? path = Marshal.PtrToStringAuto(pPath);
                    Console.WriteLine($"[PTP] SetupDiGetDeviceInterfaceDetail resolved path: {path}");
                    return path;
                }
                else
                {
                    int err = Marshal.GetLastWin32Error();
                    Console.WriteLine($"[PTP] SetupDiGetDeviceInterfaceDetail (data) failed. Win32 Error: {err}.");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(pDetail);
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(hDevInfo);
        }

        return null;
    }

    private static void DiagnoseDeviceNode()
    {
        Console.WriteLine("[PTP Diag] Probing Windows Device Manager for 'Root\\T650VirtualPtp'...");
        IntPtr hDevInfo = SetupDiGetClassDevs(IntPtr.Zero, null, IntPtr.Zero, DIGCF_ALLCLASSES);
        if (hDevInfo == IntPtr.Zero || hDevInfo.ToInt64() == -1)
        {
            Console.WriteLine("[PTP Diag] Failed to enumerate device nodes (SetupDiGetClassDevs failed).");
            return;
        }

        bool deviceFound = false;
        try
        {
            var devData = new SP_DEVINFO_DATA();
            devData.cbSize = Marshal.SizeOf(devData);

            for (int i = 0; SetupDiEnumDeviceInfo(hDevInfo, i, ref devData); i++)
            {
                string? hwId = GetDevicePropertyString(hDevInfo, ref devData, SPDRP_HARDWAREID);
                string? desc = GetDevicePropertyString(hDevInfo, ref devData, SPDRP_DEVICEDESC) 
                            ?? GetDevicePropertyString(hDevInfo, ref devData, SPDRP_FRIENDLYNAME);

                var instanceSb = new StringBuilder(512);
                SetupDiGetDeviceInstanceId(hDevInfo, ref devData, instanceSb, instanceSb.Capacity, out _);
                string instanceId = instanceSb.ToString();

                bool isMatch = (!string.IsNullOrEmpty(hwId) && hwId.Contains("T650", StringComparison.OrdinalIgnoreCase))
                            || (!string.IsNullOrEmpty(desc) && desc.Contains("T650", StringComparison.OrdinalIgnoreCase))
                            || (!string.IsNullOrEmpty(instanceId) && instanceId.Contains("T650", StringComparison.OrdinalIgnoreCase));

                if (isMatch)
                {
                    deviceFound = true;
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"[PTP Diag] Found Device Node in Device Manager:");
                    Console.WriteLine($"[PTP Diag]   - Name:        {desc ?? "(Unknown)"}");
                    Console.WriteLine($"[PTP Diag]   - Instance ID: {instanceId}");
                    Console.WriteLine($"[PTP Diag]   - Hardware ID: {hwId ?? "(None)"}");

                    int cr = CM_Get_DevNode_Status(out uint status, out uint problem, devData.DevInst, 0);
                    if (cr == 0) // CR_SUCCESS
                    {
                        Console.WriteLine($"[PTP Diag]   - DevNode Status: 0x{status:X8} | Problem Code: {problem} (0x{problem:X2})");
                        ExplainProblemCode(problem, status);
                    }
                    else
                    {
                        Console.WriteLine($"[PTP Diag]   - CM_Get_DevNode_Status returned CR code: {cr}");
                    }
                    Console.ResetColor();
                }
            }

            if (!deviceFound)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[PTP Diag] No device node matching 'T650' or 'Root\\T650VirtualPtp' found in Device Manager.");
                Console.WriteLine("[PTP Diag] -> The Virtual PTP device has not been registered in Windows.");
                Console.WriteLine("[PTP Diag] -> Solution: Run 'C:\\Program Files\\Logitech T650 PTP\\Driver\\InstallAction.cmd' as Administrator.");
                Console.ResetColor();
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(hDevInfo);
        }
    }

    private static void ExplainProblemCode(uint problem, uint status)
    {
        switch (problem)
        {
            case 0:
                Console.WriteLine("[PTP Diag]   -> Device Status: OK (Working properly in Device Manager).");
                Console.WriteLine("[PTP Diag]   -> Notice: Device is running, but interface GUID was not published.");
                Console.WriteLine("[PTP Diag]      Possible reasons: Driver DLL failed inside WUDFHost, or device is not in working D0 state.");
                break;
            case 10: // CM_PROB_FAILED_START
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[PTP Diag]   -> Problem Code 10 (CM_PROB_FAILED_START): This device cannot start.");
                Console.WriteLine("[PTP Diag]      The UMDF 2 driver (VirtualPtpDriver.dll) failed during DriverEntry or EvtDeviceAdd.");
                Console.WriteLine("[PTP Diag]      Check Event Viewer -> Applications and Services Logs -> Microsoft -> Windows -> DriverFrameworks-UserMode -> Operational");
                break;
            case 14: // CM_PROB_NEED_RESTART
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[PTP Diag]   -> Problem Code 14 (CM_PROB_NEED_RESTART): Restart Required.");
                Console.WriteLine("[PTP Diag]      Windows requires a computer restart before this device can start.");
                break;
            case 22: // CM_PROB_DISABLED
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[PTP Diag]   -> Problem Code 22 (CM_PROB_DISABLED): Device is disabled in Device Manager.");
                Console.WriteLine("[PTP Diag]      Right-click the device in Device Manager and select 'Enable device'.");
                break;
            case 31: // CM_PROB_FAILED_ADD
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[PTP Diag]   -> Problem Code 31 (CM_PROB_FAILED_ADD): Windows cannot load drivers for this device.");
                break;
            case 52: // CM_PROB_UNSIGNED_DRIVER
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[PTP Diag]   -> Problem Code 52 (CM_PROB_UNSIGNED_DRIVER): Digital signature not verified!");
                Console.WriteLine("[PTP Diag]      Windows blocked the driver because test-signing is not active or Secure Boot is enabled.");
                Console.WriteLine("[PTP Diag]      Solution:");
                Console.WriteLine("[PTP Diag]        1. If VM has UEFI Secure Boot enabled, disable Secure Boot in VM settings.");
                Console.WriteLine("[PTP Diag]        2. Run 'bcdedit /set testsigning on' in elevated cmd.");
                Console.WriteLine("[PTP Diag]        3. Reboot the VM.");
                break;
            default:
                Console.WriteLine($"[PTP Diag]   -> Problem Code {problem} (DevNode Status: 0x{status:X8}).");
                break;
        }
    }

    private static string? GetDevicePropertyString(IntPtr hDevInfo, ref SP_DEVINFO_DATA devData, uint property)
    {
        byte[] buffer = new byte[1024];
        if (SetupDiGetDeviceRegistryProperty(hDevInfo, ref devData, property, out _, buffer, (uint)buffer.Length, out uint reqSize))
        {
            if (reqSize > 2)
            {
                return Encoding.Unicode.GetString(buffer, 0, (int)reqSize).TrimEnd('\0');
            }
        }
        return null;
    }
    #endregion
}
