using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace T650Bridge.Ptp;

public class PtpDriverClient : IDisposable
{
    // GUID_DEVINTERFACE_T650_VIRTUAL_PTP: {A50E5B44-5D88-4A56-BE34-F18A56DF762B}
    private static readonly Guid InterfaceGuid = new("A50E5B44-5D88-4A56-BE34-F18A56DF762B");

    private const uint IOCTL_PTP_INJECT_REPORT = 0x00222004; // CTL_CODE(FILE_DEVICE_UNKNOWN, 0x801, METHOD_BUFFERED, FILE_ANY_ACCESS)

    private SafeFileHandle? _handle;

    public bool IsConnected => _handle != null && !_handle.IsInvalid;

    public bool Connect()
    {
        try
        {
            string? devicePath = FindDevicePath(InterfaceGuid);
            if (string.IsNullOrEmpty(devicePath))
                return false;

            _handle = CreateFile(
                devicePath,
                FileAccessGenericRead | FileAccessGenericWrite,
                FileShareRead | FileShareWrite,
                IntPtr.Zero,
                CreationDispositionOpenExisting,
                FileAttributeNormal,
                IntPtr.Zero
            );

            return !_handle.IsInvalid;
        }
        catch
        {
            return false;
        }
    }

    public bool InjectReport(PtpTouchReport report)
    {
        if (!IsConnected) return false;

        int size = Marshal.SizeOf<PtpTouchReport>();
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(report, ptr, false);
            return DeviceIoControl(
                _handle!,
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

    public void Dispose()
    {
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
    private const int DIGCF_DEVICEINTERFACE = 0x00000010;

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
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    private static string? FindDevicePath(Guid guid)
    {
        IntPtr hDevInfo = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (hDevInfo == IntPtr.Zero || hDevInfo.ToInt64() == -1)
            return null;

        try
        {
            var dia = new SP_DEVICE_INTERFACE_DATA();
            dia.cbSize = Marshal.SizeOf(dia);

            if (SetupDiEnumDeviceInterfaces(hDevInfo, IntPtr.Zero, ref guid, 0, ref dia))
            {
                int reqSize = 0;
                SetupDiGetDeviceInterfaceDetail(hDevInfo, ref dia, IntPtr.Zero, 0, ref reqSize, IntPtr.Zero);
                if (reqSize > 0)
                {
                    IntPtr pDetail = Marshal.AllocHGlobal(reqSize);
                    try
                    {
                        // cbSize on x64 is 8 bytes
                        Marshal.WriteInt32(pDetail, IntPtr.Size == 8 ? 8 : 4 + Marshal.SystemDefaultCharSize);
                        if (SetupDiGetDeviceInterfaceDetail(hDevInfo, ref dia, pDetail, reqSize, ref reqSize, IntPtr.Zero))
                        {
                            IntPtr pPath = new(pDetail.ToInt64() + 4);
                            return Marshal.PtrToStringAuto(pPath);
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(pDetail);
                    }
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(hDevInfo);
        }

        return null;
    }
    #endregion
}
