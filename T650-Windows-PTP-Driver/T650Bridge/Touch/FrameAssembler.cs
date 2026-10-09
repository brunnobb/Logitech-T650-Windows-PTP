using System.Diagnostics;

namespace T650Bridge.Touch;

public class FrameAssembler
{
    public const ushort MaxX = 2832;
    public const ushort MaxY = 2364;

    private readonly List<TouchContact> _pendingContacts = new();
    private bool _buttonPressed;
    private byte _declaredCount;
    private ushort _lastHwTimestamp;

    public event Action<TouchFrame>? FrameReady;

    public void ProcessRawReport(ReadOnlySpan<byte> report)
    {
        if (report.Length < 20 || report[0] != 0x11 || report[3] != 0x00)
            return;

        ushort hwTimestamp = (ushort)((report[4] << 8) | report[5]);
        _lastHwTimestamp = hwTimestamp;

        // Byte 12 is payload[8] (Contact A shared byte)
        byte byte12 = report[12];
        byte fidA = (byte)((byte12 >> 4) & 0x0F);
        bool btn = ((byte12 >> 2) & 0x01) == 1;
        bool endOfFrame = (byte12 & 0x01) == 1;
        if (btn) _buttonPressed = true;

        // Byte 19 is payload[15] (Contact B shared byte)
        byte byte19 = report[19];
        byte fidB = (byte)((byte19 >> 4) & 0x0F);
        byte fingerCount = (byte)(byte19 & 0x0F);
        if (fingerCount > 0) _declaredCount = fingerCount;

        // Contact A (report bytes 6..12)
        if (fidA != 0)
        {
            var contact = ParseContact(report.Slice(6, 6), fidA);
            _pendingContacts.Add(contact);
        }

        // Contact B (report bytes 13..19)
        if (fidB != 0)
        {
            var contact = ParseContact(report.Slice(13, 6), fidB);
            _pendingContacts.Add(contact);
        }

        if (endOfFrame)
        {
            var frame = new TouchFrame
            {
                HardwareTimestamp = _lastHwTimestamp,
                SystemTimestampMicroseconds = Stopwatch.GetTimestamp() * 1_000_000L / Stopwatch.Frequency,
                DeclaredFingerCount = _declaredCount,
                PhysicalButtonPressed = _buttonPressed
            };
            frame.Contacts.AddRange(_pendingContacts);

            _pendingContacts.Clear();
            _buttonPressed = false;
            _declaredCount = 0;

            FrameReady?.Invoke(frame);
        }
    }

    private static bool IsContactActive(ReadOnlySpan<byte> b)
    {
        // If all coordinate/status bytes are 0, slot is empty
        return (b[0] | b[1] | b[2] | b[3]) != 0;
    }

    private static TouchContact ParseContact(ReadOnlySpan<byte> b, byte fingerId)
    {
        byte type = (byte)(b[0] >> 6);
        ushort rawX = (ushort)(((b[0] & 0x3F) << 8) | b[1]);
        byte statusByte = (byte)(b[2] >> 6);
        ushort rawY = (ushort)(((b[2] & 0x3F) << 8) | b[3]);
        byte area = b[5];

        // Hardware origin is lower-left; invert Y for standard top-left screen coordinates
        ushort clampedX = Math.Min(rawX, MaxX);
        ushort clampedY = rawY <= MaxY ? (ushort)(MaxY - rawY) : (ushort)0;

        return new TouchContact
        {
            Id = fingerId,
            Type = type,
            Status = (ContactStatus)statusByte,
            X = clampedX,
            Y = clampedY,
            Area = area
        };
    }
}
