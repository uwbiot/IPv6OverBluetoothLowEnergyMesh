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

    // Initialize WPP Tracing
    WPP_INIT_TRACING(DriverObject, RegistryPath);

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DRIVER, "%!FUNC! Entry");

	//
	// Step 1
	// Prepare for driver object creation
	//

    // Set the global callouts registered variable to FALSE to start
    gCalloutsRegistered = FALSE;

	// Initialize the driver config structure. Second parameter is does not
	// have a pointer to a device add callback because there is no device add 
	// callback in a non-PnP driver like this
    WDF_DRIVER_CONFIG_INIT(&config,
						   WDF_NO_EVENT_CALLBACK
                           );
	
	// Indicate that this is not a PnP driver
	config.DriverInitFlags |= WdfDriverInitNonPnpDriver;

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
                             &gWdfDriverObject
                             );    
	if (!NT_SUCCESS(status)) 
	{
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DRIVER, "WdfDriverCreate failed %!STATUS!", status);
        WPP_CLEANUP(DriverObject);
		goto Exit;
    }

    //
    // Step 3
    // Check the driver parameters key to see if we are running on the border
    // router or not
    //
    status = IPv6ToBleRegistryCheckBorderRouterFlag();
    if (!NT_SUCCESS(status))
    {
        goto Exit;
    }

    //
    // Step 4
    // Initialize the global objects
    //
    status = IPv6ToBleDriverInitGlobalObjects();
    if (!NT_SUCCESS(status))
    {
        goto Exit;
    }

	// 
	// Step 5
	// Create the control device object
	//
	status = IPv6ToBleDeviceCreate(gWdfDriverObject);

	if (!NT_SUCCESS(status)) 
	{
		TraceEvents(TRACE_LEVEL_ERROR, TRACE_DRIVER, "WdfDeviceCreate failed %!STATUS!", status);
		goto Exit;
	}    

    //
    // Step 6
    // Initialize the I/O queues
    //
    status = IPv6ToBleQueuesInitialize(gWdfDeviceObject);
    if (!NT_SUCCESS(status))
    {
        goto Exit;
    }

    //
    // Step 7
    // Finish initializing the control device object
    //
    WdfControlFinishInitializing(gWdfDeviceObject);

    // Get the associated WDM device object, for registering the callouts and
    // other functions that take the underlying WDM device object as a param
    gWdmDeviceObject = WdfDeviceWdmGetDeviceObject(gWdfDeviceObject);

    //
    // Step 8
    // Create the injection handle for packet injection. We do that here
    // because, on the border router device, we may not register callouts right away
    // if loading lists from the registry fails. But we still want to create
    // the injection handle in case the callouts are registered later (e.g.
    // after entries are added to the white list and mesh list).
    //
    status = FwpsInjectionHandleCreate0(AF_INET6,
                                        FWPS_INJECTION_TYPE_NETWORK,
                                        &gInjectionHandleNetwork
                                        );
    if (!NT_SUCCESS(status))
    {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DRIVER, "FwpsInjectionHandleCreate0 failed %!STATUS!", status);
        goto Exit;
    }	

	if (gBorderRouterFlag)
	{
		//
		// Step 9
		// Initialize and start the periodic timer. This applies only to the border
		// router.
		//
		status = IPv6ToBleDriverInitTimer();
		if (!NT_SUCCESS(status))
		{
			goto Exit;
		}

		//
		// Step 10
		// Populate the device context with runtime information about the white 
		// list and mesh list if applicable. These function calls open and close
		// the registry keys as needed.
		//
		// Note: This only applies to the border router device.
		//

		// Populate the lists. 
		// 
		// We still want to succeed DriverEntry if we were unsuccessful at
		// loading info from the registry about the two lists. Record the error
		// with tracing, then re-set status to SUCCESS and exit gracefully. This 
		// will always happen the very first time the driver is installed because 
		// there's nothing in the registry yet, or if the user cleared out one or
		// both of the lists between reboots.
		BOOLEAN whiteListLoaded = TRUE;
		status = IPv6ToBleRegistryRetrieveRuntimeList(WHITE_LIST);
		if (!NT_SUCCESS(status))
		{
			// We ignore status if this call fails because we stil want to check
			// the mesh list. But we mark that it failed.
			whiteListLoaded = FALSE;
			TraceEvents(TRACE_LEVEL_ERROR, TRACE_DRIVER, "Loading registry info for the white list failed %!STATUS!", status);
		}

		BOOLEAN meshListLoaded = TRUE;
		status = IPv6ToBleRegistryRetrieveRuntimeList(MESH_LIST);
		if (!NT_SUCCESS(status))
		{
			meshListLoaded = FALSE;
			TraceEvents(TRACE_LEVEL_ERROR, TRACE_DRIVER, "Loading registry info for the mesh list failed %!STATUS!", status);
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
	}

	//
	// Step 11
	// Register the callout(s) and filter. 
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

	status = IPv6ToBleCalloutsRegister();

Exit:

	// clean up the handles if we failed
	if (!NT_SUCCESS(status))
	{
		if (gFilterEngineHandle)
		{
			IPv6ToBleCalloutsUnregister();
		}
		if (gInjectionHandleNetwork)
		{
			FwpsInjectionHandleDestroy0(gInjectionHandleNetwork);
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
NTSTATUS
IPv6ToBleDriverInitGlobalObjects()
/*++
Routine Description:

    Initializes the global objects.

Arguments:

    None...they're global objects. :)

Return Value:

    STATUS_SUCCESS if the operations were successful; appropriate NTSTATUS
    error code otherwise.

--*/
{
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DRIVER, "%!FUNC! Entry");

    NTSTATUS status = STATUS_SUCCESS;

#if DBG
    KIRQL irql = KeGetCurrentIrql();
#endif  // DBG

    //
    // Step 1
    // Initialize the spin locks
    //

    // Listen request queue spinlock
    WDF_OBJECT_ATTRIBUTES listenRequestQueueLockAttributes;
    WDF_OBJECT_ATTRIBUTES_INIT(&listenRequestQueueLockAttributes);
    listenRequestQueueLockAttributes.ParentObject = gWdfDeviceObject;

    status = WdfSpinLockCreate(&listenRequestQueueLockAttributes,
                               &gListenRequestQueueLock
                               );
    if (!NT_SUCCESS(status))
    {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DRIVER, "Creating listen request queue spin lock failed %!STATUS!", status);
        goto Exit;
    }

	if (gBorderRouterFlag)
	{
		// White list spinlock
		WDF_OBJECT_ATTRIBUTES whiteListModifiedLockAttributes;
		WDF_OBJECT_ATTRIBUTES_INIT(&whiteListModifiedLockAttributes);
		whiteListModifiedLockAttributes.ParentObject = gWdfDeviceObject;

		status = WdfSpinLockCreate(&whiteListModifiedLockAttributes,
								   &gWhiteListModifiedLock
								   );
		if (!NT_SUCCESS(status))
		{
			TraceEvents(TRACE_LEVEL_ERROR, TRACE_DRIVER, "Creating white list modified spin lock failed %!STATUS!", status);
			goto Exit;
		}

		// Mesh list spinlock
		WDF_OBJECT_ATTRIBUTES meshListModifiedLockAttributes;
		WDF_OBJECT_ATTRIBUTES_INIT(&meshListModifiedLockAttributes);
		meshListModifiedLockAttributes.ParentObject = gWdfDeviceObject;

		status = WdfSpinLockCreate(&meshListModifiedLockAttributes,
								   &gMeshListModifiedLock
								   );
		if (!NT_SUCCESS(status))
		{
			TraceEvents(TRACE_LEVEL_ERROR, TRACE_DRIVER, "Creating mesh list modified spin lock failed %!STATUS!", status);
			goto Exit;
		}

		NT_ASSERT(irql == KeGetCurrentIrql());

		//
		// Step 2
		// Initialize the list heads
		//

		// White list head
		gWhiteListHead = (PLIST_ENTRY)ExAllocatePoolWithTag(NonPagedPoolNx,
														   sizeof(LIST_ENTRY),
														   IPV6_TO_BLE_WHITE_LIST_TAG
														   );
		if (!gWhiteListHead)
		{
			status = STATUS_INSUFFICIENT_RESOURCES;
			goto Exit;
		}
		InitializeListHead(gWhiteListHead);

		// Mesh list head
		gMeshListHead = (PLIST_ENTRY)ExAllocatePoolWithTag(NonPagedPoolNx,
														   sizeof(LIST_ENTRY),
														   IPV6_TO_BLE_WHITE_LIST_TAG
														   );
		if (!gMeshListHead)
		{
			status = STATUS_INSUFFICIENT_RESOURCES;
			goto Exit;
		}
		InitializeListHead(gMeshListHead);

		//
		// Step 3
		// Initialize the list booleans
		//
		gWhiteListModified = FALSE;
		gMeshListModified = FALSE;
	}

    //
    // Step 4
    // Create the NDIS pool data structure, which also populates it
    //
    status = IPv6ToBleNDISPoolDataCreate(gNdisPoolData,
                                         IPV6_TO_BLE_NDIS_TAG
                                         );
    if (!NT_SUCCESS(status) && gNdisPoolData != NULL)
    {
        IPv6ToBleNDISPoolDataDestroy(gNdisPoolData);
    }

    NT_ASSERT(irql == KeGetCurrentIrql());

Exit:
    
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DRIVER, "%!FUNC! Exit");

    return status;
}

