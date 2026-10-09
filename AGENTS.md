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

---

## Versioning & Release Guidelines (AI Agent Reference)

When an AI prompt requests a new release, version bump, or installer update (e.g., *"bump version to 1.1.0 and build installers"*), follow these standardized specifications.

### 1. Versioning Standard

* **Human & Git Version (SemVer):** `MAJOR.MINOR.PATCH` (e.g., `1.0.0`, `1.1.0`, `1.0.1`).
* **Windows & WiX Version (4-Part):** `MAJOR.MINOR.PATCH.BUILD` (e.g., `1.0.0.0`, `1.1.0.0`).
* **Important Windows Installer Rule:** Windows Installer (MSI) only compares the first **three** fields (`MAJOR.MINOR.PATCH`) to determine if an installed version is newer. The 4th field is ignored for upgrade checks. Therefore, every release must increment at least `PATCH` or `MINOR`.

### 2. Canonical Version File Locations

When changing versions, the following **5 files** must stay synchronized:

| File | Target Element / Property | Example Value |
| :--- | :--- | :--- |
| [`T650Bridge.csproj`](file:///c:/Workspace/T650-Windows-PTP/T650-Windows-PTP-Driver/T650Bridge/T650Bridge.csproj) | `<Version>`, `<AssemblyVersion>`, `<FileVersion>` | `<Version>1.1.0</Version>`<br>`<AssemblyVersion>1.1.0.0</AssemblyVersion>` |
| [`T650Bridge.wxs`](file:///c:/Workspace/T650-Windows-PTP/Installer/T650Bridge/T650Bridge.wxs) | `<Product Version="..." ...>` | `Version="1.1.0.0"` |
| [`T650Bridge-Admin.wxs`](file:///c:/Workspace/T650-Windows-PTP/Installer/T650Bridge/T650Bridge-Admin.wxs) | `<Product Version="..." ...>` | `Version="1.1.0.0"` |
| [`VirtualPtpDriver.wxs`](file:///c:/Workspace/T650-Windows-PTP/Installer/VirtualPtpDriver/VirtualPtpDriver.wxs) | `<Product Version="..." ...>` | `Version="1.1.0.0"` |
| [`VirtualPtpDriver.inx`](file:///c:/Workspace/T650-Windows-PTP/T650-Windows-PTP-Driver/VirtualPtpDriver/VirtualPtpDriver.inx) | `DriverVer = MM/DD/YYYY,MAJOR.MINOR.PATCH.BUILD` | `DriverVer = 10/09/2026,1.1.0.0` |

### 3. WiX Upgrade Rules (Strict Requirements for AI Prompts)

1. **Keep `Product Id="*"` dynamic:** Never hardcode a GUID in `Product Id`. Setting `Id="*"` ensures WiX generates a new `ProductCode` GUID for each release, enabling smooth automatic upgrades.
2. **Never change `UpgradeCode`:**
   - Bridge PTP Edition UpgradeCode: `355A1666-3125-4EC3-82E0-7F4B02825E0B`
   - Bridge Admin Edition UpgradeCode: `8E3B6899-28BC-4537-88EB-8B9075ACD421`
   - Virtual Driver UpgradeCode: `71E9D2E0-72E7-4AD2-8498-27041365E996`
   - Changing `UpgradeCode` causes Windows Installer to treat it as a different application rather than an upgrade.
3. **MajorUpgrade Configuration:** `<MajorUpgrade DowngradeErrorMessage="A newer version of [ProductName] is already installed." />` handles replacing earlier releases automatically.

### 4. Automated Version Bump (Preferred Method)

An automated PowerShell script updates all files and rebuilds both MSIs in a single step:

```powershell
# Bump version to 1.1.0 across all files and recompile both MSIs:
pwsh -File "Installer/Bump-Version.ps1" -NewVersion "1.1.0"
```

### 5. Manual Build & Packaging Commands

If executing commands step-by-step:

```powershell
# 1. Build and test Bridge .NET project (both flavors)
dotnet build "T650-Windows-PTP-Driver\T650Bridge" -c Release -p:Flavor=Ptp
dotnet build "T650-Windows-PTP-Driver\T650Bridge" -c Release -p:Flavor=Admin

# 2. Build all MSI installers (with versioned names and SHA-256 hashes)
pwsh -File "Installer\Build-All-Installers.ps1" -Version "1.1.2"

# 3. Output artifacts are placed in Dist\:
#    Dist\T650Bridge-PTP-Setup-v1.1.2.msi    (PTP Driver client edition)
#    Dist\T650Bridge-Admin-Setup-v1.1.2.msi  (Standalone Admin / UIPI bypass edition)
#    Dist\VirtualPtpDriver-Setup-v1.1.2.msi  (Virtual PTP Driver package)
#    Dist\SHA256SUMS.txt
#    (Convenience unversioned copies T650Bridge-PTP-Setup.msi, T650Bridge-Admin-Setup.msi, and VirtualPtpDriver-Setup.msi are also maintained)

# 4. Git commit and tag release
git add -u
git commit -m "chore(release): bump version to 1.1.2"
git tag -a v1.1.2 -m "Release v1.1.2"
git push origin main --tags
```

### 6. Auto-Start & Task Manager Verification

* **PTP Edition:**
  - **Registry Key:** `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
  - **Entry Name:** `Logitech T650 Bridge`
  - **Command:** `"C:\Program Files\Logitech T650 PTP\Bridge\T650Bridge.exe" --silent`
  - **Behavior:** Visible in Windows Task Manager $\rightarrow$ **Startup apps** tab as **"Logitech T650 Bridge"**.

* **Admin Edition:**
  - **Task Scheduler:** `Logitech T650 Bridge Admin`
  - **Command:** `"C:\Program Files\Logitech T650 PTP\Bridge-Admin\T650Bridge.exe" --silent`
  - **Trigger:** At logon, runs with Highest Privileges (`/rl highest`) **silently without any UAC prompt**, allowing uninterrupted mouse movement across elevated Administrator windows.

