/*++

Module Name:

    queue.c

Abstract:

    This file contains the queue entry points and callbacks.

Environment:

    Kernel-mode Driver Framework

--*/

#include "Includes.h"
#include "queue.tmh"    // auto-generated tracing file

#ifdef ALLOC_PRAGMA
#pragma alloc_text (PAGE, IPv6ToBleQueuesInitialize)
#endif

_Use_decl_annotations_
NTSTATUS
IPv6ToBleQueuesInitialize(
    _In_ WDFDEVICE Device
)
/*++

Routine Description:

     The I/O dispatch callbacks for the frameworks device object
     are configured in this function.

     A single default I/O Queue is configured for parallel request
     processing.

	 Subsequent queues can be created for additional queue needs. Additional
	 queues are stored in the device's context space.

Arguments:

    Device - Handle to a framework device object.

Return Value:

    VOID

--*/
{
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_QUEUE, "%!FUNC! Entry");

    WDFQUEUE defaultQueue;	// Required default queue for framework requests
    NTSTATUS status = STATUS_SUCCESS;
    WDF_IO_QUEUE_CONFIG queueConfig;

    PAGED_CODE();

    //
	// Step 1
    // Configure a default queue so that requests that are not
    // configure-fowarded using WdfDeviceConfigureRequestDispatching to goto
    // other queues get dispatched here.
    //
    WDF_IO_QUEUE_CONFIG_INIT_DEFAULT_QUEUE(&queueConfig,
										   WdfIoQueueDispatchParallel
										   );

	// Set the I/O control callback on the default queue.
	// The following comment is borrowed from the OSR Inverted sample driver.
	// 
	// This driver only handles IOCTLs. WDF will automagically handle Create 
	// and Close requests for us and will complete any other request types 
	// with STATUS_INVALID_DEVICE_REQUEST. 
    queueConfig.EvtIoDeviceControl = IPv6ToBleEvtIoDeviceControl;

	// Set the default queue to be non-power managed because this is a
	// software-only device
	queueConfig.PowerManaged = WdfFalse;

	// Create the default queue
    status = WdfIoQueueCreate(Device,
							  &queueConfig,
							  WDF_NO_OBJECT_ATTRIBUTES,
							  &defaultQueue
							  );
    if(!NT_SUCCESS(status)) {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_QUEUE, "WdfIoQueueCreate for default queue failed %!STATUS!", status);
		goto Exit;
    }

	//
	// Step 2
	// Configure secondary, manual-dispatch queue for our IOCTL notifications
	// we'll receive from the usermode app. This queue lives in the driver
	// context space.
	//

	// Get the device context
	PIPV6_TO_BLE_DEVICE_CONTEXT deviceContext = IPv6ToBleGetContextFromDevice(
		wdfDeviceObject
	);

	// Configure the queue to manual dispatch and non-power managed
	WDF_IO_QUEUE_CONFIG_INIT(&queueConfig,
							 WdfIoQueueDispatchManual
							 );
	queueConfig.PowerManaged = WdfFalse;

	// Create the manual queue
	status = WdfIoQueueCreate(wdfDeviceObject,
							  &queueConfig,
							  WDF_NO_OBJECT_ATTRIBUTES,
							  &deviceContext->listenRequestQueue
							  );
    if (!NT_SUCCESS(status))
    {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_QUEUE, "WdfIoQueueCreate for listen request queue failed %!STATUS!", status);
    }

Exit:
	
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_QUEUE, "%!FUNC! Exit");

    return status;
}

_Use_decl_annotations_
VOID
IPv6ToBleEvtIoDeviceControl(
    _In_ WDFQUEUE Queue,
    _In_ WDFREQUEST Request,
    _In_ size_t OutputBufferLength,
    _In_ size_t InputBufferLength,
    _In_ ULONG IoControlCode
    )
