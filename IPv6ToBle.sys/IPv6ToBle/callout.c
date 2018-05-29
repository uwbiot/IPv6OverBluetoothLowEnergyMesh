/*++

Module Name:

	callout.c

Abstract:

	This file contains the WFP callout driver's callbacks and main logic.

Environment:

	Kernel-mode Driver Framework

--*/

#include "Includes.h"
#include "callout.tmh"  // auto-generated tracing file

_Use_decl_annotations_
VOID
IPv6ToBleCalloutClassifyInboundIpPacketV6(
	_In_		const FWPS_INCOMING_VALUES0*			inFixedValues,
	_In_		const FWPS_INCOMING_METADATA_VALUES0*	inMetaValues,
	_Inout_opt_	void*									layerData,
	_In_opt_	const void*								classifyContext,
	_In_		const FWPS_FILTER2*						filter,
	_In_		UINT64									flowContext,
	_Inout_		FWPS_CLASSIFY_OUT0*						classifyOut
)
/*++
Routine Description:

	The callback for classifying inbound IPv6 packets at the IP_PACKET
    layer. The filter engine calls the driver's ClassifyFn function whenever 
    there is data to be processed by the callout.

	For this driver, if the driver receives IPv6 packets that are from a
	trusted external IPv6 address and are destined for an IPv6 address within
	the Bluetooth LE mesh network, it passes the packet up to the usermode
	packet processing background app.

    Filters in this driver are based on the white list to improve performance
    in the filter engine itself. This function then compares the received
    packet's destination to the mesh list addresses to see if it is destined
    for one of the mesh devices. 

    Note: the gateway PC on which this driver runs is assumed not to have been
    added to the mesh list. Therefore, traffic that does not match an address
    in the mesh list will be permitted as it is assumed to be traffic for the
    host.

    Procedures for incoming traffic:

        1. Verify the classifyFn callback has rights to alter the classify and
            didn't inspect the packet itself earlier. If the callback does not
            have rights to alter the classify or already inspected the packet,
            permit.
        2. Verify the packet is intended for a mesh device by examining the
            destination address. If it is not, permit.
        3. Try to retrieve a WDFREQUEST from the listening queue. If
            unsuccessful, that means the packet processing app is not running
            so the entire solution won't work. Or, the provided output buffer
            wasn't big enough to hold the paket. Block.        
        4. Verify the packet is a UDP datagram packet by examining the size of
            the transport header (UDP is 8 bytes). If it is a TCP segment,
            block.              
        5. Verify the data is no larger than 1280 bytes (octets), the MTU for
            Bluetooth. If it is larger, block. We do this by going ahead and
            converting the packet to the usermode byte array; the conversion
            function takes care of the IP header for us and reports the data
            size.        
        6. Put the converted byte array into the WDFREQUEST output buffer and 
            complete the request.
        7. Block/absorb the original packet unless it was permitted earlier.

Arguments:

	See https://docs.microsoft.com/windows-hardware/drivers/ddi/content/fwpsk/nc-fwpsk-fwps_callout_classify_fn2
	for parameter descriptions.

Return Value:

	VOID.

--*/
{
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_CLASSIFY_INBOUND_IP_PACKET_V6, "%!FUNC! Entry");

    UNREFERENCED_PARAMETER(classifyContext);
    UNREFERENCED_PARAMETER(filter);
    UNREFERENCED_PARAMETER(flowContext);

    NTSTATUS status = STATUS_SUCCESS;

#if DBG
    KIRQL irql = KeGetCurrentIrql();