_Use_decl_annotations_
NTSTATUS
IPv6ToBleDriverInitTimer()
/*++
Routine Description:

    Initializes and starts the periodic timer object.

Arguments:

    None. Accesses the global timer variable.

Return Value:

    STATUS_SUCCESS if the operation was successful; appropriate NTSTATUS error
    code otherwise.

--*/
{
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DRIVER, "%!FUNC! Entry");

    NTSTATUS status = STATUS_SUCCESS;

    WDF_TIMER_CONFIG timerConfig;
    WDF_OBJECT_ATTRIBUTES timerAttributes;

    // Initialize the timer configuration object with the timer event callback
    // and a period of 5 seconds (5000 milliseconds)
    WDF_TIMER_CONFIG_INIT_PERIODIC(&timerConfig,
                                   IPv6ToBleTimerCheckAndFlushLists,
                                   5000
                                   );

    // Set the framework to automatically synchronize this with callbacks under
    // the parent object (the device)
    timerConfig.AutomaticSerialization = TRUE;

    // Initialize the timer attributes to make the device object its parent
    WDF_OBJECT_ATTRIBUTES_INIT(&timerAttributes);
    timerAttributes.ParentObject = gWdfDeviceObject;

    // Create the timer
    status = WdfTimerCreate(&timerConfig,
                            &timerAttributes,
                            &gRegistryTimer
                            );
    if (!NT_SUCCESS(status))
    {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DRIVER, "Timer creation failed failed %!STATUS!", status);
        goto Exit;
    }

    // Start the timer
    WdfTimerStart(gRegistryTimer, WDF_REL_TIMEOUT_IN_MS(5000));

