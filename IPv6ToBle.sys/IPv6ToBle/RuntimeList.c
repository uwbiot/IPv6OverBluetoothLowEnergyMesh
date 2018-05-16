/*++

Module Name:

    RuntimeList.c

Abstract:

    This file contains implementations for runtime list functions.

    This file and its header are only used on the gateway device.

Environment:

    Kernel-mode Driver Framework

--*/

#include "Includes.h"
#include "RuntimeList.tmh"  // auto-generated tracing file

_Use_decl_annotations_
NTSTATUS
IPv6ToBleRuntimeListAssignNewListEntry(
    _In_    WDFREQUEST  Request,
	_In_	ULONG		WhichList
)
/*++
Routine Description:

    Adds an entry to a runtime list.

    This function is called from EvtIoDeviceControl, which can run either at
    IRQL = PASSIVE_LEVEL or DISPATCH_LEVEL.

Arguments:

    Request - the WDFREQUEST object sent from user mode. The desired address is
    supplied in the request's input buffer.
    
    Accesses global variables defined in Driver.h.

Return Value:

    STATUS_SUCCESS if successful; appropriate NTSTATUS error codes otherwise.

--*/
{
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_RUNTIME_LIST, "%!FUNC! Entry");

    NTSTATUS status = STATUS_SUCCESS;

#if DBG
    KIRQL irql = KeGetCurrentIrql();
#endif // DBG

    PVOID inputBuffer;
    size_t receivedSize = 0;

	// Validate input
	if (WhichList != WHITE_LIST && WhichList != MESH_LIST)
	{
		status = STATUS_INVALID_PARAMETER;
		TraceEvents(TRACE_LEVEL_ERROR, TRACE_RUNTIME_LIST, "Invalid list option during %!FUNC! with %!STATUS!", status);
		goto Exit;
	}

    //
    // Step 1
    // Retrieve the address from the request's input buffer. It must be in the
    // form of a string representation of the IPv6 address. Technically, an
    // IPv6 address can be valid with only 3 characters (e.g. ::1) so that is
    // the *minimum* size needed. However, it is expected to be a full address.
    // 
    
    status = WdfRequestRetrieveInputBuffer(Request,
                                           sizeof(WCHAR) * 3,
                                           &inputBuffer,
                                           &receivedSize
                                           );
    if (!NT_SUCCESS(status))
    {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_RUNTIME_LIST, "Retrieving input buffer from WDFREQUEST failed during %!FUNC! with %!STATUS!", status);
        goto Exit;
    }

    // Check for buffer overrun
    if (receivedSize >= INET6_ADDRSTRLEN)
    {
        status = STATUS_BUFFER_OVERFLOW;
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_RUNTIME_LIST, "Input buffer larger than an IPv6 address string during %!FUNC! with %!STATUS!", status);
        goto Exit;
    }

    //
    // Step 2
    // Assign the retrieved string to a UNICODE_STRING structure
    //
    DECLARE_UNICODE_STRING_SIZE(desiredAddress, INET6_ADDRSTRLEN);
    desiredAddress.Buffer = (PWCH)inputBuffer;
    desiredAddress.Length = (USHORT)receivedSize;

    //
    // Step 3
    // Validate the received address and get its 16 byte value form
    //

    // Defensively null-terminate the string
    desiredAddress.Length = min(desiredAddress.Length,
                                desiredAddress.MaximumLength - sizeof(WCHAR)
                                );

    // Suppressing buffer overrun warning because we previously verified the
    // received size and set the length to be *at most* (INET6_ADDRSTRLEN - 1)