/*++

Routine Description:

    This event is invoked when the framework receives IRP_MJ_DEVICE_CONTROL 
	request.

	Of note is that the Windows Driver Framework automatically synchronizes
	this callback function, so accessing the device context space within
	this function is guaranteed to be safe. Thus, no locks are required for
	accessing or modifying the data in the device context space.

	The runtime white list and mesh device list are read and modified either
	from within DriverEntry or from within this callback (including functions
    invoked by this callback). Reading and modification is triggered either by 
    assigning values from the registry or by registering callouts.

	Because those lists are read and modified ONLY from within THIS callback or
	DriverEntry, we don't have to synchronize access to them at all. 

    This all holds true at IRQL = PASSIVE_LEVEL.

    The only other function in this driver that will need to access the lists
    is the periodic timer callback, which executes at DISPATCH_LEVEL as it is
    a Deferred Procedure Call (DPC). The only overlap between that callback and
    this one is checking if the lists have been modified, so the functions to
    modify the runtime lists that are called from this callback do acquire the
    spinlock just to update that boolean. There is no race condition because
    the timer callback will keep checking every 5 seconds and will catch up
    in short order if it missed an update (e.g. the timer callback had the
    spinlock, which stopped this function from setting the "modified" boolean
    to TRUE during an update, then this function updates the boolean after the
    timer callback returns, then the timer checks again and sees the update).

Arguments:

    Queue -  Handle to the framework queue object that is associated with the
             I/O request.

    Request - Handle to a framework request object.

    OutputBufferLength - Size of the output buffer in bytes

    InputBufferLength - Size of the input buffer in bytes

    IoControlCode - I/O control code as defined in Public.h.

Return Value:

    VOID

--*/
{
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_QUEUE, "%!FUNC! Entry");
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_QUEUE, "%!FUNC! Queue 0x%p, Request 0x%p OutputBufferLength %d InputBufferLength %d IoControlCode %d", Queue, Request, (int) OutputBufferLength, (int) InputBufferLength, IoControlCode);

	PIPV6_TO_BLE_DEVICE_CONTEXT deviceContext;
	NTSTATUS status = STATUS_INVALID_PARAMETER;
    KIRQL irql = KeGetCurrentIrql();

	// Get the device context
	deviceContext = IPv6ToBleGetContextFromDevice(wdfDeviceObject);

	// Switch based on the IOCTL sent to us by the usermode app(s)
	switch (IoControlCode)
	{
        // IOCTL 1: Listen inbound or outbound.
        //
		// On the gateway device, represents a request to listen for INBOUND
        // IPv6 packets. On the Pi/IoT device, represents a request to listen
        // for OUTBOUND IPv6 packets.
		//
		// This IOCTL is sent by the background packet processing app on BOTH
        // the gateway and Pi/IoT devices. The difference is that the gateway
        // will access the listen request queue when intercepting INBOUND
        // traffic, while the the Pi/IoT device will access the listen request
        // queue when intercepting OUTBOUND traffic.
        //
        case IOCTL_IPV6_TO_BLE_LISTEN_NETWORK_V6:
		{

			// First check if the supplied output buffer is big enough to
			// hold an IPv6 packet. IPv6 is normally not fragmented so the
			// buffer should be the MTU size. Typical MTU size for Bluetooth
			// is 1280 octets, or bytes.
			//
			// NOTE: This means that packets passed from this driver to the
			// usermode app will be, at most, 1280 bytes (octets). The driver
			// will not be able to handle packets larger than that and will
			// drop them (see the inbound IPv6 packet classify callback).
			if (OutputBufferLength < (sizeof(BYTE) * 1280))
			{

				// If not provided enough space, return invalid parameter, set 
				// previously
				break;
			}

			// Attempt to forward the request to the listening queue. If not
			// successful, complete the request with whatever status we get
			// back from WdfRequestForwardToIoQueue.
            //
            // Use a spin lock to guard adding the request to the listen queue.
            // The spin lock automatically raises this thread to DISPATCH_LEVEL
            // when acquired, blocking other thread interrupts, and lowers it
            // when released.
            WdfSpinLockAcquire(deviceContext->listenRequestQueueLock);
			status = WdfRequestForwardToIoQueue(Request,
				                                deviceContext->listenRequestQueue
			                                    );
            WdfSpinLockRelease(deviceContext->listenRequestQueueLock);

            NT_ASSERT(irql == KeGetCurrentIrql());

			if (!NT_SUCCESS(status))
			{
                TraceEvents(TRACE_LEVEL_ERROR, TRACE_QUEUE, "Forwarding I/O request to listening queue failed %!STATUS!", status);
				break;
			}

			// If successful in forwarding the request to the listening
			// queue, return here with the request pending and **do not break
			// or fall through**
			return;

		}

        //
        // IOCTL 2: Inject inbound
        //
        // This IOCTL is sent as a request to inject an IPv6 packet into the
        // device's inbound TCP/IP stack. The IPv6 packet is supplied in the
        // input buffer and is injected into the inbound network layer.
        //
        // This IOCTL is sent by the usermode packet processing app on both the
        // BORDER_ROUTER device AND the Pi/IoT device.
        //
        case IOCTL_IPV6_TO_BLE_INJECT_INBOUND_NETWORK_V6:
        {
            // Inject the packet
            status = IPv6ToBleQueueInjectNetworkInboundV6(Request);
            break;
        }

#ifdef BORDER_ROUTER

        //
        // IOCTL 3: Inject outbound
        //
		// This IOCTL is sent as a request to send an IPv6 packet out. The
		// IPv6 packet is supplied in the input buffer and is injected into the 
		// outbound network layer. The packet is assumed to be formed correctly
		// by the usermode packet processing app; that is, it has a correctly
		// formed UDP header and IPv6 header, as it will re-traverse the TCP/IP
		// stack and will be validated along the way.
		//
		// This IOCTL is sent by the usermode packet processing app and is
        // ONLY used on the gateway device to send a packet back out to the 
        // internet (e.g. an ACK to a previous status request from outside).
        //
		case IOCTL_IPV6_TO_BLE_INJECT_OUTBOUND_NETWORK_V6: 
        {

			// Inject the packet
			status = IPv6ToBleQueueInjectNetworkOutboundV6(Request);
            break;
		}        

        //
        // IOCTL 4: Add to white list
        //
        // This IOCTL is sent as a request to add an address to the white list.
        // The desired address is supplied in the WDFREQUEST input buffer.
        //
        // It is sent by the usermode GUI app after the app has
        // successfully registered a trusted external device.
        //
        // This IOCTL is used ONLY on the gateway device.
        //
        case IOCTL_IPV6_TO_BLE_ADD_TO_WHITE_LIST:
        {
            status = IPv6ToBleRuntimeListAssignWhiteListEntry(Request);
            break;
        }

        //
        // IOCTL 5: Remove from white list
        //
        // This IOCTL is sent as a request to remove an address from the white
        // list. The desired address is supplied in the WDFREQUEST input 
        // buffer.
        //
        // This IOCTL is sent by the usermode GUI app after the app has
        // successfully unregistered a trusted external device.
        //
        // This IOCTL is used ONLY on the gateway device.
        //
        case IOCTL_IPV6_TO_BLE_REMOVE_FROM_WHITE_LIST:
        {
            status = IPv6ToBleRuntimeListRemoveWhiteListEntry(Request);
            break;
        }

        //
        // IOCTL 6: Add to mesh list
        //
        // This IOCTL is sent as a request to add an address to the mesh list.
        // The desired address is supplied in the WDFREQUEST input buffer.
        //
        // It is sent by the usermode GUI app after the app has
        // successfully provisioned a new device into the BLE mesh network.
        //
        // This IOCTL is used only on the gateway device.
        //
        case IOCTL_IPV6_TO_BLE_ADD_TO_MESH_LIST:
        {
            status = IPv6ToBleRuntimeListAssignMeshListEntry(Request);
            break;
        }

        //
        // IOCTL 7: Remove from mesh list
        //
        // This IOCTL is sent as a request to remove an address from the mesh
        // list. The desired address is supplied in the WDFREQUEST input 
        // buffer.
        //
        // It is sent by the usermode GUI app after the app has
        // successfully deleted a device from the BLE mesh network.
        //
        // This IOCTL is ONLY used on the gateway device.
        //
        case IOCTL_IPV6_TO_BLE_REMOVE_FROM_MESH_LIST:
        {
            status = IPv6ToBleRuntimeListRemoveMeshListEntry(Request);
            break;
        }

#endif // BORDER_ROUTER

        default:
        {
            TraceEvents(TRACE_LEVEL_ERROR, TRACE_QUEUE, "Invalid IOCTL received.\n");
            break;
        }
	}

	// Complete the request with the returned status from the called operation
    WdfRequestComplete(Request, status);

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_QUEUE, "%!FUNC! Exit");

    return;
}