Exit:

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DRIVER, "%!FUNC! Exit");

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

    Besides unregistering the callouts, this function cleans up the globals.

Arguments:

	Driver - the driver's driver object.

Return Value:

	None.

--*/
{
	TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DRIVER, "%!FUNC! Entry");

	// We don't need to do anything to clean up the driver object itself
	UNREFERENCED_PARAMETER(Driver);

    //
    // Step 1
    // Clean up callouts
    //

	// Unregister the callouts
	IPv6ToBleCalloutsUnregister();

	// Destroy the injection handle. If this fails then just log the error
    // since the driver is unloading.
	NTSTATUS status = FwpsInjectionHandleDestroy0(gInjectionHandleNetwork);
    if (!NT_SUCCESS(status))
    {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DRIVER, "Destroying the injection handle failed %!STATUS!", status);
    }

	if (gBorderRouterFlag)
	{
		//
		// Step 2
		// Clean up the runtime lists
		//
		IPv6ToBleRuntimeListPurgeRuntimeList(WHITE_LIST);
		if (gWhiteListHead)
		{
			ExFreePoolWithTag(gWhiteListHead, IPV6_TO_BLE_WHITE_LIST_TAG);
		}

		IPv6ToBleRuntimeListPurgeRuntimeList(MESH_LIST);
		if (gMeshListHead)
		{
			ExFreePoolWithTag(gMeshListHead, IPV6_TO_BLE_MESH_LIST_TAG);
		}
	}

    //
    // Step 3
    // Clean up the NDIS memory pool data structure
    //
    IPv6ToBleNDISPoolDataDestroy(gNdisPoolData);

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DRIVER, "%!FUNC! Exit");

    // Stop WPP Tracing
    WPP_CLEANUP(gWdmDeviceObject);  
}