#pragma warning(suppress: 6386)
    desiredAddress.Buffer[desiredAddress.Length / sizeof(WCHAR)] = UNICODE_NULL;

    // Convert the string to its network byte order binary form. This function 
    // validates that it is a valid IPv6 address for us.
    IN6_ADDR ipv6AddressStorage = { 0 };
    ULONG scopeId = 0;
    USHORT port = 0;
    status = RtlIpv6StringToAddressExW((PWSTR)desiredAddress.Buffer,
                                       &ipv6AddressStorage,
                                       &scopeId,
                                       &port  // not used, will be 0
                                       );
    if (!NT_SUCCESS(status))
    {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_RUNTIME_LIST, "Converting IPv6 string to address failed during %!FUNC! with %!STATUS!", status);
        goto Exit;
    }

    //
    // Step 4
    // Verify the entry isn't already in the list
    // 
    PLIST_ENTRY entry = WhichList == WHITE_LIST ? gWhiteListHead->Flink : gMeshListHead->Flink;

    NT_ASSERT(entry);

    if (!IsListEmpty(WhichList == WHITE_LIST ? gWhiteListHead : gMeshListHead))
    {
        while (entry != (WhichList == WHITE_LIST ? gWhiteListHead : gMeshListHead))
        {
			// Get the struct that contains this entry

			// This is the closest we can get to dynamic type declaration at
			// runtime
			union {
				PWHITE_LIST_ENTRY whiteListEntry;
				PMESH_LIST_ENTRY meshListEntry;
			} runtimeListEntry;

			if (WhichList == WHITE_LIST)
			{
				runtimeListEntry.whiteListEntry = CONTAINING_RECORD(entry,
																	WHITE_LIST_ENTRY,
																	listEntry
																	);
			}
			else
			{
				runtimeListEntry.meshListEntry = CONTAINING_RECORD(entry,
																   MESH_LIST_ENTRY,
																   listEntry
																   );
			}
            // Compare the memory (byte arrays)
            if (RtlEqualMemory(WhichList == WHITE_LIST ? &runtimeListEntry.whiteListEntry->ipv6Address : &runtimeListEntry.meshListEntry->ipv6Address,
                               &ipv6AddressStorage,
                               sizeof(IN6_ADDR)) &&
                RtlEqualMemory(WhichList == WHITE_LIST ? &runtimeListEntry.whiteListEntry->scopeId : &runtimeListEntry.meshListEntry->scopeId,
                               &scopeId,
                               sizeof(ULONG)))
            {
                // Found it            
                status = STATUS_INVALID_PARAMETER;
                TraceEvents(TRACE_LEVEL_WARNING, TRACE_RUNTIME_LIST, "Entry is already in the white list %!STATUS!", status);
                goto Exit;
            }

            entry = entry->Flink;
        }
    }    

    //
    // Step 4
    // Assuming it is not a duplicate, add the entry to the list
    //
	if (WhichList == WHITE_LIST)
	{
		PWHITE_LIST_ENTRY newWhiteListEntry = (PWHITE_LIST_ENTRY)ExAllocatePoolWithTag(
												NonPagedPoolNx,
												sizeof(WHITE_LIST_ENTRY),
												IPV6_TO_BLE_WHITE_LIST_TAG
											   );
		if (!newWhiteListEntry)
		{
			status = STATUS_INSUFFICIENT_RESOURCES;
			TraceEvents(TRACE_LEVEL_ERROR, TRACE_HELPERS_REGISTRY, "New white list entry allocation failed during %!FUNC! with this error code: %!STATUS!", status);
			goto Exit;
		}

		// Add the entry to the list
		InsertHeadList(gWhiteListHead, &newWhiteListEntry->listEntry);

		// Insert the address into the entry
		newWhiteListEntry->ipv6Address = ipv6AddressStorage;
		newWhiteListEntry->scopeId = scopeId;
	}
	else
	{
		PMESH_LIST_ENTRY newMeshListEntry = (PMESH_LIST_ENTRY)ExAllocatePoolWithTag(
												NonPagedPoolNx,
												sizeof(MESH_LIST_ENTRY),
												IPV6_TO_BLE_MESH_LIST_TAG
											);
		if (!newMeshListEntry)
		{
			status = STATUS_INSUFFICIENT_RESOURCES;
			TraceEvents(TRACE_LEVEL_ERROR, TRACE_HELPERS_REGISTRY, "New mesh list entry allocation failed during %!FUNC! with this error code: %!STATUS!", status);
			goto Exit;
		}

		// Add the entry to the list
		InsertHeadList(gMeshListHead, &newMeshListEntry->listEntry);

		// Insert the address into the entry
		newMeshListEntry->ipv6Address = ipv6AddressStorage;
		newMeshListEntry->scopeId = scopeId;
	}

    //
    // Step 5
    // Update the boolean that the list has been modified
    //
	if (WhichList == WHITE_LIST)
	{
		WdfSpinLockAcquire(gWhiteListModifiedLock);
		gWhiteListModified = TRUE;
		WdfSpinLockRelease(gWhiteListModifiedLock);
	}
	else
	{
		WdfSpinLockAcquire(gMeshListModifiedLock);
		gMeshListModified = TRUE;
		WdfSpinLockRelease(gMeshListModifiedLock);
	}    

    NT_ASSERT(irql == KeGetCurrentIrql());

    //
    // Step 6
    // Because the white list has had an entry successfully added, and the
    // callout filters are based on the white list, tear down and rebuild the
    // callouts IF the mesh list is also not empty. If we just added an entry
    // to the white list but there is nothing in the mesh list, we can't
    // perform listening callout operations.
    //
    // Also, we have to check if the callouts were registered. If they were
    // not and we now have at least one entry in both the white list and the
    // mesh list, then we have to register the callouts but not try to
    // unregister them first. If the callouts were already registered, i.e. 
    // there was already at least one entry in each list and we just added 
    // another one, then tear down and rebuild.
    //
    // Note: we shouldn't have to worry about synchronizing access to the list
    // heads because this and other list modification functions are called from
    // EvtIoDeviceControl, which is guaranteed to be synchronized by the
    // framework. Nothing else in this driver should ever come along at
    // IRQL > PASSIVE_LEVEL that nees to check whether the callouts are
    // registered.
    //
    if ((WhichList == WHITE_LIST && !IsListEmpty(gMeshListHead)) ||
		(WhichList == MESH_LIST && !IsListEmpty(gWhiteListHead)))
    {
        if (gCalloutsRegistered)
        {
            IPv6ToBleCalloutsUnregister();
            status = IPv6ToBleCalloutsRegister();
            if (!NT_SUCCESS(status))
            {
                TraceEvents(TRACE_LEVEL_ERROR, TRACE_RUNTIME_LIST, "Registering callouts during %!FUNC! failed with %!STATUS!", status);
            }
        }
        else
        {
            status = IPv6ToBleCalloutsRegister();
            if (!NT_SUCCESS(status))
            {
                TraceEvents(TRACE_LEVEL_ERROR, TRACE_RUNTIME_LIST, "Registering callouts during %!FUNC! failed with %!STATUS!", status);
            }
        }
    }