_Use_decl_annotations_
NTSTATUS
IPv6ToBleQueueInjectNetworkInboundV6(
    _In_ WDFREQUEST Request
)
/*++
Routine Description:

Injects a provided IPv6 packet into the inbound Network Layer data path
when called by the usermode packet processing app.

Because we create a brand new packet ourselves and this function is NOT
called from a classify callback function, we don't have to worry about
loopback packets. Also, because this is IPv6, we don't have to worry about
checksum.

The only question is the iFace index and sub-iFace index parameters for the
injection function. Normally, this function is called from a classify
callback and the system provides those to you (the indices of the original
network interface on which the original packet was indicated). But we don't
have that information because the packet was received over Bluetooth.

This function is used on both the gateway device and on the IoT Core
devices.

Arguments:

Request - the WDFREQUEST object that contains the packet from usermode.

Return Value:

STATUS_SUCCESS if the packet was successfully injected. Other appropriate
NTSTATUS error codes otherwise, depending on where the failure occurred.

--*/
{
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_INJECT_NETWORK_INBOUND, "%!FUNC! Entry");

    NTSTATUS status = STATUS_SUCCESS;
    PVOID inputBuffer;
    size_t receivedSize;

    NET_BUFFER_LIST* NBL = 0;
    KIRQL irql = KeGetCurrentIrql();

    //
    // Step 1
    // Retrieve the packet to send from the input buffer of the WDFREQUEST. The
    // minimum size is 40 bytes (fixed size for IPv6 header) + 8 bytes for the
    // UDP header + at least 1 byte for the rest of the payload. This is the 
    // minimum; the packet will likely be larger than this (but must be less 
    // than 1280 as that is the MTU for Bluetooth radios).
    //
    BYTE* packetFromUsermode;
    status = WdfRequestRetrieveInputBuffer(Request,
                                          (sizeof(BYTE) * 49),
                                          &inputBuffer,
                                          &receivedSize
                                          );
    if (!NT_SUCCESS(status))
    {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_INJECT_NETWORK_INBOUND, "Retrieving input buffer from WDFREQUEST failed during %!FUNC! with %!STATUS!", status);
        goto Exit;
    }
    packetFromUsermode = (BYTE*)inputBuffer;

    //
    // Step 2
    // Create the NET_BUFFER_LIST from the buffer
    //

    // Get the context so we can retrieve the NDIS NBL pool handle
    PIPV6_TO_BLE_DEVICE_CONTEXT deviceContext = IPv6ToBleGetContextFromDevice(
        wdfDeviceObject
    );
    HANDLE nblPoolHandle = deviceContext->ndisPoolData->nblPoolHandle;

    // Create the NBL from the usermode packet (buffer)
    NBL = IPv6ToBleNBLCreateFromBuffer(nblPoolHandle,
                                       packetFromUsermode,
                                       &receivedSize
                                       );
    if (!NBL)
    {
        status = STATUS_INSUFFICIENT_RESOURCES;
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_INJECT_NETWORK_INBOUND, "Creating NBL from usermode packet failed during %!FUNC! with %!STATUS!", status);
        goto Exit;
    }

    //
    // Step 3
    // Inject the packet into the receive path
    //
    status = FwpsInjectNetworkReceiveAsync0(injectionHandle,
                                            0,
                                            0,
                                            DEFAULT_COMPARTMENT_ID,
                                            0,   // New packet, so no original
                                                 // iFace index?
                                            0,   // Or sub-iFace index?
                                            NBL,
                                            IPv6ToBleQueueInjectNetworkComplete,
                                            NULL
                                            );

    NT_ASSERT(irql == KeGetCurrentIrql());

    if (!NT_SUCCESS(status))
    {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_INJECT_NETWORK_INBOUND, "Inbound injection at network layer failed %!STATUS!", status);
    }


