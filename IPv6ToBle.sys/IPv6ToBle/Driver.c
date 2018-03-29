/*++

Module Name:

    driver.c

Abstract:

    This file contains the driver entry points and callback implementations.

Environment:

    Kernel-mode Driver Framework

--*/

#include "Includes.h"
#include "driver.tmh"   // auto-generated tracing file

#ifdef ALLOC_PRAGMA
#pragma alloc_text (INIT, DriverEntry)
#endif

_Use_decl_annotations_
NTSTATUS
DriverEntry(
    _In_ PDRIVER_OBJECT  DriverObject,
    _In_ PUNICODE_STRING RegistryPath
    )
/*++

Routine Description:
    DriverEntry initializes the driver and is the first routine called by the
    system after the driver is loaded. DriverEntry specifies the other entry
    points in the function driver, such as DriverUnload.

Parameters Description:

    DriverObject- represents the instance of the function driver that is loaded
    into memory. DriverEntry must initialize members of DriverObject before it
    returns to the caller. DriverObject is allocated by the system before the
    driver is loaded, and it is released by the system after the system unloads
    the function driver from memory.

    RegistryPath - represents the driver specific path in the Registry.
    The function driver can use the path to store driver related data between
    reboots. The path does not store hardware instance specific data.

Return Value:

    STATUS_SUCCESS if successful,
    STATUS_UNSUCCESSFUL otherwise.

--*/
{
	NTSTATUS status = STATUS_SUCCESS;

	// Objects for driver creation
    WDF_DRIVER_CONFIG config;
	WDFDRIVER driver;	    

    // Initialize WPP Tracing
    WPP_INIT_TRACING(DriverObject, RegistryPath);
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DRIVER, "%!FUNC! Entry");

	//
	// Step 1
	// Prepare for driver object creation
	//

	// Initialize the driver config structure. Second parameter is does not
	// have a pointer to a device add callback because there is no device add 
	// callback in a non-PnP driver like this
    WDF_DRIVER_CONFIG_INIT(&config,
						   WDF_NO_EVENT_CALLBACK
                           );
	
	// Indicate that this is not a PnP driver
	config.DriverInitFlags = WdfDriverInitNonPnpDriver;

	// Specify the driver's unload function
	config.EvtDriverUnload = IPv6ToBleEvtDriverUnload;

	//
	// Step 2
	// Create the WDF driver object
	//
    status = WdfDriverCreate(DriverObject,
                             RegistryPath,
                             WDF_NO_OBJECT_ATTRIBUTES,
                             &config,
                             &driver
                             );    
	if (!NT_SUCCESS(status)) 
	{
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DRIVER, "WdfDriverCreate failed %!STATUS!", status);
        WPP_CLEANUP(DriverObject);
		goto Exit;
    }

	// 
	// Step 3
	// Create the control device object
	//
	status = IPv6ToBleControlDeviceCreate(driver);	

	if (!NT_SUCCESS(status)) 
	{
		TraceEvents(TRACE_LEVEL_ERROR, TRACE_DRIVER, "WdfDriverCreate failed %!STATUS!", status);
		goto Exit;
	}

    // Get the associated WDM device object, for registering the callouts
    wdmDeviceObject = WdfDeviceWdmGetDeviceObject(wdfDeviceObject);

    //
    // Step 4
    // Create the injection handle for packet injection. We do that here
    // because, on the gateway device, we may not register callouts right away
    // if loading lists from the registry fails. But we still want to create
    // the injection handle in case the callouts are registered later (e.g.
    // after entries are added to the white list and mesh list).
    //
    status = FwpsInjectionHandleCreate0(AF_INET6,
                                        FWPS_INJECTION_TYPE_NETWORK,
                                        &injectionHandle
                                        );
    if (!NT_SUCCESS(status))
    {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DRIVER, "FwpsInjectionHandleCreate0 failed %!STATUS!", status);
        goto Exit;
    }

	//
	// Step 5
	// Open the driver's registry key, then see what is there and populate
	// the device context with runtime information about the white list and
	// mesh list if applicable. Close keys when done.
    //
    // Note: This only applies to the gateway device.
	//

