# Logitech T650 Windows Precision Touchpad (PTP) Driver

Modern Windows Precision Touchpad (PTP) driver and bridge stack for the **Logitech Wireless Rechargeable Touchpad T650** over the Logitech Unifying Receiver (`VID 0x046D`, `PID 0xC52B`).

---

## Background & Motivation

The Logitech T650 was released during the Windows 7 / 8 era. On modern Windows (10 and 11), Logitech's legacy software (SetPoint) is discontinued and translates multi-touch gestures on-device into legacy keyboard shortcuts (such as Charms Bar, Windows 8 Flip 3D, and browser navigation). In contrast, on Linux the device works flawlessly with multi-touch gestures via the kernel's `hid-logitech-hidpp` driver.

This project unlocks the T650's raw multi-touch sensor stream on Windows and connects it directly into the modern Windows input stack.

---

## System Architecture: The 2-Tier Stack

Device communication is decoupled from the Windows HID driver stack for stability and safety:

```text
┌────────────────────────────────────────────────────────┐
│  Tier 1: Hardware Bridge Daemon (C# .NET)              │
│  - Reads raw reports from Logitech Unifying Receiver   │
│  - Handshakes Logitech HID++ 2.0 (TOUCHPAD_RAW_XY)     │
│  - Reassembles multi-contact frames & coordinates      │
│  - High-performance gesture engine (mouse_event fallback)│
└──────────────────────────┬─────────────────────────────┘
                           │ IPC / IOCTL (DeviceIoControl)
┌──────────────────────────▼─────────────────────────────┐
│  Tier 2: Virtual PTP Injector (UMDF 2 / VHF in C++)    │
│  - Registers Microsoft PTP TLC Report Descriptor       │
│  - Exposes Virtual HID device to Windows Touch Stack   │
│  - Passes formatted PTP frames to OS                   │
│  - Unlocks native Windows 10/11 Touchpad Settings page │
└────────────────────────────────────────────────────────┘
```

---

## Current Status & Implemented Features

### Tier 1: Hardware Bridge (`T650Bridge`) — **WORKING & TESTED**
- **Protocol Unlock:** Dynamically resolves Feature `0x6100` (`TOUCHPAD_RAW_XY`) via runtime feature discovery (Feature Index `0x0F` on Device `0x01`), sending `setRawReportState(0x05)` to activate raw 14-bit streaming mode with enhanced sensitivity.
- **Heartbeat Daemon:** Keeps the 2.4 GHz Unifying connection alive without dropping into sleep.
- **Multi-Frame Reassembly:** Assembles raw 20-byte HID++ reports into complete multi-contact touch frames (supporting up to 5 simultaneous contacts).
- **Coordinate Space:** Inverts hardware lower-left origin to standard top-left screen coordinates (`0..2832` $\times$ `0..2364`).
- **Precision Gesture Engine:**
  - **1-Finger Glide:** Smooth pointer motion with velocity acceleration curves.
  - **1-Finger Tap:** Left click.
  - **Physical Button Click:** Mechanical switches at bottom of pad mapped to left down/up with drag & drop support.
  - **2-Finger Scroll:** Smooth vertical and horizontal scrolling in natural directions.
  - **2-Finger Tap:** Right click (Context menu).
  - **3-Finger Swipe Up:** Task View (`Win + Tab`).
  - **3-Finger Swipe Down:** Show Desktop (`Win + D`).
  - **3-Finger Swipe Left / Right:** App Switcher (`Alt + Tab` / `Alt + Shift + Tab`).
  - **3-Finger Tap:** Windows Search (`Win + S`).
  - **4-Finger Swipe Left / Right:** Switch Virtual Desktops (`Ctrl + Win + Left` / `Ctrl + Win + Right`).
  - **4-Finger Swipe Up / Down:** Task View (`Win + Tab`) / Action Center (`Win + A`).

### Tier 2: Virtual PTP Driver (`VirtualPtpDriver`) — **IN PROGRESS**
- User-Mode Driver Framework (UMDF 2) driver based on Microsoft Virtual HID Framework (VHF).
- Implements official Microsoft Windows Precision Touchpad (PTP) TLC Report Descriptor.
- Exposes `IOCTL_PTP_INJECT_REPORT` for direct report submission to the Windows touch subsystem.

---

## Project Structure

```text
├── T650-Windows-PTP-Driver/
│   ├── T650Bridge/               # Tier 1: .NET C# Hardware Bridge Daemon
│   │   ├── Hidpp/                # Logitech Unifying HID++ 2.0 communication
│   │   ├── Touch/                # Frame reassembler & coordinate parsing
│   │   ├── Gesture/              # Precision Gesture Engine
│   │   ├── Ptp/                  # Microsoft PTP touch report builders
│   │   └── Program.cs            # Entry point & interactive desktop attach
│   └── VirtualPtpDriver/         # Tier 2: UMDF 2 Virtual PTP HID Driver (C++)
│       ├── PtpDescriptor.h       # Microsoft PTP TLC HID Report Descriptor
│       ├── PtpTypes.h            # IOCTL & PTP data structures
│       ├── Driver.cpp / Device.cpp / Queue.cpp
│       └── VirtualPtpDriver.inx  # Driver INF file
├── DEVELOPMENT_PLAN.MD           # Detailed development roadmap & phase tracking
├── AGENTS.md                     # Agent engineering & architecture guidelines
└── .gitignore                    # Git exclusions
```

---

## Building and Running

### Prerequisites
* Windows 10 (1903+) or Windows 11 (x64)
* [.NET 10 SDK](https://dotnet.microsoft.com/download)
* Visual Studio 2022 (Community or higher) with:
  * *.NET desktop development*
  * *Desktop development with C++*
  * *Windows Driver Kit (WDK)* (for Tier 2)

### Running Tier 1 (Hardware Bridge Daemon)
```powershell
cd T650-Windows-PTP-Driver\T650Bridge
dotnet build -c Release
dotnet run -c Release --no-build
```
Or run the compiled executable directly:
```powershell
.\bin\Release\net10.0\T650Bridge.exe
```

---

## Attributions & References

This project builds upon the research and implementations of open-source projects:

1. **`logitec-t650-macos-driver`**
   - Empirical protocol specification (`T650_PROTOCOL_SPEC.md`), bit-packing definitions, and empirical touch session models (`GestureRecognizer.swift` / `FrameAssembler.swift`).
2. **`T650drivers`**
   - Early Windows experimentation and device discovery tooling over the Unifying receiver.
3. **Linux Kernel (`hid-logitech-hidpp.c`)**
   - The foundational reference for the Logitech HID++ 2.0 protocol and Feature `0x6100` (`TOUCHPAD_RAW_XY`).
4. **`mac-precision-touchpad` (by imbushuo)**
   - The reference implementation for Windows Precision Touchpad (PTP) Top-Level Collection (TLC) HID report descriptors and WDF driver architecture.

---

## License

This project is licensed under the MIT License.