Exit:

    // Free memory if injection failed but the NBL was successfully allocated,
    // as if injection fails then the completion function is not called
    if (!NT_SUCCESS(status))
    {
        if (NBL)
        {
            FwpsFreeNetBufferList0(NBL);
            NBL = 0;
        }
    }

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_INJECT_NETWORK_INBOUND, "%!FUNC! Exit");

    return status;
}

_Use_decl_annotations_
NTSTATUS
IPv6ToBleQueueInjectNetworkOutboundV6(
	_In_ WDFREQUEST Request
)
/*++
Routine Description:

	Injects a provided IPv6 packet into the outbound Network Layer data path
	when called by the usermode packet processing app.

    This function is only used on the gateway device.

Arguments:

	Request - the WDFREQUEST object that contains the packet from usermode.

Return Value:

	STATUS_SUCCESS if the packet was successfully injected. Other appropriate
	NTSTATUS error codes otherwise, depending on where the failure occurred.

--*/
{
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_INJECT_NETWORK_OUTBOUND, "%!FUNC! Entry");

	NTSTATUS status = STATUS_SUCCESS;
	PVOID inputBuffer;
	size_t receivedSize;

    NET_BUFFER_LIST* NBL = 0;
    KIRQL irql = KeGetCurrentIrql();

	//
	// Step 1
	// Retrieve the packet to send from the input buffer of the WDFREQUEST. The
    // minimum size is 40 bytes (fixed size for IPv6 header) + 8 bytes for the
    // UDP header + at least 1 byte for the payload. This is the minimum; the 
    // packet will likely be larger than this.
	//
	BYTE* packetFromUsermode;
    status = WdfRequestRetrieveInputBuffer(Request,
                                          (sizeof(BYTE) * 49),
										  &inputBuffer, 
										  &receivedSize
										  );
	if (!NT_SUCCESS(status)) 
	{
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_INJECT_NETWORK_OUTBOUND, "Retrieving input buffer from WDFREQUEST failed during %!FUNC! with %!STATUS!", status);
		goto Exit;
	}
	packetFromUsermode = (BYTE*)inputBuffer;

	//
	// Step 2
	// Create the NET_BUFFER_LIST from the buffer
	//

    // Get the context so we can retrieve the NDIS NBL pool handle
	PIPV6_TO_BLE_DEVICE_CONTEXT deviceContext = IPv6ToBleGetContextFromDevice(
													wdfDeviceObject
												);
    HANDLE nblPoolHandle = deviceContext->ndisPoolData->nblPoolHandle;

    // Create the NBL from the usermode packet (buffer)
	NBL = IPv6ToBleNBLCreateFromBuffer(nblPoolHandle, 
		                               packetFromUsermode, 
		                               &receivedSize
		                               );
	if (!NBL)
	{
		status = STATUS_INSUFFICIENT_RESOURCES;
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_INJECT_NETWORK_OUTBOUND, "Creating NBL from usermode packet failed during %!FUNC! with %!STATUS!", status);
		goto Exit;
	}
	
	//
	// Step 3
	// Inject the packet into the send path
    //
    status = FwpsInjectNetworkSendAsync0(injectionHandle,
                                         0,
                                         0,
                                         DEFAULT_COMPARTMENT_ID,
                                         NBL,
                                         IPv6ToBleQueueInjectNetworkComplete,
                                         NULL
                                         );

    NT_ASSERT(irql == KeGetCurrentIrql());

    if (!NT_SUCCESS(status))
    {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_INJECT_NETWORK_OUTBOUND, "Outbound injection at network layer failed %!STATUS!", status);
    }

