#include <windows.h>
#include "HidMinip.h"
#include "Queue.h"
#include "Device.h"
#include "PtpTypes.h"
#include "PtpDescriptor.h"

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

    switch (IoControlCode)
    {
    case IOCTL_HID_GET_DEVICE_DESCRIPTOR:
    {
        PVOID buffer;
        size_t bufferSize;
        status = WdfRequestRetrieveOutputBuffer(Request, sizeof(HID_DESCRIPTOR), &buffer, &bufferSize);
        if (NT_SUCCESS(status)) {
            HID_DESCRIPTOR hidDesc = { 0 };
            hidDesc.bLength = sizeof(HID_DESCRIPTOR);
            hidDesc.bDescriptorType = HID_HID_DESCRIPTOR_TYPE;
            hidDesc.bcdHID = 0x0100;
            hidDesc.bCountry = 0;
            hidDesc.bNumDescriptors = 1;
            hidDesc.DescriptorList[0].bReportType = HID_REPORT_DESCRIPTOR_TYPE;
            hidDesc.DescriptorList[0].wReportLength = (USHORT)g_PtpReportDescriptorSize;

            RtlCopyMemory(buffer, &hidDesc, sizeof(HID_DESCRIPTOR));
            bytesReturned = sizeof(HID_DESCRIPTOR);
        }
        break;
    }

    case IOCTL_HID_GET_REPORT_DESCRIPTOR:
    {
        PVOID buffer;
        size_t bufferSize;
        status = WdfRequestRetrieveOutputBuffer(Request, g_PtpReportDescriptorSize, &buffer, &bufferSize);
        if (NT_SUCCESS(status)) {
            RtlCopyMemory(buffer, g_PtpReportDescriptor, g_PtpReportDescriptorSize);
            bytesReturned = g_PtpReportDescriptorSize;
        }
        break;
    }

    case IOCTL_HID_GET_DEVICE_ATTRIBUTES:
    {
        PVOID buffer;
        size_t bufferSize;
        status = WdfRequestRetrieveOutputBuffer(Request, sizeof(HID_DEVICE_ATTRIBUTES), &buffer, &bufferSize);
        if (NT_SUCCESS(status)) {
            PHID_DEVICE_ATTRIBUTES attr = (PHID_DEVICE_ATTRIBUTES)buffer;
            RtlZeroMemory(attr, sizeof(HID_DEVICE_ATTRIBUTES));
            attr->Size = sizeof(HID_DEVICE_ATTRIBUTES);
            attr->VendorID = 0x046D;  // Logitech VID
            attr->ProductID = 0xB008; // Logitech T650 Touchpad PID
            attr->VersionNumber = 0x0100;
            bytesReturned = sizeof(HID_DEVICE_ATTRIBUTES);
        }
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

    case IOCTL_HID_GET_FEATURE:
    {
        PHID_XFER_PACKET packet;
        status = WdfRequestRetrieveInputBuffer(Request, sizeof(HID_XFER_PACKET), (PVOID*)&packet, NULL);
        if (NT_SUCCESS(status) && packet && packet->reportBuffer) {
            if (packet->reportId == 0x02) {
                // Max Contacts Feature Report (Report ID 2)
                if (packet->reportBufferLen >= 2) {
                    packet->reportBuffer[0] = 0x02; // Report ID
                    packet->reportBuffer[1] = 0x05; // Max 5 Contacts
                    bytesReturned = 2;
                } else if (packet->reportBufferLen == 1) {
                    packet->reportBuffer[0] = 0x05;
                    bytesReturned = 1;
                }
            } else if (packet->reportId == 0x07) {
                // Surface Configuration / Certification Report (Report ID 7)
                if (packet->reportBufferLen >= 2) {
                    packet->reportBuffer[0] = 0x07;
                    packet->reportBuffer[1] = 0xC5;
                    bytesReturned = 2;
                } else if (packet->reportBufferLen == 1) {
                    packet->reportBuffer[0] = 0xC5;
                    bytesReturned = 1;
                }
            }
        }
        break;
    }

    case IOCTL_HID_SET_FEATURE:
    {
        PHID_XFER_PACKET packet;
        status = WdfRequestRetrieveInputBuffer(Request, sizeof(HID_XFER_PACKET), (PVOID*)&packet, NULL);
        if (NT_SUCCESS(status) && packet) {
            bytesReturned = packet->reportBufferLen;
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

        PPTP_TOUCH_REPORT report;
        status = WdfRequestRetrieveInputBuffer(Request, sizeof(PTP_TOUCH_REPORT), (PVOID*)&report, NULL);
        if (NT_SUCCESS(status)) {
            // Check if there is a pending read request from the HID class driver
            WDFREQUEST pendingReadRequest;
            NTSTATUS queueStatus = WdfIoQueueRetrieveNextRequest(context->ManualReportQueue, &pendingReadRequest);
            if (NT_SUCCESS(queueStatus)) {
                PVOID outBuf;
                size_t outLen;
                status = WdfRequestRetrieveOutputBuffer(pendingReadRequest, sizeof(PTP_TOUCH_REPORT), &outBuf, &outLen);
                if (NT_SUCCESS(status)) {
                    RtlCopyMemory(outBuf, report, sizeof(PTP_TOUCH_REPORT));
                    WdfRequestCompleteWithInformation(pendingReadRequest, STATUS_SUCCESS, sizeof(PTP_TOUCH_REPORT));
                } else {
                    WdfRequestComplete(pendingReadRequest, status);
                }
            }
            // Always complete the inject request successfully
            status = STATUS_SUCCESS;
            bytesReturned = sizeof(PTP_TOUCH_REPORT);
        }
        break;
    }

    default:
        status = STATUS_NOT_SUPPORTED;
        break;
    }

    WdfRequestCompleteWithInformation(Request, status, bytesReturned);
}
