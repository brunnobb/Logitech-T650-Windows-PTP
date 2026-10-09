#include <windows.h>
#include "HidMinip.h"
#include "Queue.h"
#include "Device.h"
#include "PtpTypes.h"
#include "PtpDescriptor.h"

// String descriptors for device identification
static const WCHAR g_ManufacturerString[] = L"Logitech";
static const WCHAR g_ProductString[] = L"Logitech T650 Precision Touchpad (Virtual PTP)";
static const WCHAR g_SerialNumberString[] = L"00000001";

// Microsoft Windows Precision Touchpad Hardware Quality Assurance (PTPHQA) default blob
static const UCHAR g_PtpCertificationBlob[256] = {
    0xfc, 0x28, 0xfe, 0x84, 0x40, 0xcb, 0x9a, 0x87, 0x0d, 0xbe, 0x57, 0x3c, 0xb6, 0x70, 0x09, 0x88, 0x07,
    0x97, 0x2d, 0x2b, 0xe3, 0x38, 0x34, 0xb6, 0x6c, 0xed, 0xb0, 0xf7, 0xe5, 0x9c, 0xf6, 0xc2, 0x2e, 0x84,
    0x1b, 0xe8, 0xb4, 0x51, 0x78, 0x43, 0x1f, 0x28, 0x4b, 0x7c, 0x2d, 0x53, 0xaf, 0xfc, 0x47, 0x70, 0x1b,
    0x59, 0x6f, 0x74, 0x43, 0xc4, 0xf3, 0x47, 0x18, 0x53, 0x1a, 0xa2, 0xa1, 0x71, 0xc7, 0x95, 0x0e, 0x31,
    0x55, 0x21, 0xd3, 0xb5, 0x1e, 0xe9, 0x0c, 0xba, 0xec, 0xb8, 0x89, 0x19, 0x3e, 0xb3, 0xaf, 0x75, 0x81,
    0x9d, 0x53, 0xb9, 0x41, 0x57, 0xf4, 0x6d, 0x39, 0x25, 0x29, 0x7c, 0x87, 0xd9, 0xb4, 0x98, 0x45, 0x7d,
    0xa7, 0x26, 0x9c, 0x65, 0x3b, 0x85, 0x68, 0x89, 0xd7, 0x3b, 0xbd, 0xff, 0x14, 0x67, 0xf2, 0x2b, 0xf0,
    0x2a, 0x41, 0x54, 0xf0, 0xfd, 0x2c, 0x66, 0x7c, 0xf8, 0xc0, 0x8f, 0x33, 0x13, 0x03, 0xf1, 0xd3, 0xc1, 0x0b,
    0x89, 0xd9, 0x1b, 0x62, 0xcd, 0x51, 0xb7, 0x80, 0xb8, 0xaf, 0x3a, 0x10, 0xc1, 0x8a, 0x5b, 0xe8, 0x8a,
    0x56, 0xf0, 0x8c, 0xaa, 0xfa, 0x35, 0xe9, 0x42, 0xc4, 0xd8, 0x55, 0xc3, 0x38, 0xcc, 0x2b, 0x53, 0x5c,
    0x69, 0x52, 0xd5, 0xc8, 0x73, 0x02, 0x38, 0x7c, 0x73, 0xb6, 0x41, 0xe7, 0xff, 0x05, 0xd8, 0x2b, 0x79,
    0x9a, 0xe2, 0x34, 0x60, 0x8f, 0xa3, 0x32, 0x1f, 0x09, 0x78, 0x62, 0xbc, 0x80, 0xe3, 0x0f, 0xbd, 0x65,
    0x20, 0x08, 0x13, 0xc1, 0xe2, 0xee, 0x53, 0x2d, 0x86, 0x7e, 0xa7, 0x5a, 0xc5, 0xd3, 0x7d, 0x98, 0xbe,
    0x31, 0x48, 0x1f, 0xfb, 0xda, 0xaf, 0xa2, 0xa8, 0x6a, 0x89, 0xd6, 0xbf, 0xf2, 0xd3, 0x32, 0x2a, 0x9a,
    0xe4, 0xcf, 0x17, 0xb7, 0xb8, 0xf4, 0xe1, 0x33, 0x08, 0x24, 0x8b, 0xc4, 0x43, 0xa5, 0xe5, 0x24, 0xc2
};

