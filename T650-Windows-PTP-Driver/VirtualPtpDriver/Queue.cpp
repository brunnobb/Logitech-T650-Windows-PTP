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
    case IOCTL_PTP_INJECT_REPORT:
        // Injection from user-mode bridge
        if (InputBufferLength < sizeof(PTP_TOUCH_REPORT)) {
            status = STATUS_BUFFER_TOO_SMALL;
            break;
        }

        PPTP_TOUCH_REPORT report;
        status = WdfRequestRetrieveInputBuffer(Request, sizeof(PTP_TOUCH_REPORT), (PVOID*)&report, NULL);
        if (NT_SUCCESS(status)) {
            // Complete any pending read request in the manual queue with this report
            WDFREQUEST pendingReadRequest;
            if (NT_SUCCESS(WdfIoQueueRetrieveNextRequest(context->ManualReportQueue, &pendingReadRequest))) {
                PVOID outBuf;
                size_t outLen;
                if (NT_SUCCESS(WdfRequestRetrieveOutputBuffer(pendingReadRequest, sizeof(PTP_TOUCH_REPORT), &outBuf, &outLen))) {
                    RtlCopyMemory(outBuf, report, sizeof(PTP_TOUCH_REPORT));
                    WdfRequestCompleteWithInformation(pendingReadRequest, STATUS_SUCCESS, sizeof(PTP_TOUCH_REPORT));
                } else {
                    WdfRequestComplete(pendingReadRequest, STATUS_BUFFER_TOO_SMALL);
                }
            }
        }
        break;

    default:
        status = STATUS_NOT_SUPPORTED;
        break;
    }

    WdfRequestCompleteWithInformation(Request, status, bytesReturned);
}