#endif // DBG
    
    
    BYTE* outputBuffer = 0;

    WDFREQUEST outRequest = NULL;
    BOOLEAN requestRetrieved = FALSE;
    ULONG_PTR bytesTransferred = 0;

    UINT32 ipHeaderSize = inMetaValues->ipHeaderSize;
    UINT32 transportHeaderSize = inMetaValues->transportHeaderSize;

    FWPS_PACKET_INJECTION_STATE packetState;

    //
    // Step 1
    // Verify rights to alter the classify and check if we previously injected
    // the packet. This should never be the case, except possibly for loopbacks
    //
    if ((classifyOut->rights & FWPS_RIGHT_ACTION_WRITE) == 0)
    {
        TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_CLASSIFY_INBOUND_IP_PACKET_V6, "No rights to alter the classify during %!FUNC!");

        return;
    }

    NT_ASSERT(layerData);
    _Analysis_assume_(layerData);

    // Check to make sure we don't try to re-inspect packets we inspected
    // earlier; permit and clear the write permissions flag if so
    packetState = FwpsQueryPacketInjectionState0(gInjectionHandleNetwork,
                                                 layerData,
                                                 NULL
                                                 );
    if ((packetState == FWPS_PACKET_INJECTED_BY_SELF) ||
        (packetState == FWPS_PACKET_PREVIOUSLY_INJECTED_BY_SELF))
    {
        classifyOut->actionType = FWP_ACTION_PERMIT;
        if (filter->flags & FWPS_FILTER_FLAG_CLEAR_ACTION_RIGHT)
        {
            classifyOut->rights &= ~FWPS_RIGHT_ACTION_WRITE;
        }

        TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_CLASSIFY_INBOUND_IP_PACKET_V6, "Packet was injected by self earlier");

        return;
    }

    // Check for loopback packets and ignore them
    if (inFixedValues)
    {
        FWP_DATA_TYPE valueType = inFixedValues->incomingValue->value.type;
        if (valueType == FWP_UINT32)
        {
            UINT32 flags = inFixedValues->incomingValue->value.uint32;
            if (flags & FWP_CONDITION_FLAG_IS_LOOPBACK)
            {
                classifyOut->actionType = FWP_ACTION_PERMIT;
                if (filter->flags & FWPS_FILTER_FLAG_CLEAR_ACTION_RIGHT)
                {
                    classifyOut->rights &= ~FWPS_RIGHT_ACTION_WRITE;
                }

                TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_CLASSIFY_INBOUND_IP_PACKET_V6, "Permitting loopback packet.");

                return;
            }
        }
    }

    //
    // Step 2
    // Verify the destination if on the border router.
    // 
    // If the packet is intended for a mesh device, continue with the rest of
    // the function and pass the packet to the usermode packet processing app.
    //
    // If the packet is not intended for a mesh device, permit it as it must
    // be normal traffic destined elsewhere.
    //

    // Extract the destination address from the IP header by retreating 16
    // bytes (as it is at the end of the IPv6 header)
    UINT8 extractedAddress[16] = { 0 };
    NDIS_STATUS ndisStatus = NDIS_STATUS_SUCCESS;
    ndisStatus = NdisRetreatNetBufferListDataStart(layerData,
                                                    (sizeof(BYTE) * 16),
                                                    0,
                                                    NULL,
                                                    NULL
                                                    );
    if (ndisStatus != NDIS_STATUS_SUCCESS)
    {
        classifyOut->actionType = FWP_ACTION_PERMIT;
        if (filter->flags & FWPS_FILTER_FLAG_CLEAR_ACTION_RIGHT)
        {
            classifyOut->rights &= ~FWPS_RIGHT_ACTION_WRITE;
        }

        TraceEvents(TRACE_LEVEL_ERROR, TRACE_CLASSIFY_INBOUND_IP_PACKET_V6, "Retreating NBL failed during %!FUNC! with %!STATUS!, permitting packet", status);

        return;
    }

    // Copy the destination address
    RtlCopyMemory(extractedAddress, layerData, IPV6_ADDRESS_LENGTH);

    // Advance back to the end of the IP header if the retreat operation
    // succeeded
    NdisAdvanceNetBufferListDataStart(layerData,
                                        (sizeof(BYTE) * 16),
                                        FALSE,
                                        NULL
                                        );

    BOOLEAN isInMeshList = FALSE;

    // Compare each address in the mesh list to the destination address
    PLIST_ENTRY entry = gMeshListHead->Flink;
    while (entry != gMeshListHead)
    {
        PMESH_LIST_ENTRY meshListEntry = CONTAINING_RECORD(entry,
                                                            MESH_LIST_ENTRY,
                                                            listEntry
                                                            );
        // Compare the extracted address to this entry's address
        if (RtlEqualMemory(extractedAddress,
            &meshListEntry->ipv6Address,
            IPV6_ADDRESS_LENGTH))
        {
            // It is in the mesh list!
            isInMeshList = TRUE;
            break;
        }

        // Advance the list
        entry = entry->Flink;
    }

    // Permit the packet if it wasn't in the mesh list, as that means the
    // packet is destined elsewhere (normal traffic)
    if (!isInMeshList)
    {
        classifyOut->actionType = FWP_ACTION_PERMIT;
        if (filter->flags & FWPS_FILTER_FLAG_CLEAR_ACTION_RIGHT)
        {
            classifyOut->rights &= ~FWPS_RIGHT_ACTION_WRITE;
        }

        TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_CLASSIFY_INBOUND_IP_PACKET_V6, "Packet was not destined for a device in the mesh; must be destined for the border router");

        return;
    }

    //
    // Step 3
    // Verify the packet is a UDP packet by checking the transport header size
    // (should be 8 bytes)
    //    
    if (transportHeaderSize > 8)
    {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_CLASSIFY_INBOUND_IP_PACKET_V6, "Packet is not a UDP packet, transport header size is %d when it should be 8", transportHeaderSize);

        goto Exit;
    }

    //
    // Step 4
    // Attempt to retrieve a WDFREQUEST from the listen request queue in the
    // device context, then retrieve its output buffer.
    //    

    // Retrieve the output buffer. It should be large enough to hold the MTU of
    // 1280 bytes because the EvtIoControl callback verifies that before even
    // adding the request to the listening queue.      
    WdfSpinLockAcquire(gListenRequestQueueLock);
    status = WdfIoQueueRetrieveNextRequest(gListenRequestQueue,
        &outRequest
    );
    WdfSpinLockRelease(gListenRequestQueueLock);
    if (!NT_SUCCESS(status))
    {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_CLASSIFY_INBOUND_IP_PACKET_V6, "Retrieving request to listen for outbound IPv6 packets failed %!STATUS!", status);
        goto Exit;
    }

    NT_ASSERT(irql == KeGetCurrentIrql());
    requestRetrieved = TRUE;

    // Retrieve the output buffer
    size_t outputBufferLength = 0;
    status = WdfRequestRetrieveOutputBuffer(outRequest,
                                            sizeof(BYTE) * 48,  // Min 48 bytes
                                            (PVOID*)&outputBuffer,
                                            &outputBufferLength
                                            );
    if (!NT_SUCCESS(status))
    {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_CLASSIFY_INBOUND_IP_PACKET_V6, "Retrieving output buffer from WDFREQUEST failed during %!FUNC! with %!STATUS!", status);
        goto Exit;
    }

    //
    // Step 5
    // Verify the NBL is no larger than 1280 bytes (octects), the maximum MTU
    // for Bluetooth. This includes the IP header. 
    //
    // We can acquire the size by going ahead and converting the NBL to the 
    // byte array, as this function reports the size to us.
    //
    // We can then use this size to also verify the request output buffer is
    // big enough in the next step.
    //
    status = IPv6ToBleNBLCopyToBuffer(layerData,
                                      ipHeaderSize,
                                      outputBuffer,
                                      (UINT32*)&outputBufferLength
                                      );
    if (!NT_SUCCESS(status))
    {
        goto Exit;
    }

    if (outputBufferLength > 1280)
    {

        TraceEvents(TRACE_LEVEL_ERROR, TRACE_CLASSIFY_INBOUND_IP_PACKET_V6, "Packet is too large; it must be no larger than 1280 octets for Bluetooth MTU");

        goto Exit;
    }

    bytesTransferred = outputBufferLength;

Exit:

    if (requestRetrieved)
    {
        WdfRequestCompleteWithInformation(outRequest, status, bytesTransferred);
    }

    //
    // Always set these variables upon exiting; the callout must either block
    // or permit.
    //
    classifyOut->actionType = FWP_ACTION_BLOCK;
    classifyOut->rights &= ~FWPS_RIGHT_ACTION_WRITE;
    classifyOut->flags |= FWPS_CLASSIFY_OUT_FLAG_ABSORB;

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_CLASSIFY_INBOUND_IP_PACKET_V6, "%!FUNC! Exit");

    return;
}

