#pragma once

#include <windows.h>
#include <pshpack1.h>

typedef struct _PTP_CONTACT_REPORT {
    UCHAR Flags; // Bit 0: Tip Switch, Bit 1: In Range, Bit 2: Confidence
    UCHAR ContactId;
    USHORT X;
    USHORT Y;
} PTP_CONTACT_REPORT, *PPTP_CONTACT_REPORT;

typedef struct _PTP_TOUCH_REPORT {
    UCHAR ReportId; // 0x01
    PTP_CONTACT_REPORT Contacts[5];
    USHORT ScanTime;
    UCHAR ContactCount;
    UCHAR ButtonState;
} PTP_TOUCH_REPORT, *PPTP_TOUCH_REPORT;

typedef struct _PTP_DEVICE_CAPS_REPORT {
    UCHAR ReportId; // 0x02
    UCHAR MaxContacts; // 5
} PTP_DEVICE_CAPS_REPORT, *PPTP_DEVICE_CAPS_REPORT;

#include <poppack.h>

// IOCTL for injecting PTP reports from user-mode bridge
#define IOCTL_PTP_INJECT_REPORT CTL_CODE(FILE_DEVICE_UNKNOWN, 0x801, METHOD_BUFFERED, FILE_WRITE_ACCESS)