Exit:

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_RUNTIME_LIST, "%!FUNC! Exit");
    return status;
}

_Use_decl_annotations_
NTSTATUS
IPv6ToBleRuntimeListRemoveListEntry(
    _In_    WDFREQUEST  Request,
	_In_	ULONG		WhichList
)
/*++
Routine Description:

    Removes an entry from a runtime list.

    This function is called from EvtIoDeviceControl, which can run either at
    IRQL = PASSIVE_LEVEL or DISPATCH_LEVEL.

Arguments:

    Request - the WDFREQUEST object sent from user mode. The desired address is
    supplied in the request's input buffer.

	WhichList - the desired list from which to remove an entry.
    
    Accesses global variables defined in Driver.h.

Return Value:

    STATUS_SUCCESS if successful; appropriate NTSTATUS error codes otherwise.

--*/
{
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_RUNTIME_LIST, "%!FUNC! Entry");

    NTSTATUS status = STATUS_SUCCESS;
    BOOLEAN isInList = FALSE;
    BOOLEAN parametersKeyOpened = FALSE;

    PVOID inputBuffer;
    size_t receivedSize = 0;

	// Validate input
	if (WhichList != WHITE_LIST && WhichList != MESH_LIST)
	{
		status = STATUS_INVALID_PARAMETER;
		TraceEvents(TRACE_LEVEL_ERROR, TRACE_RUNTIME_LIST, "Invalid list option during %!FUNC! with %!STATUS!", status);
		goto Exit;
	}

    //
    // Step 1
    // Check for empty list
    //
    if (IsListEmpty(WhichList == WHITE_LIST ? gWhiteListHead : gMeshListHead))
    {
        status = STATUS_INVALID_PARAMETER;
        TraceEvents(TRACE_LEVEL_WARNING, TRACE_RUNTIME_LIST, "List was empty, error code: %!STATUS!", status);
        goto Exit;
    }

    //
    // Step 2
    // Retrieve the address from the request's input buffer. It must be in the
    // form of a string representation of the IPv6 address. Technically, an
    // IPv6 address can be valid with only 3 characters (e.g. ::1) so that is
    // the *minimum* size needed. However, it is expected to be a full address.
    // 
    status = WdfRequestRetrieveInputBuffer(Request,
                                           sizeof(WCHAR) * 3,
                                           &inputBuffer,
                                           &receivedSize
                                           );
    if (!NT_SUCCESS(status))
    {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_RUNTIME_LIST, "Retrieving input buffer from WDFREQUEST failed during %!FUNC! with %!STATUS!", status);
        goto Exit;
    }
    
    // Check for buffer overrun
    if (receivedSize >= INET6_ADDRSTRLEN)
    {
        status = STATUS_BUFFER_OVERFLOW;
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_RUNTIME_LIST, "Input buffer larger than an IPv6 address string during %!FUNC! with %!STATUS!", status);
        goto Exit;
    }

    //
    // Step 3
    // Assign the retrieved string to a UNICODE_STRING structure
    //
    DECLARE_UNICODE_STRING_SIZE(desiredAddress, INET6_ADDRSTRLEN);
    desiredAddress.Buffer = (PWCH)inputBuffer;
    desiredAddress.Length = (USHORT)receivedSize;

    //
    // Step 4
    // Validate the received address and get its 16 byte value form
    //

    // Defensively null-terminate the string
    desiredAddress.Length = min(desiredAddress.Length, 
                                desiredAddress.MaximumLength - sizeof(WCHAR)
                                );

    // Suppressing buffer overrun warning because we previously verified the
    // received size and set the length to be *at most* (INET6_ADDRSTRLEN - 1)
