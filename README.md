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

### Tier 2: Virtual PTP Driver (`VirtualPtpDriver`) — **WORKING & TESTED**
- User-Mode Driver Framework (UMDF 2.15) virtual HID minidriver targeting all versions of Windows 10 & 11.
- Implements official Microsoft Windows Precision Touchpad (PTP) TLC Report Descriptor with 5 simultaneous contacts and ClickPad support.
- Ultra-low latency Named Pipe IPC (`\\.\pipe\T650VirtualPtpPipe`) bypassing kernel class driver symbolic link restrictions with zero-allocation streaming.
- Unlocks native Windows Precision Touchpad Settings in `Settings -> Bluetooth & devices -> Touchpad`.

---

## Project Structure

```text
├── T650-Windows-PTP-Driver/
│   ├── T650Bridge/               # Tier 1: .NET C# Hardware Bridge Daemon
│   │   ├── Hidpp/                # Logitech Unifying HID++ 2.0 communication
│   │   ├── Touch/                # Frame reassembler & coordinate parsing
│   │   ├── Gesture/              # Precision Gesture Engine
│   │   ├── Ptp/                  # Microsoft PTP touch report builders & IPC client
│   │   └── Program.cs            # Entry point & interactive desktop attach
│   └── VirtualPtpDriver/         # Tier 2: UMDF 2 Virtual PTP HID Driver (C++)
│       ├── PtpDescriptor.h       # Microsoft PTP TLC HID Report Descriptor
│       ├── PtpTypes.h            # IOCTL & PTP data structures
│       ├── PipeServer.cpp/.h     # Low-latency Named Pipe IPC server
│       ├── Driver.cpp / Device.cpp / Queue.cpp
│       └── VirtualPtpDriver.inx  # Driver INF file
├── Installer/                    # WiX 3.14 MSI installer packages & scripts
│   ├── Build-All-Installers.ps1  # Automated WiX builder for all MSIs
│   └── Bump-Version.ps1          # Unified version bump and recompile script
├── Dist/                         # Built MSI release packages & SHA256 checksums
├── DEVELOPMENT_PLAN.MD           # Detailed development roadmap & phase tracking
├── AGENTS.md                     # Architecture, driver internals & AI guide
└── .gitignore                    # Git exclusions
```

---

## How to Build

### Prerequisites
* Windows 10 (1903+) or Windows 11 (x64)
* [.NET 10 SDK](https://dotnet.microsoft.com/download) (or .NET 9.0/8.0)
* Visual Studio 2022 (Community or higher) with:
  * *.NET desktop development*
  * *Desktop development with C++*
  * *Windows Driver Kit (WDK 10)*
* [WiX Toolset v3.14](https://wixtoolset.org/) (for building MSI installers)
* PowerShell 7 (`pwsh`)

---

### Option A: One-Step Automated Build (Recommended)
You can compile the driver, sign it with your test certificate, compile both bridge flavors, and build all 3 MSI installers with a single command:

```powershell
# Bumps version across all source files, compiles everything, and creates installers:
pwsh -File "Installer\Bump-Version.ps1" -NewVersion "1.1.6"
```
The output installers will be placed in `Dist\`:
* `Dist\VirtualPtpDriver-Setup-v1.1.6.msi`
* `Dist\T650Bridge-PTP-Setup-v1.1.6.msi`
* `Dist\T650Bridge-Admin-Setup-v1.1.6.msi`
* `Dist\SHA256SUMS.txt`

---

### Option B: Building Components Individually

#### 1. Building the Virtual PTP Driver (C++ UMDF 2)
```powershell
# Compile the driver using MSBuild (Release x64):
$msbuild = "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
& $msbuild "T650-Windows-PTP-Driver\VirtualPtpDriver\VirtualPtpDriver.vcxproj" /p:Configuration=Release /p:Platform=x64 /t:Rebuild

# Copy the compiled DLL to the driver package staging directory:
Copy-Item "T650-Windows-PTP-Driver\VirtualPtpDriver\bin\Release\VirtualPtpDriver.dll" "T650-Windows-PTP-Driver\VirtualPtpDriver\Package\VirtualPtpDriver.dll" -Force

# Generate the driver catalog file:
$inf2cat = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x86\Inf2Cat.exe"
& $inf2cat /driver:"T650-Windows-PTP-Driver\VirtualPtpDriver\Package" /os:10_X64,Server10_X64

# Sign the driver binary and catalog (test signing):
$signtool = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe"
$thumbprint = "737386A00937ED1A64A90628A02C8850D1FA5A40"
& $signtool sign /sha1 $thumbprint /fd SHA256 /v "T650-Windows-PTP-Driver\VirtualPtpDriver\Package\VirtualPtpDriver.dll"
& $signtool sign /sha1 $thumbprint /fd SHA256 /v "T650-Windows-PTP-Driver\VirtualPtpDriver\Package\virtualptpdriver.cat"
```

#### 2. Building the C# Bridge Daemon (.NET 10)
The bridge can be built in two flavors:
* **Precision Touchpad (PTP) Edition:** Communicates with the Virtual PTP driver over the named pipe to feed native Windows Precision Touchpad events.
* **Admin Edition:** Standalone edition with Microsoft `uiAccess="true"` (UIAccess privilege) that synthesizes inputs using Windows `SendInput` with native UIPI bypass across all admin windows.

```powershell
# Build PTP Edition:
dotnet build "T650-Windows-PTP-Driver\T650Bridge" -c Release -p:Flavor=Ptp

# Build Admin Edition:
dotnet build "T650-Windows-PTP-Driver\T650Bridge" -c Release -p:Flavor=Admin
```

To publish self-contained or framework-dependent executables:
```powershell
dotnet publish "T650-Windows-PTP-Driver\T650Bridge" -c Release -p:Flavor=Ptp -r win-x64 --no-self-contained -o "Installer\T650Bridge\bin\publish-ptp"
dotnet publish "T650-Windows-PTP-Driver\T650Bridge" -c Release -p:Flavor=Admin -r win-x64 --no-self-contained -o "Installer\T650Bridge\bin\publish-admin"
```

#### 3. Building the WiX MSI Installers
```powershell
pwsh -File "Installer\Build-All-Installers.ps1" -Version "1.1.6"
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