_Use_decl_annotations_
VOID
IPv6ToBleCalloutClassifyOutboundIpPacketV6(
    _In_		const FWPS_INCOMING_VALUES0*			inFixedValues,
    _In_		const FWPS_INCOMING_METADATA_VALUES0*	inMetaValues,
    _Inout_opt_	void*									layerData,
    _In_opt_	const void*								classifyContext,
    _In_		const FWPS_FILTER2*						filter,
    _In_		UINT64									flowContext,
    _Inout_		FWPS_CLASSIFY_OUT0*						classifyOut
)
/*++
Routine Description:

    The callback for classifying outbound IPv6 packets at the IP_PACKET
    layer. The filter engine calls the driver's ClassifyFn function whenever
    there is data to be processed by the callout.

    For this driver, it simply sends all outgoing packets to the usermode
    packet processing app if running on a node device. This is based on an 
    assumption that the Pi/IoT devices should NOT be connected to 
    Wi-Fi/Ethernet.
    
    After all, the whole point of the IPv6 over BLE project is to take
    advantage of BLE's energy savings for short messages. Therefore, the TCP/IP
    stack would be a dead end so we're inserting this driver as a bucket to 
    catch outgoing traffic and send it out over BLE.

    Plus, the devices are part of a Bluetooth Mesh, which by nature does not
    permit traffic with devices outside the mesh for security reasons. That is
    why we have to have the gateway device as an arbitrator in the first place.
    Being able to manipulate the mesh devices via Wi-Fi or Ethernet would
    defeat the whole purpose of that concept of an insulated subnet.

    In addition, the only way for traffic to get to the mesh over Bluetooth
    would be to direct it all at the gateway (the destination address). So,
    the sender has to specially insert the mesh device's IPv6 address, derived
    from its UUID, into the first 16 bytes of the payload. That means only an
    app on a remote device that is designed to work with this system would be
    able to send data successfully.

    Note, however, that even if the devices were connected to Wi-Fi or 
    Ethernet, an outbound request that ORIGINATED from the Pi would simply be 
    re-routed over BLE to the Gateway and sent out from there. So, it would 
    still work. Then incoming traffic would just go straight to the Pi.

    On the border router device, this callback behaves like the inbound one -
    it filters based on the white list, then compares to the mesh list to make
    a determination to permit or block.

Procedures for outgoing traffic:

     1. Verify the classifyFn callback has rights to alter the classify and
         didn't inspect the packet itself earlier.
     2. If on the border router device, inspect the destination address to
         verify if the packet is intended for a mesh device. If it is not,
         permit. If it is, continue.
     3. Try to retrieve a WDFREQUEST from the listening queue. If
         unsuccessful, that means the packet processing app is not running
         so the entire solution won't work. Or, the provided output buffer
         wasn't big enough to hold the paket. Block.     
     4. Verify the packet is a UDP datagram packet by examining the size of
         the transport header (UDP is 8 bytes). If it is a TCP segment,
         block. 
     5. Verify the data is no larger than 1280 bytes (octets), the MTU for
         Bluetooth. If it is larger, block. We do this by going ahead and
         converting the packet to the usermode byte array; the conversion
         function takes care of the IP header for us and reports the data
         size.     
     6. Put the converted byte array into the WDFREQUEST output buffer and 
         complete the request.
     7. Block/absorb the original packet.
    

Arguments:

    See https://docs.microsoft.com/windows-hardware/drivers/ddi/content/fwpsk/nc-fwpsk-fwps_callout_classify_fn2
    for parameter descriptions.

Return Value:

    VOID.

--*/
{
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_CLASSIFY_OUTBOUND_IP_PACKET_V6, "%!FUNC! Entry");

    UNREFERENCED_PARAMETER(classifyContext);
    UNREFERENCED_PARAMETER(filter);
    UNREFERENCED_PARAMETER(flowContext);

    NTSTATUS status = STATUS_SUCCESS;    

#if DBG
    KIRQL irql = KeGetCurrentIrql();
