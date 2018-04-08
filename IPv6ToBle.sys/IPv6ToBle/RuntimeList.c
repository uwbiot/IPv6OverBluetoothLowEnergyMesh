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
IPv6ToBleRuntimeListAssignWhiteListEntry(
    _In_    WDFREQUEST  Request
)
/*++
Routine Description:

    Adds an entry to the runtime white list.

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

    //
    // Step 1
    // Retrieve the address from the request's input buffer. It must be in the
    // form of a string representation of the IPv6 address. Technically, an
    // IPv6 address can be valid with only 3 characters (e.g. ::1) so that is
    // the *minimum* size needed. However, it is expected to be a full address.
    // 
    DECLARE_UNICODE_STRING_SIZE(desiredAddress, INET6_ADDRSTRLEN);
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

    desiredAddress.Buffer = (PWCH)inputBuffer;
    desiredAddress.Length = (USHORT)receivedSize;
    desiredAddress.MaximumLength = (USHORT)receivedSize + 2;

    //
    // Step 2
    // Validate the received address and get its 16 byte value form
    //

    // Defensively null-terminate the string
    PWSTR terminator;
    IN6_ADDR ipv6AddressStorage;

    desiredAddress.Length = min(desiredAddress.Length,
                                desiredAddress.MaximumLength - sizeof(WCHAR)
                                );
    desiredAddress.Buffer[desiredAddress.Length / sizeof(WCHAR)] = UNICODE_NULL;

    // Convert the string. This function validates that it is a valid IPv6
    // address for us.
    status = RtlIpv6StringToAddressW(desiredAddress.Buffer,
                                     &terminator,
                                     &ipv6AddressStorage
                                     );
    if (!NT_SUCCESS(status) || terminator != UNICODE_NULL)
    {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_RUNTIME_LIST, "Converting IPv6 string to address failed during %!FUNC! with %!STATUS!", status);
        goto Exit;
    }

    //
    // Step 3
    // Verify the entry isn't already in the list
    // 
    PLIST_ENTRY entry = gWhiteListHead->Flink;

    NT_ASSERT(entry);

    if (!IsListEmpty(gWhiteListHead))
    {
        while (entry != gWhiteListHead)
        {
            PWHITE_LIST_ENTRY whiteListEntry = CONTAINING_RECORD(entry,
                                                                WHITE_LIST_ENTRY,
                                                                listEntry
                                                                );
            // Compare the memory (byte arrays)
            if (RtlCompareMemory(&whiteListEntry->ipv6Address,
                                 &ipv6AddressStorage.u.Byte[0],
                                 IPV6_ADDRESS_LENGTH))
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
    PWHITE_LIST_ENTRY newWhiteListEntry = (PWHITE_LIST_ENTRY)ExAllocatePoolWithTag(
                                                NonPagedPoolNx,
                                                sizeof(WHITE_LIST_ENTRY),
                                                IPV6_TO_BLE_WHITE_LIST_TAG
                                            );
    if (!newWhiteListEntry)
    {
        status = STATUS_INSUFFICIENT_RESOURCES;
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_RUNTIME_LIST, "New white list entry allocation failed %!STATUS!", status);
        goto Exit;
    }

    // Add the entry to the list
    InsertHeadList(gWhiteListHead, &newWhiteListEntry->listEntry);

    // Insert the address into the entry
    newWhiteListEntry->ipv6Address = (UINT8*)(&ipv6AddressStorage.u.Byte[0]);

    //
    // Step 5
    // Update the boolean that the list has been modified
    //
    WdfSpinLockAcquire(gWhiteListModifiedLock);
    gWhiteListModified = TRUE;
    WdfSpinLockRelease(gWhiteListModifiedLock);

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
    if (!IsListEmpty(gMeshListHead))
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
IPv6ToBleRuntimeListAssignMeshListEntry(
    _In_    WDFREQUEST  Request
)
/*++
Routine Description:

    Adds an entry to the runtime mesh list.

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

    PVOID inputBuffer = 0;
    size_t receivedSize = 0;

    //
    // Step 1
    // Retrieve the address from the request's input buffer. It must be in the
    // form of a string representation of the IPv6 address. Technically, an
    // IPv6 address can be valid with only 3 characters (e.g. ::1) so that is
    // the *minimum* size needed. However, it is expected to be a full address.
    // 
    DECLARE_UNICODE_STRING_SIZE(desiredAddress, INET6_ADDRSTRLEN);
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
    
    desiredAddress.Buffer = (PWCH)inputBuffer;
    desiredAddress.Length = (USHORT)receivedSize;
    desiredAddress.MaximumLength = (USHORT)receivedSize + 2;

    //
    // Step 3
    // Validate the received address and get its 16 byte value form
    //

    // Defensively null-terminate the string
    PWSTR terminator;
    IN6_ADDR ipv6AddressStorage;

    desiredAddress.Length = min(desiredAddress.Length,
                                desiredAddress.MaximumLength - sizeof(WCHAR)
                                );
    desiredAddress.Buffer[desiredAddress.Length / sizeof(WCHAR)] = UNICODE_NULL;

    status = RtlIpv6StringToAddressW(desiredAddress.Buffer,
                                     &terminator,
                                     &ipv6AddressStorage
                                     );
    if (!NT_SUCCESS(status) || terminator != UNICODE_NULL)
    {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_RUNTIME_LIST, "Converting IPv6 string to address failed during %!FUNC! with %!STATUS!", status);
        goto Exit;
    }

    //
    // Step 4
    // Verify the entry isn't already in the list
    // 
    PLIST_ENTRY meshListEntry = gMeshListHead->Flink;

    NT_ASSERT(meshListEntry);
    while (meshListEntry != gMeshListHead)
    {
        PMESH_LIST_ENTRY entry = CONTAINING_RECORD(meshListEntry,
                                                   MESH_LIST_ENTRY,
                                                   listEntry
                                                   );
        // Compare the memory (byte arrays)        
        if (RtlCompareMemory(&entry->ipv6Address,
                             &ipv6AddressStorage.u.Byte[0],
                             IPV6_ADDRESS_LENGTH))
        {
            // Found it
            status = STATUS_INVALID_PARAMETER;
            TraceEvents(TRACE_LEVEL_WARNING, TRACE_RUNTIME_LIST, "Entry is already in the mesh list %!STATUS!", status);
            goto Exit;
        }

        meshListEntry = meshListEntry->Flink;
    }

    //
    // Step 5
    // Assuming it is not a duplicate, add the entry to the list
    //
    // Using non-paged pool because the list may be accessed at
    // IRQL = DISPATCH_LEVEL
    PMESH_LIST_ENTRY newMeshListEntry = (PMESH_LIST_ENTRY)ExAllocatePoolWithTag(
                                            NonPagedPoolNx,
                                            sizeof(MESH_LIST_ENTRY),
                                            IPV6_TO_BLE_MESH_LIST_TAG
                                        );
    if (!newMeshListEntry)
    {
        status = STATUS_INSUFFICIENT_RESOURCES;
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_RUNTIME_LIST, "New mesh list entry allocation failed %!STATUS!", status);
        goto Exit;
    }

    // Add the entry to the list
    InsertHeadList(gMeshListHead, &newMeshListEntry->listEntry);

    // Insert the address into the entry
    newMeshListEntry->ipv6Address = (UINT8*)(ipv6AddressStorage.u.Byte[0]);

    //
    // Step 6
    // Update the boolean that the list has been modified
    //
    WdfSpinLockAcquire(gMeshListModifiedLock);
    gMeshListModified = TRUE;
    WdfSpinLockRelease(gMeshListModifiedLock);

    NT_ASSERT(irql == KeGetCurrentIrql());

    //
    // Step 7
    // Because the mesh list has had an entry successfully added, and the
    // callout classifyFn is based on the mesh list, tear down and rebuild the
    // callouts IF the white list is also not empty. If we just added an entry
    // to the mesh list but there is nothing in the white list, we can't
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
    // framework.
    //
    if (!IsListEmpty(gWhiteListHead))
    {
        if (gCalloutsRegistered) 
        {
            IPv6ToBleCalloutsUnregister();
            status = IPv6ToBleCalloutsRegister();
            if (NT_SUCCESS(status))
            {
                TraceEvents(TRACE_LEVEL_ERROR, TRACE_RUNTIME_LIST, "Registering callouts during %!FUNC! failed with %!STATUS!", status);
            }
        }
        else
        {
            status = IPv6ToBleCalloutsRegister();
            if (NT_SUCCESS(status))
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
IPv6ToBleRuntimeListRemoveWhiteListEntry(
    _In_    WDFREQUEST  Request
)
/*++
Routine Description:

    Removes an entry from the runtime white list.

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
    BOOLEAN isInList = FALSE;
    BOOLEAN parametersKeyOpened = FALSE;

    PVOID inputBuffer;
    size_t receivedSize = 0;

    //
    // Step 1
    // Check for empty list
    //
    if (IsListEmpty(gWhiteListHead))
    {
        status = STATUS_INVALID_PARAMETER;
        TraceEvents(TRACE_LEVEL_WARNING, TRACE_RUNTIME_LIST, "White list was empty, error code: %!STATUS!", status);
        goto Exit;
    }

    //
    // Step 2
    // Retrieve the address from the request's input buffer. It must be in the
    // form of a string representation of the IPv6 address. Technically, an
    // IPv6 address can be valid with only 3 characters (e.g. ::1) so that is
    // the *minimum* size needed. However, it is expected to be a full address.
    // 
    DECLARE_UNICODE_STRING_SIZE(desiredAddress, INET6_ADDRSTRLEN);
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
    
    desiredAddress.Buffer = (PWCH)inputBuffer;
    desiredAddress.Length = (USHORT)receivedSize;
    desiredAddress.MaximumLength = (USHORT)receivedSize + 2;

    //
    // Step 3
    // Validate the received address and get its 16 byte value form
    //

    // Defensively null-terminate the string
    PWSTR terminator;
    IN6_ADDR ipv6AddressStorage;

    desiredAddress.Length = min(desiredAddress.Length, 
                                desiredAddress.MaximumLength - sizeof(WCHAR)
                                );
    desiredAddress.Buffer[desiredAddress.Length / sizeof(WCHAR)] = UNICODE_NULL;

    status = RtlIpv6StringToAddressW(desiredAddress.Buffer,
                                     &terminator,
                                     &ipv6AddressStorage
                                     );
    if (!NT_SUCCESS(status) || terminator != UNICODE_NULL)
    {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_RUNTIME_LIST, "Converting IPv6 string to address failed during %!FUNC! with %!STATUS!", status);
        goto Exit;
    }

    //
    // Step 4
    // Traverse the list and remove the entry if we find it
    //
    PLIST_ENTRY entry = gWhiteListHead->Flink;

    NT_ASSERT(entry);

    while (entry != gWhiteListHead)
    {
        PWHITE_LIST_ENTRY whiteListEntry = CONTAINING_RECORD(entry,
                                                            WHITE_LIST_ENTRY,
                                                            listEntry
                                                            );
        // Compare the memory (byte arrays)        
        if (RtlCompareMemory(&whiteListEntry->ipv6Address,
                             &ipv6AddressStorage.u.Byte[0],
                             IPV6_ADDRESS_LENGTH))
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
            ExFreePoolWithTag(whiteListEntry, IPV6_TO_BLE_WHITE_LIST_TAG);
            whiteListEntry = 0;
            break;
        }

        entry = entry->Flink;
    }  

    //
    // Step 5
    // Exit if we didn't find the entry, otherwise mark that we modified the
    // list by acquiring the appropriate spinlock
    //
    if (!isInList)
    {
        status = STATUS_INVALID_PARAMETER;
        TraceEvents(TRACE_LEVEL_WARNING, TRACE_RUNTIME_LIST, "Could not find requested entry in the white list %!STATUS!", status);
        goto Exit;
    }
    else
    {
        WdfSpinLockAcquire(gWhiteListModifiedLock);
        gWhiteListModified = TRUE;
        WdfSpinLockRelease(gWhiteListModifiedLock);
    }

    //
    // Step 6
    // If the white list is *now* empty and the callouts were registered,
    // unregister the callouts. Doesn't matter about the mesh list.
    //
    if (IsListEmpty(gWhiteListHead))
    {
        // Unregister the callouts
        if (gCalloutsRegistered)
        {
            IPv6ToBleCalloutsUnregister();
        }

        // Remove the white list registry key since the list is now empty
        status = IPv6ToBleRegistryOpenParametersKey();
        if (!NT_SUCCESS(status))
        {
            goto Exit;
        }
        parametersKeyOpened = TRUE;

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
NTSTATUS
IPv6ToBleRuntimeListRemoveMeshListEntry(
    _In_    WDFREQUEST  Request
)
/*++
Routine Description:

    Removes an entry from the runtime mesh list.

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
    BOOLEAN isInList = FALSE;
    BOOLEAN parametersKeyOpened = FALSE;

    PVOID inputBuffer;
    size_t receivedSize = 0;

    //
    // Step 1
    // Check for empty list
    //
    if (IsListEmpty(gMeshListHead))
    {
        status = STATUS_INVALID_PARAMETER;
        TraceEvents(TRACE_LEVEL_WARNING, TRACE_RUNTIME_LIST, "Mesh list was empty, error code: %!STATUS!", status);
        goto Exit;
    }

    //
    // Step 2
    // Retrieve the address from the request's input buffer. It must be in the
    // form of a string representation of the IPv6 address. Technically, an
    // IPv6 address can be valid with only 3 characters (e.g. ::1) so that is
    // the *minimum* size needed. However, it is expected to be a full address.
    // 
    DECLARE_UNICODE_STRING_SIZE(desiredAddress, INET6_ADDRSTRLEN);
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
    
    desiredAddress.Buffer = (PWCH)inputBuffer;
    desiredAddress.Length = (USHORT)receivedSize;
    desiredAddress.MaximumLength = (USHORT)receivedSize + 2;

    //
    // Step 3
    // Validate the received address and get its 16 byte value form
    //

    // Defensively null-terminate the string
    PWSTR terminator;
    IN6_ADDR ipv6AddressStorage;

    desiredAddress.Length = min(desiredAddress.Length,
                                desiredAddress.MaximumLength - sizeof(WCHAR)
                                );
    desiredAddress.Buffer[desiredAddress.Length / sizeof(WCHAR)] = UNICODE_NULL;

    status = RtlIpv6StringToAddressW(desiredAddress.Buffer,
                                     &terminator,
                                     &ipv6AddressStorage
                                     );
    if (!NT_SUCCESS(status)  || terminator != UNICODE_NULL)
    {
        status = STATUS_INVALID_PARAMETER;
        goto Exit;
    }

    //
    // Step 4
    // Traverse the list and remove the entry if we find it
    //
    PLIST_ENTRY entry = gMeshListHead->Flink;

    NT_ASSERT(entry);

    while (entry != gMeshListHead)
    {
        PMESH_LIST_ENTRY meshListEntry = CONTAINING_RECORD(entry,
                                                           MESH_LIST_ENTRY,
                                                           listEntry
                                                           );
        // Compare the memory (byte arrays)        
        if (RtlCompareMemory(meshListEntry->ipv6Address,
                            &ipv6AddressStorage.u.Byte[0],
                            IPV6_ADDRESS_LENGTH))
        {
            // Found it, now remove it
            isInList = TRUE;
            BOOLEAN removed = RemoveEntryList(entry);
            if (!removed)
            {
                status = STATUS_UNSUCCESSFUL;
                TraceEvents(TRACE_LEVEL_ERROR, TRACE_RUNTIME_LIST, "Removing mesh list entry from the list failed %!STATUS!", status);
                goto Exit;
            }
            ExFreePoolWithTag(meshListEntry, IPV6_TO_BLE_WHITE_LIST_TAG);
            meshListEntry = 0;
            break;
        }

        entry = entry->Flink;
    }

    //
    // Step 5
    // Exit if we didn't find the entry, otherwise mark that we modified the
    // list by acquiring the appropriate spinlock
    //
    if (!isInList)
    {
        status = STATUS_INVALID_PARAMETER;
        TraceEvents(TRACE_LEVEL_WARNING, TRACE_RUNTIME_LIST, "Could not find requested entry in the mesh list %!STATUS!", status);
        goto Exit;
    }
    else
    {
        WdfSpinLockAcquire(gMeshListModifiedLock);
        gMeshListModified = TRUE;
        WdfSpinLockRelease(gMeshListModifiedLock);
    }

    //
    // Step 6
    // If the mesh list is *now* empty and the callouts were registered,
    // unregister the callouts. Doesn't matter about the mesh list.
    //
    if (IsListEmpty(gMeshListHead))
    {
        // Unregister the callouts
        if (gCalloutsRegistered)
        {
            IPv6ToBleCalloutsUnregister();
        }

        // Remove the white list registry key since the list is now empty
        status = IPv6ToBleRegistryOpenParametersKey();
        if (!NT_SUCCESS(status))
        {
            goto Exit;
        }
        parametersKeyOpened = TRUE;

        status = IPv6ToBleRegistryOpenMeshListKey();
        if (!NT_SUCCESS(status))
        {
            goto Exit;
        }
        status = WdfRegistryRemoveKey(gMeshListKey);
        if (!NT_SUCCESS(status))
        {
            TraceEvents(TRACE_LEVEL_ERROR, TRACE_QUEUE, "Removing white list key failed during %!FUNC!, status: %!STATUS!", status);
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
IPv6ToBleRuntimeListPurgeWhiteList()
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

    // Check for empty list
    if (IsListEmpty(gWhiteListHead))
    {
        TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_RUNTIME_LIST, "List is empty; nothing to purge.\n");
        return;
    }

    // Clean up the linked list
    while (!IsListEmpty(gWhiteListHead))
    {
        PLIST_ENTRY entry = RemoveHeadList(gWhiteListHead);   // remove from list
        PWHITE_LIST_ENTRY whiteListEntry = CONTAINING_RECORD(entry,
                                                             WHITE_LIST_ENTRY,
                                                             listEntry
                                                             );
        
        ExFreePoolWithTag(whiteListEntry, IPV6_TO_BLE_WHITE_LIST_TAG); // free white list entry memory
        whiteListEntry = 0;       
    }

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_RUNTIME_LIST, "%!FUNC! Exit");
}

_Use_decl_annotations_
VOID
IPv6ToBleRuntimeListPurgeMeshList()
/*++
Routine Description:

    Cleans up the runtime linked list of mesh list entries, if possible.

    This function is called from the device cleanup callback, which is called
    at IRQL = PASSIVE_LEVEL.

Arguments:

    None.

Return Value:

    None.

--*/
{
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_RUNTIME_LIST, "%!FUNC! Entry");

    // Clean up the linked list
    while (!IsListEmpty(gMeshListHead))
    {
        PLIST_ENTRY entry = gMeshListHead->Flink;
        PMESH_LIST_ENTRY meshListEntry = CONTAINING_RECORD(entry,
                                                           MESH_LIST_ENTRY,
                                                           listEntry
                                                           );
        entry = RemoveHeadList(gMeshListHead); // remove from list
        entry = 0;
        ExFreePoolWithTag(meshListEntry, IPV6_TO_BLE_MESH_LIST_TAG); // free memory
        meshListEntry = 0;  // free pointer        
    }

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_RUNTIME_LIST, "%!FUNC! Exit");
}