#pragma warning(suppress: 6386)
    desiredAddress.Buffer[desiredAddress.Length / sizeof(WCHAR)] = UNICODE_NULL;
    
    // Convert the string to its network byte order binary form. This function 
    // validates that it is a valid IPv6 address for us.
    IN6_ADDR ipv6AddressStorage = { 0 };
    ULONG scopeId = 0;
    USHORT port = 0;
    status = RtlIpv6StringToAddressExW((PWSTR)desiredAddress.Buffer,
                                        &ipv6AddressStorage,
                                        &scopeId,
                                        &port  // not used, will be 0
                                        );
    if (!NT_SUCCESS(status))
    {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_RUNTIME_LIST, "Converting IPv6 string to address failed during %!FUNC! with %!STATUS!", status);
        goto Exit;
    }

    //
    // Step 5
    // Traverse the list and remove the entry if we find it
    //
    PLIST_ENTRY entry = WhichList == WHITE_LIST ? gWhiteListHead->Flink : gMeshListHead->Flink;

    NT_ASSERT(entry);

    while (entry != (WhichList == WHITE_LIST ? gWhiteListHead : gMeshListHead))
    {
		// Get the struct that contains this entry

		// This is the closest we can get to dynamic type declaration at
		// runtime
		union {
			PWHITE_LIST_ENTRY whiteListEntry;
			PMESH_LIST_ENTRY meshListEntry;
		} runtimeListEntry;

		if (WhichList == WHITE_LIST)
		{
			runtimeListEntry.whiteListEntry = CONTAINING_RECORD(entry,
																WHITE_LIST_ENTRY,
																listEntry
																);
		}
		else
		{
			runtimeListEntry.meshListEntry = CONTAINING_RECORD(entry,
															   MESH_LIST_ENTRY,
															   listEntry
															   );
		}
        // Compare the memory (byte arrays)        
        if (RtlEqualMemory(WhichList == WHITE_LIST ? &runtimeListEntry.whiteListEntry->ipv6Address : &runtimeListEntry.meshListEntry->ipv6Address,
                           &ipv6AddressStorage,
                           sizeof(IN6_ADDR)) &&
            RtlEqualMemory(WhichList == WHITE_LIST ? &runtimeListEntry.whiteListEntry->scopeId : &runtimeListEntry.meshListEntry->scopeId,
                           &scopeId,
                           sizeof(ULONG)))
        {
            // Found it, now remove it
            isInList = TRUE;
            BOOLEAN removed = RemoveEntryList(entry);
            if (!removed)
            {
                status = STATUS_UNSUCCESSFUL;
                TraceEvents(TRACE_LEVEL_ERROR, TRACE_RUNTIME_LIST, "Removing white list entry from the list failed %!STATUS!", status);
                goto Exit;
            }

            // Free the memory (should be valid if we got to this point)
			if (WhichList == WHITE_LIST)
			{
				ExFreePoolWithTag(runtimeListEntry.whiteListEntry,
								  IPV6_TO_BLE_WHITE_LIST_TAG
								  );
				runtimeListEntry.whiteListEntry = 0;
			}
			else
			{
				ExFreePoolWithTag(runtimeListEntry.meshListEntry,
								  IPV6_TO_BLE_MESH_LIST_TAG
								  );
				runtimeListEntry.meshListEntry = 0;
			}

            break;
        }

        entry = entry->Flink;
    }  

    //
    // Step 6
    // Exit if we didn't find the entry, otherwise mark that we modified the
    // list by acquiring the appropriate spinlock
    //
    if (!isInList)
    {
        status = STATUS_INVALID_PARAMETER;
        TraceEvents(TRACE_LEVEL_WARNING, TRACE_RUNTIME_LIST, "Could not find requested entry in the list %!STATUS!", status);
        goto Exit;
    }
    else
    {
		if (WhichList == WHITE_LIST)
		{
			WdfSpinLockAcquire(gWhiteListModifiedLock);
			gWhiteListModified = TRUE;
			WdfSpinLockRelease(gWhiteListModifiedLock);
		}
		else
		{
			WdfSpinLockAcquire(gMeshListModifiedLock);
			gMeshListModified = TRUE;
			WdfSpinLockRelease(gMeshListModifiedLock);
		}
    }

    //
    // Step 7
    // If the list is *now* empty and the callouts were registered,
    // unregister the callouts. Doesn't matter about the other list.
    //
    if ((WhichList == WHITE_LIST && IsListEmpty(gWhiteListHead)) ||
		(WhichList == MESH_LIST && IsListEmpty(gMeshListHead)))
    {
        // Unregister the callouts
        if (gCalloutsRegistered)
        {
            IPv6ToBleCalloutsUnregister();
        }

        // Remove the list registry key since the list is now empty
        status = IPv6ToBleRegistryOpenParametersKey();
        if (!NT_SUCCESS(status))
        {
            goto Exit;
        }
        parametersKeyOpened = TRUE;

		if (WhichList == WHITE_LIST)
		{
			status = IPv6ToBleRegistryOpenWhiteListKey();
			if (!NT_SUCCESS(status))
			{
				goto Exit;
			}
			status = WdfRegistryRemoveKey(gWhiteListKey);
			if (!NT_SUCCESS(status))
			{
				TraceEvents(TRACE_LEVEL_ERROR, TRACE_RUNTIME_LIST, "Removing white list key failed during %!FUNC!, status: %!STATUS!", status);
			}
		}
		else
		{
			status = IPv6ToBleRegistryOpenMeshListKey();
			if (!NT_SUCCESS(status))
			{
				goto Exit;
			}
			status = WdfRegistryRemoveKey(gMeshListKey);
			if (!NT_SUCCESS(status))
			{
				TraceEvents(TRACE_LEVEL_ERROR, TRACE_RUNTIME_LIST, "Removing mesh list key failed during %!FUNC!, status: %!STATUS!", status);
			}
		}
        
    }