#endif // DBG

    
    BYTE* outputBuffer = 0;
    
    WDFREQUEST outRequest = NULL;
    BOOLEAN requestRetrieved = FALSE;   
    ULONG_PTR bytesTransferred = 0;

    UINT32 transportHeaderSize = inMetaValues->transportHeaderSize;

    FWPS_PACKET_INJECTION_STATE packetState;

    //
    // Step 1
    // Verify rights to alter the classify and check if we previously injected
    // the packet. This should never be the case, except possibly for loopbacks
    //
    if ((classifyOut->rights & FWPS_RIGHT_ACTION_WRITE) == 0)
    {
        TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_CLASSIFY_OUTBOUND_IP_PACKET_V6, "No rights to alter the classify during %!FUNC!");

        return;
    }

    NT_ASSERT(layerData);
    _Analysis_assume_(layerData);

    // Check to make sure we don't try to re-inspect packets we inspected
    // earlier; permit and clear the write permissions flag if so
    packetState = FwpsQueryPacketInjectionState0(gInjectionHandleNetwork,
                                                 layerData,
                                                 NULL
                                                 );
    if ((packetState == FWPS_PACKET_INJECTED_BY_SELF) ||
        (packetState == FWPS_PACKET_PREVIOUSLY_INJECTED_BY_SELF))
    {
        classifyOut->actionType = FWP_ACTION_PERMIT;
        if (filter->flags & FWPS_FILTER_FLAG_CLEAR_ACTION_RIGHT)
        {
            classifyOut->rights &= ~FWPS_RIGHT_ACTION_WRITE;
        }

        TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_CLASSIFY_OUTBOUND_IP_PACKET_V6, "Packet was injected by self earlier");

        return;
    }

    // Check for loopback packets and ignore them
    if (inFixedValues)
    {
        FWP_DATA_TYPE valueType = inFixedValues->incomingValue->value.type;
        if (valueType == FWP_UINT32)
        {
            UINT32 flags = inFixedValues->incomingValue->value.uint32;
            if (flags & FWP_CONDITION_FLAG_IS_LOOPBACK)
            {
                classifyOut->actionType = FWP_ACTION_PERMIT;
                if (filter->flags & FWPS_FILTER_FLAG_CLEAR_ACTION_RIGHT)
                {
                    classifyOut->rights &= ~FWPS_RIGHT_ACTION_WRITE;
                }

                TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_CLASSIFY_OUTBOUND_IP_PACKET_V6, "Permitting loopback packet.");

                return;
            }
        }
    }

    //
    // Step 2
    // Verify the destination if on the border router.
    // 
    // If the packet is intended for a mesh device, continue with the rest of
    // the function and pass the packet to the usermode packet processing app.
    //
    // If the packet is not intended for a mesh device, permit it as it must
    // be normal traffic destined elsewhere.
    //

	if (gBorderRouterFlag)
	{
		// Extract the destination address from the IP header by retreating 16
		// bytes (as it is at the end of the IPv6 header)
		UINT8 extractedAddress[16] = { 0 };
		NDIS_STATUS ndisStatus = NDIS_STATUS_SUCCESS;
		ndisStatus = NdisRetreatNetBufferListDataStart(layerData,
													   (sizeof(BYTE) * 16),
													   0,
													   NULL,
													   NULL
													   );
		if (ndisStatus != NDIS_STATUS_SUCCESS)
		{
			classifyOut->actionType = FWP_ACTION_PERMIT;
			if (filter->flags & FWPS_FILTER_FLAG_CLEAR_ACTION_RIGHT)
			{
				classifyOut->rights &= ~FWPS_RIGHT_ACTION_WRITE;
			}

			TraceEvents(TRACE_LEVEL_ERROR, TRACE_CLASSIFY_OUTBOUND_IP_PACKET_V6, "Retreating NBL failed during %!FUNC! with %!STATUS!, permitting packet", status);

			return;
		}

		// Copy the destination address
		RtlCopyMemory(extractedAddress, layerData, IPV6_ADDRESS_LENGTH);

		// Advance back to the end of the IP header if the retreat operation
		// succeeded
		NdisAdvanceNetBufferListDataStart(layerData,
										  (sizeof(BYTE) * 16),
										  FALSE,
										  NULL
										  );

		BOOLEAN isInMeshList = FALSE;

		// Compare each address in the mesh list to the destination address
		PLIST_ENTRY entry = gMeshListHead->Flink;
		while (entry != gMeshListHead)
		{
			PMESH_LIST_ENTRY meshListEntry = CONTAINING_RECORD(entry,
															   MESH_LIST_ENTRY,
															   listEntry
															   );
			// Compare the extracted address to this entry's address
			if (RtlEqualMemory(extractedAddress,
				&meshListEntry->ipv6Address,
				IPV6_ADDRESS_LENGTH))
			{
				// It is in the mesh list!
				isInMeshList = TRUE;
				break;
			}

			// Advance the list
			entry = entry->Flink;
		}

		// Permit the packet if it wasn't in the mesh list, as that means the
		// packet is destined elsewhere (normal traffic)
		if (!isInMeshList)
		{
			classifyOut->actionType = FWP_ACTION_PERMIT;
			if (filter->flags & FWPS_FILTER_FLAG_CLEAR_ACTION_RIGHT)
			{
				classifyOut->rights &= ~FWPS_RIGHT_ACTION_WRITE;
			}

			TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_CLASSIFY_OUTBOUND_IP_PACKET_V6, "Packet was not destined for a device in the mesh; must be destined elsewhere");

			return;
		}
	}        

    //
    // Step 3
    // Verify the packet is a UDP packet by checking the transport header size
    // (should be 8 bytes)
    //    
    if (transportHeaderSize > 8)
    {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_CLASSIFY_OUTBOUND_IP_PACKET_V6, "Packet is not a UDP packet, transport header size is %d when it should be 8", transportHeaderSize);

        goto Exit;
    }   

    //
    // Step 4
    // Attempt to retrieve a WDFREQUEST from the listen request queue in the
    // device context, then retrieve its output buffer.
    //    

    // Retrieve the output buffer. It should be large enough to hold the MTU of
    // 1280 bytes because the EvtIoControl callback verifies that before even
    // adding the request to the listening queue.      
    WdfSpinLockAcquire(gListenRequestQueueLock);
    status = WdfIoQueueRetrieveNextRequest(gListenRequestQueue,
                                           &outRequest
                                           );
    WdfSpinLockRelease(gListenRequestQueueLock);
    if (!NT_SUCCESS(status))
    {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_CLASSIFY_OUTBOUND_IP_PACKET_V6, "Retrieving request to listen for outbound IPv6 packets failed %!STATUS!", status);
        goto Exit;
    }

    NT_ASSERT(irql == KeGetCurrentIrql());
    requestRetrieved = TRUE;

    // Retrieve the output buffer
    size_t outputBufferLength = 0;
    status = WdfRequestRetrieveOutputBuffer(outRequest,
                                            sizeof(BYTE) * 48,  // Min 48 bytes
                                            (PVOID*)&outputBuffer,
                                            &outputBufferLength
                                            );
    if (!NT_SUCCESS(status))
    {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_CLASSIFY_OUTBOUND_IP_PACKET_V6, "Retrieving output buffer from WDFREQUEST failed during %!FUNC! with %!STATUS!", status);
        goto Exit;
    }

    //
    // Step 5
    // Verify the NBL is no larger than 1280 bytes (octects), the maximum MTU
    // for Bluetooth. This includes the IP header. 
    //
    // We can acquire the size by going ahead and converting the NBL to the 
    // byte array, as this function reports the size to us.
    //
    // We can then use this size to also verify the request output buffer is
    // big enough in the next step.
    //
    status = IPv6ToBleNBLCopyToBuffer(layerData,
                                      0, // On outbound IP_PACKET
                                         // layer, NBL is positioned
                                         // at the BEGINNING of the
                                         // IP header. So this is 0.
                                      outputBuffer,
                                      (UINT32*)&outputBufferLength
                                      );
    if (!NT_SUCCESS(status))
    {
        goto Exit;
    }

    if (outputBufferLength > 1280)
    {

        TraceEvents(TRACE_LEVEL_ERROR, TRACE_CLASSIFY_OUTBOUND_IP_PACKET_V6, "Packet is too large; it must be no larger than 1280 octets for Bluetooth MTU");
        
        goto Exit;
    }

    bytesTransferred = outputBufferLength;