#ifdef BORDER_ROUTER
    BOOLEAN parametersKeyOpened = FALSE;

	// Open the key; the API creates the key if it is not there so this would
	// only fail if we lack permissions, low resources, or some such issue
	status = WdfDriverOpenParametersRegistryKey(driver,
												KEY_READ,
												WDF_NO_OBJECT_ATTRIBUTES,
												&parametersKey
												);	
	if (!NT_SUCCESS(status)){
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DRIVER, "Opening parameters registry key failed %!STATUS!", status);
		goto Exit;
	}
    parametersKeyOpened = TRUE;

	// Populate the lists. 
	// 
	// We still want to succeed DriverEntry if we were unsuccessful at
	// loading info from the registry about the two lists. Record the error
	// with tracing, then re-set status to SUCCESS and exit gracefully. This 
	// will always happen the very first time the driver is installed because 
	// there's nothing in the registry yet, or if the user cleared out one or
	// both of the lists between reboots.
	BOOLEAN whiteListLoaded = TRUE;
	status = IPv6ToBleRegistryRetrieveWhiteList();
	if (!NT_SUCCESS(status))
	{		
		// We ignore status if this call fails because we stil want to check
		// the mesh list. But we mark that it failed.
		whiteListLoaded = FALSE;
		TraceEvents(TRACE_LEVEL_ERROR, TRACE_DRIVER, "Loading registry info for the white list failed %!STATUS!", status);
	}

	BOOLEAN meshListLoaded = TRUE;
	status = IPv6ToBleRegistryRetrieveMeshList();
	if (!NT_SUCCESS(status)) 
	{
		meshListLoaded = FALSE;
		TraceEvents(TRACE_LEVEL_ERROR, TRACE_DRIVER, "Loading registry info for the mesh list failed %!STATUS!", status);
	}

	// Close keys
	if (whiteListLoaded)
	{
		WdfRegistryClose(whiteListKey);
	}
	if (meshListLoaded)
	{
		WdfRegistryClose(meshListKey);
	}
    if (parametersKeyOpened)
    {
        WdfRegistryClose(parametersKey);
    }	

	// Still succeed if one failed but not the other, or if they both failed,
	// but don't continue past here. Callout/filter is not registered, and the
	// driver just sits waiting for the usermode app to give it enough info
	// (i.e. add enough entries to the lists so each has at least one).
	if ((!whiteListLoaded && meshListLoaded) ||
		(whiteListLoaded && !meshListLoaded) ||
		(!whiteListLoaded && !meshListLoaded))
	{
		status = STATUS_SUCCESS;
        TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DRIVER, "Could not load both white list and mesh list, succeeding DriverEntry anyway.");
		goto Exit;
	}
#endif  // BORDER_ROUTER

	//
	// Step 6
	// Register the callout and filter. 
    //
    // BORDER_ROUTER device
    // By getting this far, it means that we have at least one item in each of
    // the lists and we are ready to listen for incoming IPv6 packets that 
    // match the conditions. While we do need at least one item in each list, 
    // we only match packets based on white list addresses to reduce
    // performance impact. This is also to reduce complexity in the logic for 
    // adding filters. Then we compare to the mesh list during the ClassifyFn.
	//	
    // Pi/IoT device
    // We always hit this point, as we don't mess with the registry on this
    // device.
    //

	// Register the callout and filter(s)
	status = IPv6ToBleCalloutsRegister();
    if (!NT_SUCCESS(status))
    {
        goto Exit;
    }

#ifdef BORDER_ROUTER

    // Set that callouts are registered. On the gateway device, they may or
    // may not be registered, depending on the state of the white list and
    // mesh list. On the IoT core devices, the callouts are always registered.
    PIPV6_TO_BLE_DEVICE_CONTEXT deviceContext = IPv6ToBleGetContextFromDevice(
                                                    wdfDeviceObject
                                                );
    deviceContext->calloutsRegistered = TRUE;

#endif  // BORDER_ROUTER

Exit:

	// clean up the handles if we failed
	if (!NT_SUCCESS(status))
	{
		if (filterEngineHandle)
		{
			IPv6ToBleCalloutsUnregister();
		}
		if (injectionHandle)
		{
			FwpsInjectionHandleDestroy0(injectionHandle);
		}

        // Stop WPP Tracing if DriverEntry fails
        WPP_CLEANUP(DriverObject);
	} 
    else
    {
        TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DRIVER, "%!FUNC! Exit");
    }	

    return status;
}

_Use_decl_annotations_
VOID
IPv6ToBleEvtDriverUnload(
	_In_ WDFDRIVER Driver
)
/*++
Routine Description:

	Unload function for the driver. WFP callout drivers must guarantee that
	the callouts they registered with the filter engine are unregistered
	before the system unloads the driver's memory.

Arguments:

	Driver - the driver's driver object.

Return Value:

	None.

--*/
{
	TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DRIVER, "%!FUNC! Entry");

	// We don't need to do anything to clean up the driver object itself
	UNREFERENCED_PARAMETER(Driver);

	// Unregister the callout
	IPv6ToBleCalloutsUnregister();

	// Destroy the injection handle. If this fails then just log the error
    // since the driver is unloading.
	NTSTATUS status = FwpsInjectionHandleDestroy0(injectionHandle);
    if (!NT_SUCCESS(status))
    {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DRIVER, "Destroying the injection handle failed %!STATUS!", status);
    }

	TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DRIVER, "%!FUNC! Exit");

    // Stop WPP Tracing
    WPP_CLEANUP(WdfDriverWdmGetDriverObject(Driver));
}