// Helper: copy buffer to output memory of a WDF request (handles METHOD_NEITHER in UMDF 2)
static NTSTATUS RequestCopyFromBuffer(
    _In_ WDFREQUEST Request,
    _In_ PVOID SourceBuffer,
    _In_ size_t NumBytesToCopyFrom
)
{
    WDFMEMORY memory;
    size_t outputBufferLength = 0;

    NTSTATUS status = WdfRequestRetrieveOutputMemory(Request, &memory);
    if (!NT_SUCCESS(status)) {
        return status;
    }

    WdfMemoryGetBuffer(memory, &outputBufferLength);
    if (outputBufferLength < NumBytesToCopyFrom) {
        return STATUS_INVALID_BUFFER_SIZE;
    }

    status = WdfMemoryCopyFromBuffer(memory, 0, SourceBuffer, NumBytesToCopyFrom);
    if (NT_SUCCESS(status)) {
        WdfRequestSetInformation(Request, NumBytesToCopyFrom);
    }
    return status;
}

// Helper: extract HID transfer packet for reading from device in UMDF 2
// Input buffer contains Report ID; output buffer receives the report
static NTSTATUS RequestGetHidXferPacket_ToReadFromDevice(
    _In_  WDFREQUEST Request,
    _Out_ HID_XFER_PACKET* Packet
)
{
    WDFMEMORY inputMemory;
    WDFMEMORY outputMemory;
    size_t inputBufferLength = 0;
    size_t outputBufferLength = 0;
    PVOID inputBuffer = NULL;
    PVOID outputBuffer = NULL;

    NTSTATUS status = WdfRequestRetrieveInputMemory(Request, &inputMemory);
    if (!NT_SUCCESS(status)) {
        return status;
    }

    inputBuffer = WdfMemoryGetBuffer(inputMemory, &inputBufferLength);
    if (inputBufferLength < sizeof(UCHAR)) {
        return STATUS_INVALID_BUFFER_SIZE;
    }

    Packet->reportId = *(PUCHAR)inputBuffer;

    status = WdfRequestRetrieveOutputMemory(Request, &outputMemory);
    if (!NT_SUCCESS(status)) {
        return status;
    }

    outputBuffer = WdfMemoryGetBuffer(outputMemory, &outputBufferLength);
    Packet->reportBuffer = (PUCHAR)outputBuffer;
    Packet->reportBufferLen = (ULONG)outputBufferLength;

    return STATUS_SUCCESS;
}

// Helper: extract string ID from IOCTL_HID_GET_STRING
static NTSTATUS GetStringId(
    _In_  WDFREQUEST Request,
    _Out_ ULONG* StringId,
    _Out_ ULONG* LanguageId
)
{
    WDFMEMORY inputMemory;
    size_t inputBufferLength = 0;

    NTSTATUS status = WdfRequestRetrieveInputMemory(Request, &inputMemory);
    if (!NT_SUCCESS(status)) {
        return status;
    }

    PVOID inputBuffer = WdfMemoryGetBuffer(inputMemory, &inputBufferLength);
    if (inputBufferLength < sizeof(ULONG)) {
        return STATUS_INVALID_BUFFER_SIZE;
    }

    ULONG inputValue = *(PULONG)inputBuffer;
    *StringId = (inputValue & 0xFFFF);
    *LanguageId = (inputValue >> 16);
    return STATUS_SUCCESS;
}