Exit:

    // Free memory if injection failed but the NBL was successfully allocated,
    // because if injection fails then the completion function is not called
    if (!NT_SUCCESS(status))
    {
        if (NBL)
        {
            FwpsFreeNetBufferList0(NBL);
            NBL = 0;
        }
    }

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_INJECT_NETWORK_OUTBOUND, "%!FUNC! Exit");

	return status;

}

_Use_decl_annotations_
VOID NTAPI
IPv6ToBleQueueInjectNetworkComplete(
    _In_    void*               context,
    _Inout_ NET_BUFFER_LIST*    netBufferList,
    _In_    BOOLEAN             dispatchLevel
)
/*++
Routine Description:

    Called by the filter engine when a packet has been successfully injected
    into the outbound stack. Performs memory cleanup, etc.

Arguments:

    context - A pointer to the completionContext parameter of the packet
    injection function.

    netBufferList - the NET_BUFFER_LIST parameter from the injection function.

    dispatchLevel - the IRQL at which this is called. It can be called at
    <= DISPATCH_LEVEL. If this is true the function is being called at
    DISPATCH_LEVEL; otherwise it is being called at < DISPATCH_LEVEL.

Return Value:

    None.

--*/
{
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_INJECT_NETWORK_COMPLETE, "%!FUNC! Entry");

    // This was not passed in as we don't need any kind of completion data
    // such as statistics, tracking number of completed packet injections, etc.
    UNREFERENCED_PARAMETER(context);

    // Doesn't matter if this is called at or below DISPATCH_LEVEL
    UNREFERENCED_PARAMETER(dispatchLevel);

    //
    // Step 1
    // Verify the injection succeeded
    //
    NT_ASSERT(netBufferList);
    NT_ASSERT(netBufferList->Status);

    NTSTATUS status = netBufferList->Status;

    if (!NT_SUCCESS(status))
    {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_INJECT_NETWORK_COMPLETE, "Injection complete: NBL status did not succeed %!STATUS!", status);
    }

    //
    // Step 2
    // Free the NBL
    //
    FwpsFreeNetBufferList0(netBufferList);
    netBufferList = 0;

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_INJECT_NETWORK_COMPLETE, "%!FUNC! Exit");

    return;
}