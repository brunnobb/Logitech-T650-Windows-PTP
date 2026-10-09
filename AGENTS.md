# Logitech T650 Windows Precision Touchpad (PTP) Project

> **Note:** The step-by-step development strategy and task breakdown can be found in [DEVELOPMENT_PLAN.MD](./DEVELOPMENT_PLAN.MD).

## Project Overview

The goal of this project is to create a modern Windows Precision Touchpad (PTP) driver for the Logitech T650. While the T650 works flawlessly with multi-touch gestures on Linux (thanks to the kernel's `hid-logitech-hidpp` driver), on Windows it defaults to legacy mouse emulation and relies on outdated software (SetPoint) that translates gestures into legacy keyboard shortcuts. This project aims to bridge the raw multi-touch data from the device directly into the modern Windows input stack.

## System Architecture: The 2-Tier Stack

The most maintainable and stable design decouples device communication from the Windows HID subsystem:

```text
┌────────────────────────────────────────────────────────┐
│  Tier 1: Hardware Bridge Daemon (C# or Modern C++)     │
│  - Reads raw bytes from Logitech Unifying via Win32 HID│
│  - Handles HID++ 2.0 handshake & keeps connection alive│
│  - Decodes 20-byte multi-touch reports                 │
└──────────────────────────┬─────────────────────────────┘
                           │ IPC / IOCTL (DeviceIoControl)
┌──────────────────────────▼─────────────────────────────┐
│  Tier 2: Virtual PTP Injector (UMDF 2 / VHF in C++)    │
│  - Registers Microsoft PTP TLC Report Descriptor       │
│  - Exposes Virtual HID device to Windows Touch Stack   │
│  - Passes formatted PTP input frames to OS             │
└────────────────────────────────────────────────────────┘
```

## Tooling & Technology Stack Breakdown

| Component | Recommended Technology | Why It’s the Best Choice |
| :--- | :--- | :--- |
| **IDE / Build Chain** | Visual Studio 2022 + WDK 10/11 | Required for compiling and packaging Windows Driver Framework (WDF) projects and INF catalogs. |
| **Driver Framework** | UMDF 2 (User-Mode Driver Framework) | Runs inside `WUDFHost.exe` (User space). A bug or null pointer causes an application crash, never a Blue Screen of Death (BSOD). |
| **Virtual HID API** | VHF (Virtual HID Framework) | Microsoft’s built-in framework specifically designed for virtual input devices. Eliminates writing complex IRP queues from scratch. |
| **Bridge Service** | C# (.NET 8/9) or Modern C++20 | C# with HidSharp or native P/Invoke handles the Logitech protocol cleanly with high developer velocity. |
| **Debugger** | WinDbg (Local / User-mode) | You can attach WinDbg directly to `WUDFHost.exe` just like debugging a standard desktop application. |
| **Testing Environment** | Hyper-V / VMware Workstation | Enable `bcdedit /set testsigning on` in a guest VM to install test-signed drivers without compromising host security. |

## Open Source References

We will leverage two major open-source solutions to avoid reinventing the wheel:

1.  **Linux Kernel Driver (`hid-logitech-hidpp.c`)**
    *   **Purpose:** Reverse-engineering the Logitech HID++ protocol.
    *   **Logic:** Ping the receiver, query Feature `0x6100` (Touchpad Raw XY), and send the command byte `[0x10, device_index, feature_index, 0x00, 0x03, ...]` to switch the device out of standard mouse emulation into raw multi-touch streaming mode.
    *   **Packet Structure:** Parses 20-byte HID++ packets into contact counts, finger IDs, status bits (down/up/hover), and 12-bit/16-bit X/Y coordinates.
2.  **`mac-precision-touchpad` (by imbushuo)**
    *   **Purpose:** The blueprint for the Windows Virtual PTP Driver (UMDF/KMDF).
    *   **Logic:** Provides the exact Microsoft PTP TLC (Top-Level Collection) HID Report Descriptor.
    *   **Data Structures:** Contains the required `PTP_TOUCH_REPORT` structure including tip switch, confidence bits, scan times, and contact IDs.

## Core Technical Challenges to Solve

1.  **Hardware Reverse-Engineering (HID++ 2.0):** T650 does not speak standard USB HID. It speaks Logitech HID++ 2.0 over the Unifying receiver (0x046d / 0xc52b). We must send the specific feature activation command to bypass on-device gesture synthesis.
2.  **Strict Windows PTP Validation:** Windows expects precise compliance. Dropped frames or timestamp jitters (common over 2.4 GHz) can disable gesture recognition. Scan timestamps must increment consistently using `QueryPerformanceCounter`.
3.  **Driver Signing Constraints:** Running a custom virtual HID miniport requires a test-signed environment (`testsigning on`) for local development, as the PTP framework does not accept unsigned driver injections natively without a pre-signed virtual HID bus bridge.
