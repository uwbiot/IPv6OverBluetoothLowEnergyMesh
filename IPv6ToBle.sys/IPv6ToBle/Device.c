/*++

Module Name:

    device.c - Device handling events for example driver.

Abstract:

   This file contains the device entry points and callbacks.
    
Environment:

    Kernel-mode Driver Framework

--*/

#include "Includes.h"
#include "device.tmh"   // auto-generated tracing file

#ifdef ALLOC_PRAGMA
#pragma alloc_text (PAGE, IPv6ToBleDeviceCreate)
#endif

_Use_decl_annotations_
NTSTATUS
IPv6ToBleDeviceCreate(
    _In_	WDFDRIVER		Driver
)
/*++

Routine Description:

    Routine called to create a device and its software resources.

Arguments:

	Driver - The WDF driver object to which to associate this device.

Return Value:

    STATUS_SUCCESS if the device was successfully created and initialized;
    other appropriate NTSTATUS error codes otherwise.

--*/
{
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DEVICE, "%!FUNC! Entry");

	NTSTATUS status = STATUS_SUCCESS;

#if DBG
    KIRQL irql = KeGetCurrentIrql();
#endif // DBG

	// Object for device creation
	PWDFDEVICE_INIT deviceInit;    

	//
	// Step 1
	// Prepare for device creation
	//

    PAGED_CODE();

	// Allocate the device initialization structure with a security descriptor.
    // This security descriptor grants GENERIC_ALL permissions to SYSTEM, the
    // built-in administrator account, authenticated users, and appcontainer
    // applications. See wdmsec.h for details.
    DECLARE_CONST_UNICODE_STRING(IPV6_TO_BLE_PROTECTION,
                                 L"D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GA;;;AU)(A;;GA;;;AC)"
                                 );


	deviceInit = WdfControlDeviceInitAllocate(Driver,
											  &IPV6_TO_BLE_PROTECTION
											  );
	
	if (!deviceInit)
	{
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DEVICE, "Device init allocation failed %!STATUS!", status);
		status = STATUS_INSUFFICIENT_RESOURCES;
		goto Exit;
	}

	// Set the device type
	WdfDeviceInitSetDeviceType(deviceInit, FILE_DEVICE_NETWORK);

	// Set the security descriptor characteristics
	WdfDeviceInitSetCharacteristics(deviceInit, 
									FILE_DEVICE_SECURE_OPEN,
									FALSE
									);

    // Previous two functions can be called at DISPATCH_LEVEL; verify IRQL
    // did not change
    NT_ASSERT(irql == KeGetCurrentIrql());

	// Define a native name for the device and assign it to the device
	DECLARE_CONST_UNICODE_STRING(nativeDeviceName, L"\\Device\\IPv6ToBle");

	status = WdfDeviceInitAssignName(deviceInit, &nativeDeviceName);
	
	if (!NT_SUCCESS(status)) 
	{
        TraceEvents(TRACE_LEVEL_WARNING, TRACE_DEVICE, "Device init assigning native device name failed %!STATUS!", status);
        WdfDeviceInitFree(deviceInit);
		goto Exit;
	}

    NT_ASSERT(irql == KeGetCurrentIrql());

	//
	// Step 2 
	// Create the framework device object
	//
	status = WdfDeviceCreate(&deviceInit, 
							 WDF_NO_OBJECT_ATTRIBUTES,
							 &gWdfDeviceObject
							 );
	
	if (!NT_SUCCESS(status)) 
	{
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DEVICE, "Device creation failed %!STATUS!", status);
		WdfDeviceInitFree(deviceInit);
		goto Exit;
	}		

    NT_ASSERT(irql == KeGetCurrentIrql());

	//
	// Step 3
	// Make the device accessible to usermode apps with a symbolic link
	//

	// Define a friendly name for user mode apps to access the device
	DECLARE_CONST_UNICODE_STRING(userDeviceName, L"\\Global??\\IPv6ToBle");

	// Create a symbolic link to the created device object
	status = WdfDeviceCreateSymbolicLink(gWdfDeviceObject, 
										 &userDeviceName
										 );
	
	if (!NT_SUCCESS(status)) 
	{
        TraceEvents(TRACE_LEVEL_WARNING, TRACE_DEVICE, "Device creating symbolic link failed %!STATUS!", status);
		goto Exit;
	}

    //
    // Step 4
    // Finish initializing the control device object
    //
    WdfControlFinishInitializing(gWdfDeviceObject);

Exit:	

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DEVICE, "%!FUNC! Exit");

    return status;
}