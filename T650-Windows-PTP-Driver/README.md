# Logitech T650 Windows Precision Touchpad (PTP) Driver

This project implements a full Precision Touchpad (PTP) pipeline for the **Logitech T650 Wireless Rechargeable Touchpad** on Windows 10 and 11.

---

## 1. Project Overview & Architecture

The architecture decouples the proprietary 2.4 GHz Logitech Unifying receiver communication from the Windows HID / Precision Touchpad subsystem using a **2-Tier Stack**:

```text
┌────────────────────────────────────────────────────────────────────────┐
│  Tier 1: Hardware Bridge Daemon (C# .NET 10 / T650Bridge)               │
│  - Connects to Logitech Unifying Receiver (VID 0x046D, PID 0xC52B)     │
│  - Automatically discovers Feature 0x6100 (TOUCHPAD_RAW_XY)            │
│  - Issues HID++ 2.0 unlock sequence: setRawReportState(0x05)           │
│  - Maintains radio heartbeat every 2.5s to prevent autosuspend         │
│  - Assembles multi-packet frames across 20-byte reports                │
│  - Inverts hardware Y axis and scales coordinates (0..2832 x 0..2364)  │
│  - Packs data into Microsoft PTP TLC Touch Reports                     │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │ IOCTL / IPC
┌───────────────────────────────────▼────────────────────────────────────┐
│  Tier 2: Virtual PTP Driver (UMDF 2 / VirtualPtpDriver)                │
│  - Registers Microsoft PTP TLC (Digitizer / Touch Pad 0x0D:0x05)       │
│  - Exposes 5 contact collections (Tip Switch, In-Range, Confidence)    │
│  - Injects native touch frames directly into Windows Touch Stack       │
└────────────────────────────────────────────────────────────────────────┘
```

---

## 2. Directory Structure

```text
T650-Windows-PTP-Driver/
├── T650Bridge/                  # Tier 1: Hardware Bridge Daemon (.NET 10)
│   ├── Hidpp/
│   │   └── UnifyingReceiver.cs  # Direct HID++ receiver protocol & unlock handshake
│   ├── Touch/
│   │   ├── TouchFrame.cs        # Contact models & frame abstractions
│   │   └── FrameAssembler.cs    # 14-bit coordinate unpacking & report multi-buffering
│   ├── Ptp/
│   │   └── PtpReport.cs         # Microsoft PTP TLC structure definitions
│   ├── Gesture/
│   │   └── PrecisionGestureEngine.cs # Precision gesture mapper & smooth scroll engine
│   ├── Program.cs               # Entry point & interactive diagnostic console
│   └── T650Bridge.csproj
├── VirtualPtpDriver/            # Tier 2: UMDF 2 Virtual PTP Driver (C++)
│   ├── PtpDescriptor.h          # Validated Microsoft PTP HID Report Descriptor
│   ├── PtpTypes.h               # PTP Report structures & IOCTL definition
│   ├── Driver.h / Driver.cpp    # WDF DriverEntry & EvtDeviceAdd
│   ├── Device.h / Device.cpp    # Device context & interface registration
│   ├── Queue.h / Queue.cpp      # I/O queue & report injection handler
│   └── VirtualPtpDriver.inx     # Driver installation INF
└── README.md
```

---

## 3. Quick Start & Testing

### Running the Live Bridge Daemon
You can run the bridge immediately in your terminal:

```powershell
cd c:\Workspace\T650-Windows-PTP\T650-Windows-PTP-Driver\T650Bridge
dotnet run -c Release
```

### What You Will See:
1. **Receiver Detection:** Finds the Logitech Unifying receiver interface.
2. **Runtime Resolution:** Resolves Feature `0x6100` (`TOUCHPAD_RAW_XY`) index dynamically.
3. **Hardware Unlock:** Sends the `0x05` raw multi-touch enable packet.
4. **Interactive Dashboard:**
   ```text
   [Frame #00142] Active: 2/5         | Contacts: F0:(1420, 1100), F1:(1680, 1150)
   ```
5. **Supported Gestures (Out of the box):**
   - **Smooth 2-Finger Scrolling:** Natural direction with pixel-level responsiveness.
   - **Horizontal Scrolling:** 2-finger sideways pan.
   - **3-Finger Swipe UP:** Opens Windows Task View (`Win + Tab`).
   - **3-Finger Swipe DOWN:** Shows Desktop (`Win + D`).
   - **3-Finger Swipe LEFT / RIGHT:** Fast App Switcher (`Alt + Tab`).
   - **4-Finger Swipe LEFT / RIGHT:** Switches Windows Virtual Desktops (`Ctrl + Win + Left/Right`).
   - **Physical Click:** Full hardware glass click-down drag support.

---

## 4. Hardware Protocol Reference (Verified on Hardware)

| Property | Value | Notes |
|---|---|---|
| Receiver VID:PID | `0x046D` : `0xC52B` | Logitech Unifying Receiver |
| Target Interface | Interface 2, Usage 2 | The only interface accepting 20-byte output reports |
| Touchpad Feature | `0x6100` (`TOUCHPAD_RAW_XY`) | Resolved dynamically at runtime (typically index `0x0F` or `0x10`) |
| Unlock Packet | `[0x11, 0x01, <feat_idx>, 0x2E, 0x05, 0x00 ...]` | Function 2 (`setRawReportState`), params `0x05` |
| X Resolution | 0 .. 2832 | 14-bit Big-Endian: `((b[0] & 0x3F) << 8) \| b[1]` |
| Y Resolution | 0 .. 2364 | 14-bit Big-Endian: Inverted `2364 - (((b[2] & 0x3F) << 8) \| b[3])` |
| Report Rate | ~125 Hz | Delivered across unsolicited `0x11` long reports |
| Multi-touch Framing | Buffered until `end_of_frame == 1` | `ceil(n / 2)` reports per frame |