Exit:

    if (requestRetrieved)
    {
        WdfRequestCompleteWithInformation(outRequest, status, bytesTransferred);
    }

    //
    // Always set these variables upon exiting; the callout must either block
    // or permit.
    //
    classifyOut->actionType = FWP_ACTION_BLOCK;
    classifyOut->rights &= ~FWPS_RIGHT_ACTION_WRITE;
    classifyOut->flags |= FWPS_CLASSIFY_OUT_FLAG_ABSORB;

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_CLASSIFY_OUTBOUND_IP_PACKET_V6, "%!FUNC! Exit");

    return;
}

_Use_decl_annotations_
NTSTATUS
IPv6ToBleCalloutNotifyIpPacket(
	_In_	FWPS_CALLOUT_NOTIFY_TYPE	notifyType,
	_In_	const GUID*					filterKey,
	_Inout_	const FWPS_FILTER2*			filter
)
/*++
Routine Description:

	A callback routine the filter engine calls if there are events associated
	with the callout.

	For our purposes, there isn't anything to do here. We are not expecting
	anyone else to come along and register or delete a filter that has to do
	with our callout driver; if they do, nothing happens.

Arguments:

	See https://docs.microsoft.com/windows-hardware/drivers/ddi/content/fwpsk/nc-fwpsk-fwps_callout_notify_fn2
	for parameter descriptions.

Return Value:

	STATUS_SUCCESS if the callout accepts the notification from the filter
	engine. Other NTSTATUS error codes as appropriate.

	If the notifyType parameter is FWPS_CALLOUT_NOTIFY_ADD_FILTER, the filter
	will not be added to the filter engine. If the notifyType parameter is
	FWPS_CALLOUT_NOTIFY_DELETE_FILTER, the filter will still be deleted from
	the filter engine.

--*/
{
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_NOTIFY, "%!FUNC! Entry");

	UNREFERENCED_PARAMETER(notifyType);
	UNREFERENCED_PARAMETER(filterKey);
	UNREFERENCED_PARAMETER(filter);

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_NOTIFY, "%!FUNC! Exit");

	return STATUS_SUCCESS;
}

_Use_decl_annotations_
NTSTATUS
IPv6ToBleCalloutsRegister()
/*++
Routine Description:

    Opens the session to the filter engine, then registers the callouts and
    filters. An important note is that the Windows Filtering Platform is
    session-based, so if we ever need to unregister callouts any objects such
    as filters are automatically deleted by the filter engine when we close the
    session. Then all we need to do is unregister the callouts themselves.

    This function is designed to be able to register multiple types of
    callouts, with one function for each. For the initial release of this
    IPv6 To Bluetooth Low Energy project, we only need one.

    ***
    This is where you would call functions to register different kinds of
    callouts after having written a function for each to register that type.
***

Arguments:

    None. Accesses the WDM device object, a global variable.


Return Value:

    STATUS_SUCCESS if the callout driver successfully registers its callout
    and filter. STATUS_UNSUCCESSFUL otherwise.

--*/
{
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_CALLOUT_REGISTRATION, "%!FUNC! Entry");

    NTSTATUS status = STATUS_SUCCESS;

    // Markers for whether the handle to the filter engine is open and if we
    // are in the middle of a transaction with it, and if the callout is
    // registered
    BOOLEAN engineOpened = FALSE;
    BOOLEAN inTransaction = FALSE;

    //
    // Step 1
    // Create a management session to open a handle to the filter engine
    // 

    // Structure for our management session info
    FWPM_SESSION0 session = { 0 };

    // Set flags to dynamic so objects added during the session are
    // automatically deleted, including filters and other objects
    session.flags = FWPM_SESSION_FLAG_DYNAMIC;

    // Create the management session
    status = FwpmEngineOpen0(NULL,
        RPC_C_AUTHN_WINNT,
        NULL,
        &session,
        &gFilterEngineHandle
    );
    if (!NT_SUCCESS(status))
    {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_CALLOUT_REGISTRATION, "Opening the session to the filter engine failed %!STATUS!", status);
        goto Exit;
    }
    engineOpened = TRUE;

    //
    // Step 2
    // Begin the transaction with the filter engine
    //
    status = FwpmTransactionBegin0(gFilterEngineHandle, 0);
    if (!NT_SUCCESS(status))
    {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_CALLOUT_REGISTRATION, "Beginning the transaction with the filter engine failed %!STATUS!", status);
        goto Exit;
    }
    inTransaction = TRUE;

    //
    // Step 3
    // Configure our sublayer info and add it to the engine
    //

    // Set up the sublayer structure
    FWPM_SUBLAYER0 ipv6ToBleSublayer;
    RtlZeroMemory(&ipv6ToBleSublayer, sizeof(FWPM_SUBLAYER0));

    // Set the sublayer info such as its GUID key, human-readable display info,
    // flags, and weight
    ipv6ToBleSublayer.subLayerKey = IPV6_TO_BLE_SUBLAYER;
    ipv6ToBleSublayer.displayData.name = L"IP Packet Sub-Layer";
    ipv6ToBleSublayer.displayData.description =
        L"Sub-Layer for use by the inbound or outbound IP Packet callout";
    ipv6ToBleSublayer.flags = 0;
    ipv6ToBleSublayer.weight = 0; // must be less than the weight of 
                                  // FWPM_SUBLAYER_UNIVERSAL to be
                                  // compatible with Vista's IpSec
                                  // implementation.

    // Add the sublayer
    status = FwpmSubLayerAdd0(gFilterEngineHandle, &ipv6ToBleSublayer, NULL);
    if (!NT_SUCCESS(status))
    {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_CALLOUT_REGISTRATION, "Adding the sublayer for callouts failed %!STATUS!", status);
        goto Exit;
    }

    //
    // Step 4
    // Register the callout for listening at the inbound IP packet v6 layer,
    // and its filter, if on the gateway device. If on the Pi/IoT device (ARM64
    // processor), then register the callout for listening at the outbound IP
    // packet V6 layer.
    //

	if (gBorderRouterFlag) // Register inbound and outbound classify on gateway
	{
		status = IPv6ToBleCalloutRegisterInboundIpPacketV6Callout(
			&FWPM_LAYER_INBOUND_IPPACKET_V6,
			&IPV6_TO_BLE_INBOUND_IP_PACKET_V6,
			&gInboundIpPacketV6CalloutId
		);
		if (!NT_SUCCESS(status))
		{
			goto Exit;
		}

	}

    status = IPv6ToBleCalloutRegisterOutboundIpPacketV6Callout(
        &FWPM_LAYER_OUTBOUND_IPPACKET_V6,
        &IPV6_TO_BLE_OUTBOUND_IP_PACKET_V6,
        &gOutboundIpPacketV6CalloutId
    );
    if (!NT_SUCCESS(status))
    {
        goto Exit;
    }

    //
    // Step 5
    // Commit the transaction to the filter engine
    //
    status = FwpmTransactionCommit0(gFilterEngineHandle);
    if (!NT_SUCCESS(status))
    {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_CALLOUT_REGISTRATION, "Committing the transaction to the filter engine failed %!STATUS!", status);
        goto Exit;
    }
    inTransaction = FALSE;

