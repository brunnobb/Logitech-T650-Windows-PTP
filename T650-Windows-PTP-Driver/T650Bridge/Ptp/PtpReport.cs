using System.Runtime.InteropServices;
using T650Bridge.Touch;

namespace T650Bridge.Ptp;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PtpContactReport
{
    public byte Flags; // Bit 0: Tip Switch, Bit 1: In Range, Bit 2: Confidence
    public byte ContactId;
    public ushort X;
    public ushort Y;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PtpTouchReport
{
    public byte ReportId; // 0x01
    public PtpContactReport Contact0;
    public PtpContactReport Contact1;
    public PtpContactReport Contact2;
    public PtpContactReport Contact3;
    public PtpContactReport Contact4;
    public ushort ScanTime; // 100 microsecond increments
    public byte ContactCount;
    public byte ButtonState; // Bit 0: Button 1

    public static PtpTouchReport FromTouchFrame(TouchFrame frame, ushort scanTime)
    {
        var report = new PtpTouchReport
        {
            ReportId = 0x01,
            ScanTime = scanTime,
            ContactCount = (byte)Math.Min(frame.Contacts.Count, 5),
            ButtonState = (byte)(frame.PhysicalButtonPressed ? 0x01 : 0x00)
        };

        for (int i = 0; i < frame.Contacts.Count && i < 5; i++)
        {
            var c = frame.Contacts[i];
            byte flags = 0;
            if (c.IsTouching) flags |= 0x01; // Tip Switch
            flags |= 0x02; // In Range
            flags |= 0x04; // Confidence (Valid touch)

            var contactRep = new PtpContactReport
            {
                Flags = flags,
                ContactId = c.Id,
                X = c.X,
                Y = c.Y
            };

            switch (i)
            {
                case 0: report.Contact0 = contactRep; break;
                case 1: report.Contact1 = contactRep; break;
                case 2: report.Contact2 = contactRep; break;
                case 3: report.Contact3 = contactRep; break;
                case 4: report.Contact4 = contactRep; break;
            }
        }

        return report;
    }

    public byte[] ToBytes()
    {
        byte[] arr = new byte[Marshal.SizeOf<PtpTouchReport>()];
        IntPtr ptr = Marshal.AllocHGlobal(arr.Length);
        try
        {
            Marshal.StructureToPtr(this, ptr, true);
            Marshal.Copy(ptr, arr, 0, arr.Length);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
        return arr;
    }
}