// Helper: handle GetFeature reports
// - Report 2: Device Capabilities (Max Contacts = 5, ClickPad)
// - Report 3: Input Mode (PTP mode = 3)
// - Report 4: Function Switch (Surface + Button = 3)
// - Report 7: PTPHQA Certification Blob (256 bytes)
static NTSTATUS HandleGetFeature(
    _In_ PDEVICE_CONTEXT Context,
    _In_ WDFREQUEST Request,
    _In_ ULONG IoControlCode
)
{
    HID_XFER_PACKET packet = { 0 };
    NTSTATUS status = STATUS_SUCCESS;

    if (IoControlCode == IOCTL_UMDF_HID_GET_FEATURE) {
        status = RequestGetHidXferPacket_ToReadFromDevice(Request, &packet);
    } else {
        // Kernel-mode / direct IOCTL_HID_GET_FEATURE
        PHID_XFER_PACKET pKernelPacket = NULL;
        status = WdfRequestRetrieveInputBuffer(Request, sizeof(HID_XFER_PACKET), (PVOID*)&pKernelPacket, NULL);
        if (NT_SUCCESS(status) && pKernelPacket != NULL) {
            packet = *pKernelPacket;
        }
    }

    if (!NT_SUCCESS(status)) {
        return status;
    }

    if (packet.reportBuffer == NULL || packet.reportBufferLen == 0) {
        return STATUS_INVALID_BUFFER_SIZE;
    }

    switch (packet.reportId)
    {
    case 0x02: // Capabilities (Max Contacts = 5, Pad Type = 0)
    {
        if (packet.reportBufferLen >= 2) {
            packet.reportBuffer[0] = 0x02; // Report ID
            packet.reportBuffer[1] = 0x05; // 5 contacts, ClickPad
            WdfRequestSetInformation(Request, 2);
            return STATUS_SUCCESS;
        } else if (packet.reportBufferLen == 1) {
            packet.reportBuffer[0] = 0x05;
            WdfRequestSetInformation(Request, 1);
            return STATUS_SUCCESS;
        }
        return STATUS_INVALID_BUFFER_SIZE;
    }

    case 0x03: // Input Mode (3 = PTP Mode)
    {
        if (packet.reportBufferLen >= 2) {
            packet.reportBuffer[0] = 0x03; // Report ID
            packet.reportBuffer[1] = Context->InputMode;
            WdfRequestSetInformation(Request, 2);
            return STATUS_SUCCESS;
        } else if (packet.reportBufferLen == 1) {
            packet.reportBuffer[0] = Context->InputMode;
            WdfRequestSetInformation(Request, 1);
            return STATUS_SUCCESS;
        }
        return STATUS_INVALID_BUFFER_SIZE;
    }

    case 0x04: // Function Switch (3 = Surface + Button enabled)
    {
        if (packet.reportBufferLen >= 2) {
            packet.reportBuffer[0] = 0x04; // Report ID
            packet.reportBuffer[1] = Context->FunctionSwitch;
            WdfRequestSetInformation(Request, 2);
            return STATUS_SUCCESS;
        } else if (packet.reportBufferLen == 1) {
            packet.reportBuffer[0] = Context->FunctionSwitch;
            WdfRequestSetInformation(Request, 1);
            return STATUS_SUCCESS;
        }
        return STATUS_INVALID_BUFFER_SIZE;
    }

    case 0x07: // PTPHQA Certification Blob (256 bytes)
    {
        if (packet.reportBufferLen >= 257) {
            packet.reportBuffer[0] = 0x07; // Report ID
            RtlCopyMemory(&packet.reportBuffer[1], g_PtpCertificationBlob, sizeof(g_PtpCertificationBlob));
            WdfRequestSetInformation(Request, 257);
            return STATUS_SUCCESS;
        } else if (packet.reportBufferLen == 256) {
            RtlCopyMemory(packet.reportBuffer, g_PtpCertificationBlob, sizeof(g_PtpCertificationBlob));
            WdfRequestSetInformation(Request, 256);
            return STATUS_SUCCESS;
        }
        return STATUS_INVALID_BUFFER_SIZE;
    }

    default:
        return STATUS_NOT_SUPPORTED;
    }
}

// Helper: handle SetFeature reports
// - Report 3: Input Mode (host selects Mouse or PTP mode)
// - Report 4: Function Switch (host selects Surface/Button reporting)
static NTSTATUS HandleSetFeature(
    _In_ PDEVICE_CONTEXT Context,
    _In_ WDFREQUEST Request,
    _In_ ULONG IoControlCode,
    _In_ size_t InputBufferLength
)
{
    UNREFERENCED_PARAMETER(IoControlCode);

    WDFMEMORY inputMemory;
    NTSTATUS status = WdfRequestRetrieveInputMemory(Request, &inputMemory);
    if (!NT_SUCCESS(status)) {
        return status;
    }

    size_t bufferLength = 0;
    PUCHAR buffer = (PUCHAR)WdfMemoryGetBuffer(inputMemory, &bufferLength);
    if (buffer == NULL || bufferLength == 0) {
        return STATUS_INVALID_BUFFER_SIZE;
    }

    UCHAR reportId = buffer[0];
    UCHAR value = (bufferLength >= 2) ? buffer[1] : buffer[0];

    if (reportId == 0x03) {
        Context->InputMode = value;
    } else if (reportId == 0x04) {
        Context->FunctionSwitch = value;
    }

    WdfRequestSetInformation(Request, bufferLength);
    return STATUS_SUCCESS;
}