Exit:

    // Clean up handles if we failed
    if (!NT_SUCCESS(status))
    {
        if (inTransaction)
        {
            FwpmTransactionAbort0(gFilterEngineHandle);
            _Analysis_assume_lock_not_held_(gFilterEngineHandle); // Potential
                                                                 // leak if "FwpmTransactionAbort" fails
        }
        if (engineOpened)
        {
            FwpmEngineClose0(gFilterEngineHandle);
            gFilterEngineHandle = NULL;
        }
    }
    else
    {
        gCalloutsRegistered = TRUE;
    }

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_CALLOUT_REGISTRATION, "%!FUNC! Exit");

    return status;
}

_Use_decl_annotations_
NTSTATUS
IPv6ToBleCalloutRegisterInboundIpPacketV6Callout(
    _In_ const GUID* layerKey,
    _In_ const GUID* calloutKey,
    _Out_ UINT32* calloutId
)
/*++
Routine Description:

    This function registers the callout at the inbound IP_PACKET_V6 layer and
    adds the filter(s).

Arguments:

    layerKey - the GUID identifier for which layer at which we're filtering.

    calloutKey - the GUID identifier for the callout.

    calloutId - the runtime ID for the callout.

Return Value:

    Returns STATUS_SUCCESS if successful. Otherwise, an appropriate NTSTATUS
    error code.

---*/
{
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_CALLOUT_REGISTRATION, "%!FUNC! Entry");

    NTSTATUS status = STATUS_SUCCESS;

    BOOLEAN calloutRegistered = FALSE;

    // Callout structures
    FWPS_CALLOUT2 servicingCallout = { 0 };
    FWPM_CALLOUT0 managementCallout = { 0 };

    // Structure to hold human-readable info about the management callout
    FWPM_DISPLAY_DATA0 displayData = { 0 };

    //
    // Step 1
    // Configure the servicing callout and register it
    //
    servicingCallout.calloutKey = *calloutKey;
    servicingCallout.classifyFn = IPv6ToBleCalloutClassifyInboundIpPacketV6;
    servicingCallout.notifyFn = IPv6ToBleCalloutNotifyIpPacket;

    // Register the callout
    status = FwpsCalloutRegister2(gWdmDeviceObject,
                                  &servicingCallout,
                                  calloutId
                                  );
    if (!NT_SUCCESS(status))
    {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_CALLOUT_REGISTRATION, "Registering servicing callout for inbound IP packet V6 classify failed %!STATUS!", status);
        goto Exit;
    }
    calloutRegistered = TRUE;

    //
    // Step 2
    // Configure the management callout and its display data
    //
    displayData.name = L"Inbound IP Packet V6 Callout";
    displayData.description = L"Callout for listening for inbound IPv6 packets\
		that come from a trusted device and are destined for a BLE device in \
		the mesh network.";

    managementCallout.calloutKey = *calloutKey;
    managementCallout.displayData = displayData;
    managementCallout.applicableLayer = *layerKey;

    // Add the management callout
    status = FwpmCalloutAdd0(gFilterEngineHandle,
                             &managementCallout,
                             NULL,
                             NULL
                             );
    if (!NT_SUCCESS(status))
    {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_CALLOUT_REGISTRATION, "Registering management callout for inbound IP packet V6 classify failed %!STATUS!", status);
        goto Exit;
    }

    //
    // Step 3
    // Add a filter for each entry in the white list
    //

    // Traverse the list and add a filter for each entry
    PLIST_ENTRY entry = gWhiteListHead->Flink;
    while (entry != gWhiteListHead)
    {
        PWHITE_LIST_ENTRY whiteListEntry = CONTAINING_RECORD(entry,
                                                             WHITE_LIST_ENTRY,
                                                             listEntry
                                                             );
        UINT8* remoteAddress = whiteListEntry->ipv6Address.u.Byte;

        // Add the filter
        status = IPv6ToBleCalloutFilterAdd(L"Inbound IPv6 packet filter",
            L"A filter to match packets if source is from the white list. \
            There are as many filters as there are white list entries.",
            remoteAddress,
            INBOUND,
            layerKey,
            calloutKey
        );
        if (!NT_SUCCESS(status))
        {
            break;
        }

        // Advance the list
        entry = entry->Flink;
    }

Exit:

    // Unregister the callout if we failed post-registration
    if (!NT_SUCCESS(status))
    {
        if (calloutRegistered)
        {
            FwpsCalloutUnregisterById0(*calloutId);
            *calloutId = 0;
        }
    }

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_CALLOUT_REGISTRATION, "%!FUNC! Exit");

    return status;
}