Exit:

    // Close the parent key if we opened it to remove the child list key
    if (parametersKeyOpened)
    {
        WdfRegistryClose(gParametersKey);
    }

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_RUNTIME_LIST, "%!FUNC! Exit");
    return status;
}

_Use_decl_annotations_
VOID
IPv6ToBleRuntimeListPurgeRuntimeList(
	_In_ ULONG WhichList
)
/*++
Routine Description:

    Cleans up the runtime linked list of white list entries, if possible.

    This function is called from the device cleanup callback, which is called
    at IRQL = PASSIVE_LEVEL.

Arguments:

    None.

Return Value:

    None.

--*/
{
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_RUNTIME_LIST, "%!FUNC! Entry");

	// Validate input
	if (WhichList != WHITE_LIST && WhichList != MESH_LIST)
	{
		TraceEvents(TRACE_LEVEL_ERROR, TRACE_RUNTIME_LIST, "Invalid list option during %!FUNC!");
		return;
	}

    // Check for empty list
    if ((WhichList == WHITE_LIST && IsListEmpty(gWhiteListHead)) ||
		(WhichList == MESH_LIST && IsListEmpty(gMeshListHead)))
    {
        TraceEvents(TRACE_LEVEL_WARNING, TRACE_RUNTIME_LIST, "%s List is empty; nothing to purge.", WhichList == WHITE_LIST ? "White" : "Mesh");
        return;
    }

    // Clean up the linked list
	if (WhichList == WHITE_LIST)
	{
		while (!IsListEmpty(gWhiteListHead))
		{
			PLIST_ENTRY entry = RemoveHeadList(gWhiteListHead);   // remove from list
			PWHITE_LIST_ENTRY whiteListEntry = CONTAINING_RECORD(entry,
																 WHITE_LIST_ENTRY,
																 listEntry
																 );
			entry = 0;
			ExFreePoolWithTag(whiteListEntry, IPV6_TO_BLE_WHITE_LIST_TAG); // free white list entry memory
			whiteListEntry = 0;
		}
	}
	else
	{
		while (!IsListEmpty(gMeshListHead))
		{
			PLIST_ENTRY entry = RemoveHeadList(gMeshListHead);   // remove from list
			PMESH_LIST_ENTRY meshListEntry = CONTAINING_RECORD(entry,
															   MESH_LIST_ENTRY,
															   listEntry
															   );
			entry = 0;
			ExFreePoolWithTag(meshListEntry, IPV6_TO_BLE_MESH_LIST_TAG); // free mesh list entry memory
			meshListEntry = 0;
		}
	}    

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_RUNTIME_LIST, "%!FUNC! Exit");
}