NTSTATUS QueueInitialize(_In_ WDFDEVICE Device)
{
    WDF_IO_QUEUE_CONFIG queueConfig;
    WDFQUEUE queue;
    NTSTATUS status;
    PDEVICE_CONTEXT context = DeviceGetContext(Device);

    WDF_IO_QUEUE_CONFIG_INIT_DEFAULT_QUEUE(&queueConfig, WdfIoQueueDispatchSequential);
    queueConfig.EvtIoDeviceControl = EvtIoDeviceControl;

    status = WdfIoQueueCreate(Device, &queueConfig, WDF_NO_OBJECT_ATTRIBUTES, &queue);
    if (!NT_SUCCESS(status)) {
        return status;
    }
    context->DefaultQueue = queue;

    // Create a manual queue for pending HID read reports
    WDF_IO_QUEUE_CONFIG_INIT(&queueConfig, WdfIoQueueDispatchManual);
    status = WdfIoQueueCreate(Device, &queueConfig, WDF_NO_OBJECT_ATTRIBUTES, &context->ManualReportQueue);
    return status;
}

VOID EvtIoDeviceControl(
    _In_ WDFQUEUE   Queue,
    _In_ WDFREQUEST Request,
    _In_ size_t     OutputBufferLength,
    _In_ size_t     InputBufferLength,
    _In_ ULONG      IoControlCode
)
{
    NTSTATUS status = STATUS_SUCCESS;
    WDFDEVICE device = WdfIoQueueGetDevice(Queue);
    PDEVICE_CONTEXT context = DeviceGetContext(device);
    size_t bytesReturned = 0;
    BOOLEAN completeRequest = TRUE;

    UNREFERENCED_PARAMETER(OutputBufferLength);

    switch (IoControlCode)
    {
    case IOCTL_HID_GET_DEVICE_DESCRIPTOR:
    {
        HID_DESCRIPTOR hidDesc = { 0 };
        hidDesc.bLength = sizeof(HID_DESCRIPTOR);
        hidDesc.bDescriptorType = HID_HID_DESCRIPTOR_TYPE;
        hidDesc.bcdHID = 0x0100;
        hidDesc.bCountry = 0;
        hidDesc.bNumDescriptors = 1;
        hidDesc.DescriptorList[0].bReportType = HID_REPORT_DESCRIPTOR_TYPE;
        hidDesc.DescriptorList[0].wReportLength = (USHORT)g_PtpReportDescriptorSize;

        status = RequestCopyFromBuffer(Request, &hidDesc, sizeof(HID_DESCRIPTOR));
        break;
    }

    case IOCTL_HID_GET_REPORT_DESCRIPTOR:
    {
        status = RequestCopyFromBuffer(Request, (PVOID)g_PtpReportDescriptor, g_PtpReportDescriptorSize);
        break;
    }

    case IOCTL_HID_GET_DEVICE_ATTRIBUTES:
    {
        HID_DEVICE_ATTRIBUTES attr = { 0 };
        attr.Size = sizeof(HID_DEVICE_ATTRIBUTES);
        attr.VendorID = 0x046D;  // Logitech VID
        attr.ProductID = 0xB008; // Logitech T650 Touchpad PID
        attr.VersionNumber = 0x0100;

        status = RequestCopyFromBuffer(Request, &attr, sizeof(HID_DEVICE_ATTRIBUTES));
        break;
    }

    case IOCTL_HID_READ_REPORT:
    {
        // Forward read request to manual report queue
        // It stays pending until an input report is injected from the bridge
        status = WdfRequestForwardToIoQueue(Request, context->ManualReportQueue);
        if (NT_SUCCESS(status)) {
            // Forwarded successfully - do not complete now!
            return;
        }
        break;
    }

    case IOCTL_UMDF_HID_GET_FEATURE:
    case IOCTL_HID_GET_FEATURE:
    {
        status = HandleGetFeature(context, Request, IoControlCode);
        break;
    }

    case IOCTL_UMDF_HID_SET_FEATURE:
    case IOCTL_HID_SET_FEATURE:
    {
        status = HandleSetFeature(context, Request, IoControlCode, InputBufferLength);
        break;
    }

    case IOCTL_UMDF_HID_GET_INPUT_REPORT:
    case IOCTL_HID_GET_INPUT_REPORT:
    {
        HID_XFER_PACKET packet = { 0 };
        if (IoControlCode == IOCTL_UMDF_HID_GET_INPUT_REPORT) {
            status = RequestGetHidXferPacket_ToReadFromDevice(Request, &packet);
        } else {
            PHID_XFER_PACKET pKernelPacket = NULL;
            status = WdfRequestRetrieveInputBuffer(Request, sizeof(HID_XFER_PACKET), (PVOID*)&pKernelPacket, NULL);
            if (NT_SUCCESS(status) && pKernelPacket != NULL) {
                packet = *pKernelPacket;
            }
        }
        if (NT_SUCCESS(status) && packet.reportBuffer != NULL && packet.reportBufferLen > 0) {
            if (packet.reportId == 0x01 && packet.reportBufferLen >= sizeof(PTP_TOUCH_REPORT)) {
                PTP_TOUCH_REPORT idleReport = { 0 };
                idleReport.ReportId = 0x01;
                RtlCopyMemory(packet.reportBuffer, &idleReport, sizeof(PTP_TOUCH_REPORT));
                WdfRequestSetInformation(Request, sizeof(PTP_TOUCH_REPORT));
                status = STATUS_SUCCESS;
            } else {
                RtlZeroMemory(packet.reportBuffer, packet.reportBufferLen);
                if (packet.reportId != 0 && packet.reportBufferLen > 0) {
                    packet.reportBuffer[0] = packet.reportId;
                }
                WdfRequestSetInformation(Request, packet.reportBufferLen);
                status = STATUS_SUCCESS;
            }
        }
        break;
    }

    case IOCTL_UMDF_HID_SET_OUTPUT_REPORT:
    case IOCTL_HID_SET_OUTPUT_REPORT:
    {
        WdfRequestSetInformation(Request, InputBufferLength);
        status = STATUS_SUCCESS;
        break;
    }

    case IOCTL_HID_ACTIVATE_DEVICE:
    case IOCTL_HID_DEACTIVATE_DEVICE:
    {
        status = STATUS_SUCCESS;
        break;
    }

    case IOCTL_HID_GET_STRING:
    {
        ULONG stringId = 0;
        ULONG languageId = 0;
        status = GetStringId(Request, &stringId, &languageId);
        if (NT_SUCCESS(status)) {
            switch (stringId) {
            case 1: // Manufacturer
                status = RequestCopyFromBuffer(Request, (PVOID)g_ManufacturerString, sizeof(g_ManufacturerString));
                break;
            case 2: // Product
                status = RequestCopyFromBuffer(Request, (PVOID)g_ProductString, sizeof(g_ProductString));
                break;
            case 3: // Serial Number
                status = RequestCopyFromBuffer(Request, (PVOID)g_SerialNumberString, sizeof(g_SerialNumberString));
                break;
            default:
                status = STATUS_INVALID_PARAMETER;
                break;
            }
        }
        break;
    }

    case IOCTL_PTP_INJECT_REPORT:
    {
        // Injection from user-mode bridge daemon
        if (InputBufferLength < sizeof(PTP_TOUCH_REPORT)) {
            status = STATUS_BUFFER_TOO_SMALL;
            break;
        }

        PPTP_TOUCH_REPORT report = NULL;
        status = WdfRequestRetrieveInputBuffer(Request, sizeof(PTP_TOUCH_REPORT), (PVOID*)&report, NULL);
        if (NT_SUCCESS(status) && report != NULL) {
            // Check if there is a pending read request from the HID class driver
            WDFREQUEST pendingReadRequest = NULL;
            NTSTATUS queueStatus = WdfIoQueueRetrieveNextRequest(context->ManualReportQueue, &pendingReadRequest);
            if (NT_SUCCESS(queueStatus) && pendingReadRequest != NULL) {
                NTSTATUS copyStatus = RequestCopyFromBuffer(pendingReadRequest, report, sizeof(PTP_TOUCH_REPORT));
                WdfRequestComplete(pendingReadRequest, copyStatus);
            }
            // Complete the inject request successfully
            status = STATUS_SUCCESS;
            bytesReturned = sizeof(PTP_TOUCH_REPORT);
        }
        break;
    }

    default:
        status = STATUS_NOT_SUPPORTED;
        break;
    }

    if (completeRequest) {
        if (bytesReturned > 0) {
            WdfRequestCompleteWithInformation(Request, status, bytesReturned);
        } else {
            WdfRequestComplete(Request, status);
        }
    }
}