_Use_decl_annotations_
NTSTATUS
IPv6ToBleCalloutRegisterOutboundIpPacketV6Callout(
    _In_ const GUID* layerKey,
    _In_ const GUID* calloutKey,
    _Out_ UINT32* calloutId
)
/*++
Routine Description:

    This function registers the callout at the outbound IP_PACKET_V6 layer and
    adds the filter. This callout and its accompanying filter are designed to
    catch all outbound IPv6 UDP traffic on the Pi/IoT device. On the border
    router, it is designed to catch outbound traffic destined for a device
    in the mesh.

Arguments:

    layerKey - the GUID identifier for which layer at which we're filtering.

    calloutKey - the GUID identifier for the callout.

    calloutId - the runtime ID for the callout.

Return Value:

    Returns STATUS_SUCCESS if successful. Otherwise, an appropriate NTSTATUS
    error code.

---*/
{
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_CALLOUT_REGISTRATION, "%!FUNC! Entry");

    NTSTATUS status = STATUS_SUCCESS;

    BOOLEAN calloutRegistered = FALSE;

    // Callout structures
    FWPS_CALLOUT2 servicingCallout = { 0 };
    FWPM_CALLOUT0 managementCallout = { 0 };

    // Structure to hold human-readable info about the management callout
    FWPM_DISPLAY_DATA0 displayData = { 0 };

    //
    // Step 1
    // Configure the servicing callout and register it
    //
    servicingCallout.calloutKey = *calloutKey;
    servicingCallout.classifyFn = IPv6ToBleCalloutClassifyOutboundIpPacketV6;
    servicingCallout.notifyFn = IPv6ToBleCalloutNotifyIpPacket;

    // Register the serviding callout callout
    status = FwpsCalloutRegister2(gWdmDeviceObject,
                                  &servicingCallout,
                                  calloutId
                                  );
    if (!NT_SUCCESS(status))
    {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_CALLOUT_REGISTRATION, "Registering servicing callout for outbound IP packet V6 classify failed %!STATUS!", status);
        goto Exit;
    }
    calloutRegistered = TRUE;

    //
    // Step 2
    // Configure the management callout and its display data
    //
    displayData.name = L"Outbound IP Packet V6 Callout";
    displayData.description = L"Callout that listens for outbound IPv6 packets.";

    managementCallout.calloutKey = *calloutKey;
    managementCallout.displayData = displayData;
    managementCallout.applicableLayer = *layerKey;

    // Add the management callout
    status = FwpmCalloutAdd0(gFilterEngineHandle,
                             &managementCallout,
                             NULL,
                             NULL
                             );
    if (!NT_SUCCESS(status))
    {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_CALLOUT_REGISTRATION, "Registering management callout for outbound IP packet V6 classify failed %!STATUS!", status);
        goto Exit;
    }

    //
    // Step 3
    // Add the filter - one for each entry in the white list if on the border
    // router, or just one for the nodes
    //

	if (gBorderRouterFlag)
	{
		// Traverse the list and add a filter for each entry
		PLIST_ENTRY entry = gMeshListHead->Flink;
		while (entry != gMeshListHead)
		{
			PMESH_LIST_ENTRY meshListEntry = CONTAINING_RECORD(entry,
				MESH_LIST_ENTRY,
				listEntry
			);
			UINT8* destinationAddress = meshListEntry->ipv6Address.u.Byte;

			// Add the filter
			status = IPv6ToBleCalloutFilterAdd(L"Outbound IPv6 packet filter",
				L"A filter to match packets if destination is in mesh list. \
				There are as many filters as there are mesh list entries.",
				destinationAddress,
				OUTBOUND,
				layerKey,
				calloutKey
			);
			if (!NT_SUCCESS(status))
			{
				break;
			}

			// Advance the list
			entry = entry->Flink;
		}
	}
	else
	{
		// On the non-BR devices, catch all outbound traffic
		status = IPv6ToBleCalloutFilterAdd(L"Outbound IPv6 packet filter",
										   L"A filter to match all outbound IPv6 UDP traffic and redirect to the \
										   usermode packet processing app, which sends it out over BLE.",
										   NULL,
										   OUTBOUND,
										   layerKey,
										   calloutKey
										   );
	}

Exit:

    // Unregister the callout if we failed post-registration
    if (!NT_SUCCESS(status))
    {
        if (calloutRegistered)
        {
            FwpsCalloutUnregisterById0(*calloutId);
            *calloutId = 0;
        }
    }

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_CALLOUT_REGISTRATION, "%!FUNC! Exit");

    return status;
}

