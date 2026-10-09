namespace T650Bridge.Touch;

public enum ContactStatus : byte
{
    None = 0,
    Touch = 1,
    Hover = 2
}

public struct TouchContact
{
    public byte Id;
    public ContactStatus Status;
    public byte Type;
    public ushort X;
    public ushort Y;
    public byte Area;
    public bool IsTouching => Id != 0;
}

public class TouchFrame
{
    public ushort HardwareTimestamp { get; set; }
    public long SystemTimestampMicroseconds { get; set; }
    public byte DeclaredFingerCount { get; set; }
    public bool PhysicalButtonPressed { get; set; }
    public List<TouchContact> Contacts { get; } = new();
}
