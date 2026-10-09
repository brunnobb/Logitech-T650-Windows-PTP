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

// Helper: handle GetFeature reports (Report 2 = MaxContacts, Report 7 = Surface Config)
static NTSTATUS HandleGetFeature(
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

    if (packet.reportId == 0x02) {
        // Max Contacts Feature Report (Report ID 2)
        if (packet.reportBufferLen >= 2) {
            packet.reportBuffer[0] = 0x02; // Report ID
            packet.reportBuffer[1] = 0x05; // Max 5 Contacts
            WdfRequestSetInformation(Request, 2);
            return STATUS_SUCCESS;
        } else if (packet.reportBufferLen == 1) {
            packet.reportBuffer[0] = 0x05;
            WdfRequestSetInformation(Request, 1);
            return STATUS_SUCCESS;
        }
        return STATUS_INVALID_BUFFER_SIZE;
    }
    else if (packet.reportId == 0x07) {
        // Surface Configuration / Certification Report (Report ID 7)
        if (packet.reportBufferLen >= 2) {
            packet.reportBuffer[0] = 0x07; // Report ID
            packet.reportBuffer[1] = 0xC5; // Surface Config
            WdfRequestSetInformation(Request, 2);
            return STATUS_SUCCESS;
        } else if (packet.reportBufferLen == 1) {
            packet.reportBuffer[0] = 0xC5;
            WdfRequestSetInformation(Request, 1);
            return STATUS_SUCCESS;
        }
        return STATUS_INVALID_BUFFER_SIZE;
    }

    return STATUS_NOT_SUPPORTED;
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
        status = HandleGetFeature(Request, IoControlCode);
        break;
    }

    case IOCTL_UMDF_HID_SET_FEATURE:
    case IOCTL_HID_SET_FEATURE:
    {
        WdfRequestSetInformation(Request, InputBufferLength);
        status = STATUS_SUCCESS;
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