_Use_decl_annotations_
NTSTATUS
IPv6ToBleCalloutFilterAdd(
    wchar_t *	    filterName,
    wchar_t *	    filterDesc,
    const UINT8*	ipv6Address,
    int             direction,
    const GUID *	layerKey,
    const GUID *	calloutKey
)
/*++
Routine Description:

    Adds a filter to the filter engine.

    For the gateway device, this is called for each filter, of which there are
    as many as there are white list entries.

    For the Pi/IoT device, this is called once. The outbound IP packet classify
    catches all traffic and so only has one filter with no conditions.

Arguments:

    filterName - the name of the filter in human-readable form.

    filterDesc - the description of the filter in human-readable form.

    remoteAddr - the remote address to use in this filter (if applicable).

    layerKey - the GUID for the layer at which we are adding the filter

    calloutKey - the GUID for the callout associated with this filter

Return Value:

    STATUS_SUCCESS if the callout driver successfully registers its filter.
    STATUS_UNSUCCESSFUL otherwise.

--*/
{
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_CALLOUT_REGISTRATION, "%!FUNC! Exit");

    NTSTATUS status = STATUS_SUCCESS;

    // Node devices only filter outbound, so ignore direction flag
    if (!gBorderRouterFlag)
    {
        UNREFERENCED_PARAMETER(direction);
    }

    //
    // Step 1
    // Double-check that the runtime list is not empty, even though that should
    // have been checked before a call to this function
    //
    // Note: This step only applies to the border router device. Node devices
    // don't deal with the white list/mesh list.
    //

	if (gBorderRouterFlag)
	{
		if (direction == INBOUND)
		{
			if (IsListEmpty(gWhiteListHead))
			{
				TraceEvents(TRACE_LEVEL_WARNING, TRACE_CALLOUT_REGISTRATION, "Adding filter for inbound IP packet V6 classify failed because the white list was empty %!STATUS!", status);
				status = STATUS_UNSUCCESSFUL;
				return status;
			}
		}
		else    // OUTBOUND
		{
			if (IsListEmpty(gMeshListHead))
			{
				TraceEvents(TRACE_LEVEL_WARNING, TRACE_CALLOUT_REGISTRATION, "Adding filter for outbound IP packet V6 classify failed because the mesh list was empty %!STATUS!", status);
				status = STATUS_UNSUCCESSFUL;
				return status;
			}
		}
	}

    //
    // Step 2
    // Create the filter structure and set its attributes
    //
    FWPM_FILTER0 filter = { 0 };

    filter.layerKey = *layerKey;
    filter.displayData.name = filterName;
    filter.displayData.description = filterDesc;

    filter.action.type = FWP_ACTION_CALLOUT_TERMINATING; // Invoke a callout 
                                                         // that always returns 
                                                         // block or permit.

    filter.action.calloutKey = *calloutKey;	// This associates the filter with
                                            // the correct callout

    filter.subLayerKey = IPV6_TO_BLE_SUBLAYER;  // Prevents interference with
                                                // other callouts at this layer

    filter.weight.type = FWP_EMPTY;         // auto-weight

    //
    // Step 3
    // Create the filtering condition based on the passed in IPv6 address.
    //
    // Note:
    // This is only for the gateway machine, since the Pi/IoT devices will 
    // have 0 filter conditions and thus will filter all outbound traffic.
    //	

	if (gBorderRouterFlag)
	{
		FWPM_FILTER_CONDITION0 filterCondition = { 0 };

		// This should never be null because this function only would have been
		// called after verifying the runtime list had at least one entry, but  
		// check for good practice. At least, on the border router machine.
		if (ipv6Address)
		{
			if (direction == INBOUND)
			{
				filterCondition.fieldKey = FWPM_CONDITION_IP_REMOTE_ADDRESS;
				filterCondition.matchType = FWP_MATCH_EQUAL;
				filterCondition.conditionValue.type = FWP_BYTE_ARRAY16_TYPE;
				filterCondition.conditionValue.byteArray16 =
					(FWP_BYTE_ARRAY16*)ipv6Address;
			}
			else    // OUTBOUND
			{
				filterCondition.fieldKey = FWPM_CONDITION_IP_DESTINATION_ADDRESS;
				filterCondition.matchType = FWP_MATCH_EQUAL;
				filterCondition.conditionValue.type = FWP_BYTE_ARRAY16_TYPE;
				filterCondition.conditionValue.byteArray16 =
					(FWP_BYTE_ARRAY16*)ipv6Address;
			}
		}

		// Add the filter condition to the filter
		filter.filterCondition = &filterCondition;
	}
	else
	{
        // For the non-border router, this will shunt ALL IPv6 outbound traffic
        // to the callout

        FWPM_FILTER_CONDITION0 filterCondition = { 0 };

        filterCondition.fieldKey = FWPM_CONDITION_IP_DESTINATION_ADDRESS;
        filterCondition.matchType = FWP_MATCH_EQUAL;
        filterCondition.conditionValue.type = FWP_V6_ADDR_MASK;

        // Zeroing out the address and mask ensures that ALL traffic is affected
        FWP_V6_ADDR_AND_MASK localAddressAndMask = { 0 };
        filterCondition.conditionValue.v6AddrMask = &localAddressAndMask;

        // Add the filter condition to the filter
        filter.filterCondition = &filterCondition;
	}

    //
    // Step 4
    // Add the filter
    //
    status = FwpmFilterAdd0(gFilterEngineHandle,
                            &filter,
                            NULL,
                            NULL
                            );
    if (!NT_SUCCESS(status))
    {
        if (ipv6Address)
        {
            TraceEvents(TRACE_LEVEL_ERROR, TRACE_CALLOUT_REGISTRATION, "Adding filter failed during %!FUNC! with %!STATUS!", status);
        }
        else
        {
            TraceEvents(TRACE_LEVEL_ERROR, TRACE_CALLOUT_REGISTRATION, "Adding filter for outbound IPv6 traffic failed %!STATUS!", status);
        }
    }

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_CALLOUT_REGISTRATION, "%!FUNC! Exit");

    return status;
}

_Use_decl_annotations_
VOID
IPv6ToBleCalloutsUnregister()
/*++
Routine Description:

    Unregisters the inbound IPV6 packet callout. This can be called either
    during driver unload or by the functions that add/remove entries from
    the white list and mesh list.

    The functions to add and remove entries from the white list and mesh list
    only call this function if the callouts are currently registered. Driver
    unload means the driver is unloading so the results of this function aren't
    particularly important. Therefore, we don't need to check the NTSTATUS
    that FwpsCalloutUnregisterById0 returns (the samples don't, either).

Arguments:

    None.

Return Value:

    VOID.

---*/
{
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_CALLOUT_REGISTRATION, "%!FUNC! Entry");

    NTSTATUS status = STATUS_SUCCESS;

    //
    // Step 1
    // Close the handle to the filter engine, which removes filters and other
    // objects added during the session since we created a dynamic session
    //
    if (gFilterEngineHandle)
    {
        status = FwpmEngineClose0(gFilterEngineHandle);
        if (!NT_SUCCESS(status))
        {
            TraceEvents(TRACE_LEVEL_ERROR, TRACE_CALLOUT_REGISTRATION, "Closing the filter engine failed %!STATUS!", status);
        }
        gFilterEngineHandle = NULL;
    }

    //
    // Step 2
    // Unregister the callouts. We don't check status here partially because
    // the samples don't, but also because this function must return VOID.
    // Log the error if they do fail.
    //
    if (gCalloutsRegistered)
    {

		if (gBorderRouterFlag)
		{
			status = FwpsCalloutUnregisterById0(gInboundIpPacketV6CalloutId);
			if (!NT_SUCCESS(status))
			{
				TraceEvents(TRACE_LEVEL_ERROR, TRACE_CALLOUT_REGISTRATION, "Unregistering the inbound IPv6 packet callout failed %!STATUS!", status);
			}

		}

        status = FwpsCalloutUnregisterById0(gOutboundIpPacketV6CalloutId);
        if (!NT_SUCCESS(status))
        {
            TraceEvents(TRACE_LEVEL_ERROR, TRACE_CALLOUT_REGISTRATION, "Unregistering the outbound IPv6 packet callout failed %!STATUS!", status);
        }

        gCalloutsRegistered = FALSE;
    }

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_CALLOUT_REGISTRATION, "%!FUNC! Exit");
}