_Use_decl_annotations_
VOID
IPv6ToBleTimerCheckAndFlushLists(
    _In_    WDFTIMER    Timer
)
/*++
Routine Description:

    The framework calls this timer function every 5  seconds to check if the
    runtime lists have changed. If they have, flush the runtime lists to the
    registry.

    This behavior is to prevent loss of state; the driver generally works with
    the runtime lists so it doesn't have to open and close the registry keys
    all the time, but if the lists are modified during runtime then we need to
    save that state in the registry at some point.

    Since there is no way to guarantee that you will be able to flush to the
    registry once during device or driver unload, such as an unexpected
    shutdown, the solution is to flush every 5 seconds, and only if the lists
    have been modified. This is not a major load on the system.

    This function is called at DISPATCH_LEVEL. If the lists have changed, it
    queues a work item to assign the list to the registry at PASSIVE_LEVEL.

Arguments:

    Timer - the global WDF timer object

Return Value:

    None. The two functions called by this callback do return NTSTATUS, so if
    they fail we log the error and continue because this function returns VOID.

--*/
{
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_TIMER, "%!FUNC! Entry");

    UNREFERENCED_PARAMETER(Timer);

#if DBG
    KIRQL irql = KeGetCurrentIrql();
#endif // DBG

    //
    // Step 1
    // Flush the white list if it has changed by scheduling a PASSIVE_LEVEL
    // system worker thread. We use a system worker thread because assigning
    // the list to the registry is expected to be very infrequent and doesn't
    // take long to do (no delayed processing, etc.).
    //
    WdfSpinLockAcquire(gWhiteListModifiedLock);
    if (gWhiteListModified)
    {
        PIO_WORKITEM workItem = IoAllocateWorkItem(gWdmDeviceObject);
        if (workItem)
        {
            IoQueueWorkItemEx(workItem,
                              IPv6ToBleRegistryFlushWhiteListWorkItemEx,
                              DelayedWorkQueue,
                              NULL
                              );
        }
    }
    WdfSpinLockRelease(gWhiteListModifiedLock);

    NT_ASSERT(irql == KeGetCurrentIrql());

    //
    // Step 2
    // Flush the mesh list if it has changed, also with a system worker thread.
    //
    WdfSpinLockAcquire(gMeshListModifiedLock);
    if (gMeshListModified)
    {
        PIO_WORKITEM workItem = IoAllocateWorkItem(gWdmDeviceObject);
        if (workItem)
        {
            IoQueueWorkItemEx(workItem,
                              IPv6ToBleRegistryFlushMeshListWorkItemEx,
                              DelayedWorkQueue,
                              NULL
                              );
        }
    }
    WdfSpinLockRelease(gMeshListModifiedLock);

    NT_ASSERT(irql == KeGetCurrentIrql());

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_TIMER, "%!FUNC! Exit");

    return;
}