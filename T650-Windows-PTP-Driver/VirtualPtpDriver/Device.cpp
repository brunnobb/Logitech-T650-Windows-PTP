#include <windows.h>
#include <initguid.h>
#include "Device.h"
#include "Queue.h"
#include "Trace.h"

NTSTATUS DeviceCreate(_Inout_ PWDFDEVICE_INIT DeviceInit)
{
    WDF_OBJECT_ATTRIBUTES deviceAttributes;
    WDFDEVICE device;
    NTSTATUS status;
    PDEVICE_CONTEXT context;

    PtpLog(L"[VirtualPtpDriver] DeviceCreate: Setting filter...\n");
    WDF_OBJECT_ATTRIBUTES_INIT_CONTEXT_TYPE(&deviceAttributes, DEVICE_CONTEXT);

    // Filter driver under MsHidUmdf.sys
    WdfFdoInitSetFilter(DeviceInit);

    status = WdfDeviceCreate(&DeviceInit, &deviceAttributes, &device);
    PtpLog(L"[VirtualPtpDriver] DeviceCreate: WdfDeviceCreate status=0x%08X\n", status);
    if (!NT_SUCCESS(status)) {
        return status;
    }

    context = DeviceGetContext(device);
    context->Device = device;
    context->InputMode = 0x03; // Default to Precision Touchpad mode
    context->FunctionSwitch = 0x03; // Default to Surface contacts + Button state enabled

    // Register device interface so the user-space Bridge Daemon can send IOCTLs
    status = WdfDeviceCreateDeviceInterface(
        device,
        &GUID_DEVINTERFACE_T650_VIRTUAL_PTP,
        NULL
    );
    PtpLog(L"[VirtualPtpDriver] DeviceCreate: WdfDeviceCreateDeviceInterface status=0x%08X\n", status);
    if (!NT_SUCCESS(status)) {
        return status;
    }

    status = QueueInitialize(device);
    PtpLog(L"[VirtualPtpDriver] DeviceCreate: QueueInitialize status=0x%08X\n", status);
    return status;
}
