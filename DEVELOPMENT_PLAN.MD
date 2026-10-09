# T650 Windows PTP Development Plan

This document outlines the step-by-step strategy for building the Logitech T650 Precision Touchpad driver.

## Phase 1: Setup & Scaffolding
1.  **Development Environment Preparation:**
    *   Install Visual Studio 2022 with C++ Desktop Development workloads.
    *   Install Windows Driver Kit (WDK) 10/11 for UMDF 2 support.
    *   Set up a local Hyper-V or VMware test machine with `bcdedit /set testsigning on` to safely test driver injection.
2.  **Repository Initialization:**
    *   Initialize the project structure:
        *   `/BridgeDaemon` (User-space application, e.g., C# / C++).
        *   `/VirtualPTPDriver` (UMDF 2 / VHF project).
        *   `/Shared` (Headers/IPC definitions).

## Phase 2: Tier 1 - Hardware Bridge Daemon (User Space)
This phase focuses on interacting directly with the Logitech Unifying receiver.
1.  **Device Discovery:**
    *   Use Win32 HID APIs (or a library like HidSharp in C#) to locate the Logitech Unifying Receiver (VID: `0x046d`, PID: `0xc52b`).
2.  **HID++ 2.0 Handshake (Ported from Linux):**
    *   Ping the receiver to find the T650 device index.
    *   Query Feature `0x6100` (`HIDPP_PAGE_TOUCHPAD_RAW_XY`).
    *   Send the activation payload `[0x10, device_index, feature_index, 0x00, 0x03, ...]` to switch the touchpad from mouse emulation to raw touch streaming mode.
3.  **Packet Parsing:**
    *   Implement a continuous read loop for the 20-byte multi-touch reports.
    *   Extract: Contact Count, Contact IDs (0-4), Tip Switch (touching vs hovering), and packed X/Y coordinates.

## Phase 3: Tier 2 - Virtual PTP Injector (Driver Space)
This phase focuses on creating the virtual touch device that Windows interacts with.
1.  **UMDF 2 Skeleton:**
    *   Create a basic UMDF 2 Virtual HID Miniport driver.
2.  **PTP Report Descriptor:**
    *   Integrate the Microsoft PTP TLC (Top-Level Collection) HID Report Descriptor (referenced from `mac-precision-touchpad`).
    *   Define the exact `PTP_TOUCH_REPORT` C struct (contacts array, scan time, contact count, button states).
3.  **Virtual HID Framework (VHF) Integration:**
    *   Initialize VHF to expose the virtual PTP device to the Windows Touch Input Subsystem.

## Phase 4: Integration & Data Mapping
Connecting the user-space daemon to the driver.
1.  **IPC / IOCTL Communication:**
    *   Establish a communication channel (e.g., `DeviceIoControl`) so the Bridge Daemon can send parsed touch data to the Virtual PTP Driver.
2.  **Coordinate Mapping & Validation:**
    *   Scale the T650's raw coordinate bounds to match the logical maximums declared in the PTP HID Descriptor.
    *   Implement reliable timestamping using `QueryPerformanceCounter` for the `scan_time` field to satisfy strict Windows PTP latency/jitter requirements.
    *   Set the `confidence` bit appropriately to enable native Windows palm rejection.

## Phase 5: Testing & Debugging
1.  **Dry Run via Debugger:**
    *   Run the Bridge Daemon with console output to verify raw HID++ packets are successfully decoding X/Y coordinate streams when touching the glass.
2.  **Driver Injection:**
    *   Deploy the UMDF 2 driver to the test VM.
    *   Start the Bridge Daemon to bridge the physical touch data to the virtual PTP driver.
    *   Verify the device appears natively under Windows Settings > Bluetooth & devices > Touchpad, with all multi-finger gesture options available.
3.  **Troubleshooting:**
    *   Attach WinDbg to `WUDFHost.exe` (User-mode) if there are crashes in the UMDF driver.
    *   Trace dropped frames or erratic cursor jumping to resolve potential 2.4 GHz polling inconsistencies.

## Appendix: Protocol & Hardware Implementation Notes
Derived from the `T650drivers` and `logitec-t650-macos-driver` repositories, the following details should be implemented in the bridge:

1. **HID++ 2.0 Unlocking (Key A):**
   - **Target Interface:** The bridge must write to **Interface 2** of the Unifying Receiver (`0x046d:0xc52b`).
   - **The Unlock Packet (20 bytes):** `[0x11, 0x01, 0x10, 0x21, 0x02, 0x00... ]`
     - `0x11`: Long Report ID
     - `0x01`: Device Index (assumes T650 is device 1)
     - `0x10`: Feature Index (Maps to `0x6100` Touchpad Raw XY on FW 038. Ideally, this should be dynamically queried, but `0x10` works as a fallback).
     - `0x21`: Function ID (Set mode)
     - `0x02`: Mode parameter (Advanced Raw Mode)
2. **Touch Data Decoding:**
   - Raw touch packets arrive as Report ID `0x20` (Extra Long HID++ Report).
   - **Byte 4:** Contains the Contact Mask (Bit 0 to Bit 4 for fingers 1 to 5; `1` = down, `0` = up).
   - **Coordinates (12-bit packing):** Each finger uses 3 bytes (24 bits) to encode X and Y coordinates.
     - `X` = `Byte[0] | ((Byte[1] & 0x0F) << 8)`
     - `Y` = `(Byte[1] >> 4) | (Byte[2] << 8)`
   - Fingers are ordered continuously across bytes 5-19 (e.g., F1 is 5-7, F2 is 8-10, etc.).
