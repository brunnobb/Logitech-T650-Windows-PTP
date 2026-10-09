#pragma once

#include <windows.h>

//
// Microsoft Windows Precision Touchpad (PTP) HID Report Descriptor
// Based on Microsoft PTP Hardware Requirements & sample descriptors
// Usage Page (Digitizers) 0x0D, Usage (Touch Pad) 0x05
//
const UCHAR g_PtpReportDescriptor[] = {
    // Top-Level Collection: Windows Precision Touchpad
    0x05, 0x0D,                         // Usage Page (Digitizer)
    0x09, 0x05,                         // Usage (Touch Pad)
    0xA1, 0x01,                         // Collection (Application)
    0x85, 0x01,                         //   Report ID (1) - Mouse/Touch Input

    // -------------------------------------------------------------
    // Contact 0 Collection
    // -------------------------------------------------------------
    0x05, 0x0D,                         //   Usage Page (Digitizer)
    0x09, 0x22,                         //   Usage (Finger)
    0xA1, 0x02,                         //   Collection (Logical)
    0x09, 0x42,                         //     Usage (Tip Switch)
    0x09, 0x32,                         //     Usage (In Range)
    0x09, 0x47,                         //     Usage (Confidence)
    0x15, 0x00,                         //     Logical Minimum (0)
    0x25, 0x01,                         //     Logical Maximum (1)
    0x75, 0x01,                         //     Report Size (1)
    0x95, 0x03,                         //     Report Count (3)
    0x81, 0x02,                         //     Input (Data,Var,Abs)
    0x95, 0x05,                         //     Report Count (5) - Padding
    0x81, 0x03,                         //     Input (Const,Var,Abs)
    0x09, 0x51,                         //     Usage (Contact Identifier)
    0x75, 0x08,                         //     Report Size (8)
    0x95, 0x01,                         //     Report Count (1)
    0x81, 0x02,                         //     Input (Data,Var,Abs)
    0x05, 0x01,                         //     Usage Page (Generic Desktop)
    0x26, 0x10, 0x0B,                   //     Logical Maximum (2832) - T650 Max X
    0x75, 0x10,                         //     Report Size (16)
    0x55, 0x0E,                         //     Unit Exponent (-2)
    0x65, 0x11,                         //     Unit (Centimeter)
    0x09, 0x30,                         //     Usage (X)
    0x81, 0x02,                         //     Input (Data,Var,Abs)
    0x26, 0x3C, 0x09,                   //     Logical Maximum (2364) - T650 Max Y
    0x09, 0x31,                         //     Usage (Y)
    0x81, 0x02,                         //     Input (Data,Var,Abs)
    0xC0,                               //   End Collection

    // -------------------------------------------------------------
    // Contact 1 Collection
    // -------------------------------------------------------------
    0x05, 0x0D,                         //   Usage Page (Digitizer)
    0x09, 0x22,                         //   Usage (Finger)
    0xA1, 0x02,                         //   Collection (Logical)
    0x09, 0x42,                         //     Usage (Tip Switch)
    0x09, 0x32,                         //     Usage (In Range)
    0x09, 0x47,                         //     Usage (Confidence)
    0x15, 0x00,                         //     Logical Minimum (0)
    0x25, 0x01,                         //     Logical Maximum (1)
    0x75, 0x01,                         //     Report Size (1)
    0x95, 0x03,                         //     Report Count (3)
    0x81, 0x02,                         //     Input (Data,Var,Abs)
    0x95, 0x05,                         //     Report Count (5) - Padding
    0x81, 0x03,                         //     Input (Const,Var,Abs)
    0x09, 0x51,                         //     Usage (Contact Identifier)
    0x75, 0x08,                         //     Report Size (8)
    0x95, 0x01,                         //     Report Count (1)
    0x81, 0x02,                         //     Input (Data,Var,Abs)
    0x05, 0x01,                         //     Usage Page (Generic Desktop)
    0x26, 0x10, 0x0B,                   //     Logical Maximum (2832)
    0x75, 0x10,                         //     Report Size (16)
    0x55, 0x0E,                         //     Unit Exponent (-2)
    0x65, 0x11,                         //     Unit (Centimeter)
    0x09, 0x30,                         //     Usage (X)
    0x81, 0x02,                         //     Input (Data,Var,Abs)
    0x26, 0x3C, 0x09,                   //     Logical Maximum (2364)
    0x09, 0x31,                         //     Usage (Y)
    0x81, 0x02,                         //     Input (Data,Var,Abs)
    0xC0,                               //   End Collection

    // -------------------------------------------------------------
    // Contact 2 Collection
    // -------------------------------------------------------------
    0x05, 0x0D,                         //   Usage Page (Digitizer)
    0x09, 0x22,                         //   Usage (Finger)
    0xA1, 0x02,                         //   Collection (Logical)
    0x09, 0x42,                         //     Usage (Tip Switch)
    0x09, 0x32,                         //     Usage (In Range)
    0x09, 0x47,                         //     Usage (Confidence)
    0x15, 0x00,                         //     Logical Minimum (0)
    0x25, 0x01,                         //     Logical Maximum (1)
    0x75, 0x01,                         //     Report Size (1)
    0x95, 0x03,                         //     Report Count (3)
    0x81, 0x02,                         //     Input (Data,Var,Abs)
    0x95, 0x05,                         //     Report Count (5) - Padding
    0x81, 0x03,                         //     Input (Const,Var,Abs)
    0x09, 0x51,                         //     Usage (Contact Identifier)
    0x75, 0x08,                         //     Report Size (8)
    0x95, 0x01,                         //     Report Count (1)
    0x81, 0x02,                         //     Input (Data,Var,Abs)
    0x05, 0x01,                         //     Usage Page (Generic Desktop)
    0x26, 0x10, 0x0B,                   //     Logical Maximum (2832)
    0x75, 0x10,                         //     Report Size (16)
    0x55, 0x0E,                         //     Unit Exponent (-2)
    0x65, 0x11,                         //     Unit (Centimeter)
    0x09, 0x30,                         //     Usage (X)
    0x81, 0x02,                         //     Input (Data,Var,Abs)
    0x26, 0x3C, 0x09,                   //     Logical Maximum (2364)
    0x09, 0x31,                         //     Usage (Y)
    0x81, 0x02,                         //     Input (Data,Var,Abs)
    0xC0,                               //   End Collection

    // -------------------------------------------------------------
    // Contact 3 Collection
    // -------------------------------------------------------------
    0x05, 0x0D,                         //   Usage Page (Digitizer)
    0x09, 0x22,                         //   Usage (Finger)
    0xA1, 0x02,                         //   Collection (Logical)
    0x09, 0x42,                         //     Usage (Tip Switch)
    0x09, 0x32,                         //     Usage (In Range)
    0x09, 0x47,                         //     Usage (Confidence)
    0x15, 0x00,                         //     Logical Minimum (0)
    0x25, 0x01,                         //     Logical Maximum (1)
    0x75, 0x01,                         //     Report Size (1)
    0x95, 0x03,                         //     Report Count (3)
    0x81, 0x02,                         //     Input (Data,Var,Abs)
    0x95, 0x05,                         //     Report Count (5) - Padding
    0x81, 0x03,                         //     Input (Const,Var,Abs)
    0x09, 0x51,                         //     Usage (Contact Identifier)
    0x75, 0x08,                         //     Report Size (8)
    0x95, 0x01,                         //     Report Count (1)
    0x81, 0x02,                         //     Input (Data,Var,Abs)
    0x05, 0x01,                         //     Usage Page (Generic Desktop)
    0x26, 0x10, 0x0B,                   //     Logical Maximum (2832)
    0x75, 0x10,                         //     Report Size (16)
    0x55, 0x0E,                         //     Unit Exponent (-2)
    0x65, 0x11,                         //     Unit (Centimeter)
    0x09, 0x30,                         //     Usage (X)
    0x81, 0x02,                         //     Input (Data,Var,Abs)
    0x26, 0x3C, 0x09,                   //     Logical Maximum (2364)
    0x09, 0x31,                         //     Usage (Y)
    0x81, 0x02,                         //     Input (Data,Var,Abs)
    0xC0,                               //   End Collection

    // -------------------------------------------------------------
    // Contact 4 Collection
    // -------------------------------------------------------------
    0x05, 0x0D,                         //   Usage Page (Digitizer)
    0x09, 0x22,                         //   Usage (Finger)
    0xA1, 0x02,                         //   Collection (Logical)
    0x09, 0x42,                         //     Usage (Tip Switch)
    0x09, 0x32,                         //     Usage (In Range)
    0x09, 0x47,                         //     Usage (Confidence)
    0x15, 0x00,                         //     Logical Minimum (0)
    0x25, 0x01,                         //     Logical Maximum (1)
    0x75, 0x01,                         //     Report Size (1)
    0x95, 0x03,                         //     Report Count (3)
    0x81, 0x02,                         //     Input (Data,Var,Abs)
    0x95, 0x05,                         //     Report Count (5) - Padding
    0x81, 0x03,                         //     Input (Const,Var,Abs)
    0x09, 0x51,                         //     Usage (Contact Identifier)
    0x75, 0x08,                         //     Report Size (8)
    0x95, 0x01,                         //     Report Count (1)
    0x81, 0x02,                         //     Input (Data,Var,Abs)
    0x05, 0x01,                         //     Usage Page (Generic Desktop)
    0x26, 0x10, 0x0B,                   //     Logical Maximum (2832)
    0x75, 0x10,                         //     Report Size (16)
    0x55, 0x0E,                         //     Unit Exponent (-2)
    0x65, 0x11,                         //     Unit (Centimeter)
    0x09, 0x30,                         //     Usage (X)
    0x81, 0x02,                         //     Input (Data,Var,Abs)
    0x26, 0x3C, 0x09,                   //     Logical Maximum (2364)
    0x09, 0x31,                         //     Usage (Y)
    0x81, 0x02,                         //     Input (Data,Var,Abs)
    0xC0,                               //   End Collection

    // -------------------------------------------------------------
    // Relative Scan Time, Contact Count, Physical Buttons
    // -------------------------------------------------------------
    0x05, 0x0D,                         //   Usage Page (Digitizer)
    0x55, 0x0C,                         //   Unit Exponent (-4) -> 100 microseconds
    0x66, 0x01, 0x10,                   //   Unit (Seconds)
    0x47, 0xFF, 0xFF, 0x00, 0x00,       //   Physical Maximum (65535)
    0x27, 0xFF, 0xFF, 0x00, 0x00,       //   Logical Maximum (65535)
    0x75, 0x10,                         //   Report Size (16)
    0x95, 0x01,                         //   Report Count (1)
    0x09, 0x56,                         //   Usage (Scan Time)
    0x81, 0x02,                         //   Input (Data,Var,Abs)
    0x09, 0x54,                         //   Usage (Contact Count)
    0x25, 0x7F,                         //   Logical Maximum (127)
    0x75, 0x08,                         //   Report Size (8)
    0x81, 0x02,                         //   Input (Data,Var,Abs)

    0x05, 0x09,                         //   Usage Page (Button)
    0x09, 0x01,                         //   Usage (Button 1)
    0x25, 0x01,                         //   Logical Maximum (1)
    0x75, 0x01,                         //   Report Size (1)
    0x95, 0x01,                         //   Report Count (1)
    0x81, 0x02,                         //   Input (Data,Var,Abs)
    0x95, 0x07,                         //   Report Count (7) - Padding
    0x81, 0x03,                         //   Input (Const,Var,Abs)

    // -------------------------------------------------------------
    // Feature Report: Device Capabilities (Max Contacts = 5)
    // -------------------------------------------------------------
    0x05, 0x0D,                         //   Usage Page (Digitizers)
    0x85, 0x02,                         //   Report ID (2) - Capabilities
    0x09, 0x55,                         //   Usage (Contact Count Maximum)
    0x25, 0x05,                         //   Logical Maximum (5)
    0x75, 0x08,                         //   Report Size (8)
    0x95, 0x01,                         //   Report Count (1)
    0xB1, 0x02,                         //   Feature (Data,Var,Abs)

    // -------------------------------------------------------------
    // Feature Report: Certification / Latency / Status
    // -------------------------------------------------------------
    0x85, 0x07,                         //   Report ID (7) - Surface Configuration
    0x06, 0x00, 0xFF,                   //   Usage Page (Vendor-defined)
    0x09, 0xC5,                         //   Usage (Vendor Usage 0xC5)
    0x15, 0x00,                         //   Logical Minimum (0)
    0x26, 0xFF, 0x00,                   //   Logical Maximum (255)
    0x75, 0x08,                         //   Report Size (8)
    0x95, 0x01,                         //   Report Count (1)
    0xB1, 0x02,                         //   Feature (Data,Var,Abs)

    0xC0                                // End Collection
};

const ULONG g_PtpReportDescriptorSize = sizeof(g_PtpReportDescriptor);
