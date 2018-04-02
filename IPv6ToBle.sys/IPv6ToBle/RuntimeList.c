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
    PCWSTR desiredAddress;
    status = WdfRequestRetrieveInputBuffer(Request,
                                           sizeof(WCHAR) * 3,
                                           &inputBuffer, 
                                           &receivedSize
                                           );
    if (!NT_SUCCESS(status))
    {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_RUNTIME_LIST, "Retrieving input bufer from WDFREQUEST failed during %!FUNC! with %!STATUS!", status);
        goto Exit;
    }
    desiredAddress = (PCWSTR)inputBuffer;

    //
    // Step 2
    // Validate the received address and get its 16 byte value form
    //
    IN6_ADDR ipv6AddressStorage;
    UINT32 conversionStatus = IPv6ToBleIPAddressV6StringToValue(desiredAddress,
                                   &ipv6AddressStorage.u.Byte[0]
                               );
    if (conversionStatus != NO_ERROR)
    {
        status = STATUS_INVALID_PARAMETER;
        goto Exit;
    }

    //
    // Step 3
    // Get the device context and white list head
    // 
    PIPV6_TO_BLE_DEVICE_CONTEXT deviceContext = IPv6ToBleGetContextFromDevice(
                                                    globalWdfDeviceObject
                                                );
    PLIST_ENTRY whiteListEntry = deviceContext->whiteListHead->Flink;

    NT_ASSERT(whiteListEntry);

    //
    // Step 4
    // Verify the entry isn't already in the list
    // 
    while (whiteListEntry != deviceContext->whiteListHead)
    {
        PWHITE_LIST_ENTRY entry = CONTAINING_RECORD(whiteListEntry, 
                                                    WHITE_LIST_ENTRY, 
                                                    listEntry
                                                    );
        // Compare the memory (byte arrays)        
        if (RtlCompareMemory(entry->ipv6Address,
                             (UINT8*)&ipv6AddressStorage.u.Byte[0],
                             IPV6_ADDRESS_LENGTH))
        {
            // Found it            
            status = STATUS_INVALID_PARAMETER;
            TraceEvents(TRACE_LEVEL_WARNING, TRACE_RUNTIME_LIST, "Entry is already in the white list %!STATUS!", status);
            goto Exit;
        }

        whiteListEntry = whiteListEntry->Flink;
    }

    //
    // Step 5
    // Assuming it is not a duplicate, add the entry to the list
    //
    // Using non-paged pool because the list may be accessed at
    // IRQL = DISPATCH_LEVEL
    PWHITE_LIST_ENTRY newEntry = (PWHITE_LIST_ENTRY)ExAllocatePoolWithTag(
        NonPagedPoolNx,
        sizeof(WHITE_LIST_ENTRY),
        IPV6_TO_BLE_WHITE_LIST_TAG
    );
    if (!newEntry)
    {
        status = STATUS_INSUFFICIENT_RESOURCES;
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_RUNTIME_LIST, "New white list entry allocation failed %!STATUS!", status);
        goto Exit;
    }

    // Add the entry to the list
    InsertHeadList(deviceContext->whiteListHead, &newEntry->listEntry);

    // Insert the address into the entry
    newEntry->ipv6Address = (UINT8*)(ipv6AddressStorage.u.Byte[0]);

    //
    // Step 6
    // Update the boolean in the device context that the list has been modified
    // by acquiring the spinlock. This raises us to DISPATCH_LEVEL so we aren't
    // interrupted by the timer callback that flushes the list to the registry
    // if the list has been updated
    //
    WdfSpinLockAcquire(deviceContext->whiteListModifiedLock);
    deviceContext->whiteListModified = TRUE;
    WdfSpinLockRelease(deviceContext->whiteListModifiedLock);

    NT_ASSERT(irql == KeGetCurrentIrql());

    //
    // Step 7
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
    if (!IsListEmpty(deviceContext->meshListHead))
    {
        if (deviceContext->calloutsRegistered)
        {
            IPv6ToBleCalloutsUnregister();
            status = IPv6ToBleCalloutsRegister();
            if (NT_SUCCESS(status))
            {
                deviceContext->calloutsRegistered = TRUE;
            }
        }
        else
        {
            status = IPv6ToBleCalloutsRegister();
            if (NT_SUCCESS(status))
            {
                deviceContext->calloutsRegistered = TRUE;
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
    PCWSTR desiredAddress;
    status = WdfRequestRetrieveInputBuffer(Request,
                                           sizeof(WCHAR) * 3,
                                           &inputBuffer,
                                           &receivedSize
                                           );
    if (!NT_SUCCESS(status))
    {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_RUNTIME_LIST, "Retrieving input bufer from WDFREQUEST failed during %!FUNC! with %!STATUS!", status);
        goto Exit;
    }
    desiredAddress = (PCWSTR)inputBuffer;

    //
    // Step 2
    // Validate the received address and get its 16 byte value form
    //
    IN6_ADDR ipv6AddressStorage;
    UINT32 conversionStatus = IPv6ToBleIPAddressV6StringToValue(desiredAddress,
                                   &ipv6AddressStorage.u.Byte[0]
                               );
    if (conversionStatus != NO_ERROR)
    {
        status = STATUS_INVALID_PARAMETER;
        goto Exit;
    }

    //
    // Step 3
    // Get the device context and mesh list head
    // 
    PIPV6_TO_BLE_DEVICE_CONTEXT deviceContext = IPv6ToBleGetContextFromDevice(
                                                    globalWdfDeviceObject
                                                );
    PLIST_ENTRY meshListEntry = deviceContext->meshListHead->Flink;

    NT_ASSERT(meshListEntry);

    //
    // Step 4
    // Verify the entry isn't already in the list
    // 
    while (meshListEntry != deviceContext->meshListHead)
    {
        PMESH_LIST_ENTRY entry = CONTAINING_RECORD(meshListEntry,
                                                   MESH_LIST_ENTRY,
                                                   listEntry
                                                   );
        // Compare the memory (byte arrays)        
        if (RtlCompareMemory(entry->ipv6Address,
                             (UINT8*)&ipv6AddressStorage.u.Byte[0],
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
    PMESH_LIST_ENTRY newEntry = (PMESH_LIST_ENTRY)ExAllocatePoolWithTag(
        NonPagedPoolNx,
        sizeof(MESH_LIST_ENTRY),
        IPV6_TO_BLE_MESH_LIST_TAG
    );
    if (!newEntry)
    {
        status = STATUS_INSUFFICIENT_RESOURCES;
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_RUNTIME_LIST, "New mesh list entry allocation failed %!STATUS!", status);
        goto Exit;
    }

    // Add the entry to the list
    InsertHeadList(deviceContext->meshListHead, &newEntry->listEntry);

    // Insert the address into the entry
    newEntry->ipv6Address = (UINT8*)(ipv6AddressStorage.u.Byte[0]);

    //
    // Step 6
    // Update the boolean in the device context that the list has been modified
    // by acquiring the spinlock. This raises us to DISPATCH_LEVEL so we aren't
    // interrupted by the timer callback that flushes the list to the registry
    // if the list has been updated
    //
    WdfSpinLockAcquire(deviceContext->meshListModifiedLock);
    deviceContext->meshListModified = TRUE;
    WdfSpinLockRelease(deviceContext->meshListModifiedLock);

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
    if (!IsListEmpty(deviceContext->whiteListHead))
    {
        if (deviceContext->calloutsRegistered) 
        {
            IPv6ToBleCalloutsUnregister();
            status = IPv6ToBleCalloutsRegister();
            if (NT_SUCCESS(status))
            {
                deviceContext->calloutsRegistered = TRUE;
            }
        }
        else
        {
            status = IPv6ToBleCalloutsRegister();
            if (NT_SUCCESS(status))
            {
                deviceContext->calloutsRegistered = TRUE;
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

    PVOID inputBuffer;
    size_t receivedSize = 0;

    //
    // Step 1
    // Retrieve the address from the request's input buffer. It must be in the
    // form of a string representation of the IPv6 address. Technically, an
    // IPv6 address can be valid with only 3 characters (e.g. ::1) so that is
    // the *minimum* size needed. However, it is expected to be a full address.
    // 
    PCWSTR desiredAddress;
    status = WdfRequestRetrieveInputBuffer(Request,
                                           sizeof(WCHAR) * 3,
                                           &inputBuffer,
                                           &receivedSize
                                           );
    if (!NT_SUCCESS(status))
    {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_RUNTIME_LIST, "Retrieving input bufer from WDFREQUEST failed during %!FUNC! with %!STATUS!", status);
        goto Exit;
    }
    desiredAddress = (PCWSTR)inputBuffer;

    //
    // Step 2
    // Validate the received address and get its 16 byte value form
    //
    IN6_ADDR ipv6AddressStorage;
    UINT32 conversionStatus = IPv6ToBleIPAddressV6StringToValue(desiredAddress,
                                   &ipv6AddressStorage.u.Byte[0]
                               );
    if (conversionStatus != NO_ERROR)
    {
        status = STATUS_INVALID_PARAMETER;
        goto Exit;
    }

    //
    // Step 3
    // Get the device context and white list head
    // 
    PIPV6_TO_BLE_DEVICE_CONTEXT deviceContext = IPv6ToBleGetContextFromDevice(
                                                    globalWdfDeviceObject
                                                );
    PLIST_ENTRY entry = deviceContext->whiteListHead->Flink;

    NT_ASSERT(entry);

    //
    // Step 4
    // Traverse the list and remove the entry if we find it
    // 
    while (entry != deviceContext->whiteListHead)
    {
        PWHITE_LIST_ENTRY whiteListEntry = CONTAINING_RECORD(entry,
                                                             WHITE_LIST_ENTRY,
                                                             listEntry
                                                             );
        // Compare the memory (byte arrays)        
        if (RtlCompareMemory(whiteListEntry->ipv6Address,
            (UINT8*)&ipv6AddressStorage.u.Byte[0],
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
        WdfSpinLockAcquire(deviceContext->whiteListModifiedLock);
        deviceContext->whiteListModified = TRUE;
        WdfSpinLockRelease(deviceContext->whiteListModifiedLock);
    }

    //
    // Step 6
    // If the white list is now empty and the callouts were registered,
    // unregister the callouts. Doesn't matter about the mesh list.
    //
    if (IsListEmpty(deviceContext->whiteListHead) && 
        deviceContext->calloutsRegistered)
    {
        IPv6ToBleCalloutsUnregister();
        deviceContext->calloutsRegistered = FALSE;
    }

Exit:

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

    PVOID inputBuffer;
    size_t receivedSize = 0;

    //
    // Step 1
    // Retrieve the address from the request's input buffer. It must be in the
    // form of a string representation of the IPv6 address. Technically, an
    // IPv6 address can be valid with only 3 characters (e.g. ::1) so that is
    // the *minimum* size needed. However, it is expected to be a full address.
    // 
    PCWSTR desiredAddress;
    status = WdfRequestRetrieveInputBuffer(Request,
                                           sizeof(WCHAR) * 3,
                                           &inputBuffer,
                                           &receivedSize
                                           );
    if (!NT_SUCCESS(status))
    {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_RUNTIME_LIST, "Retrieving input bufer from WDFREQUEST failed during %!FUNC! with %!STATUS!", status);
        goto Exit;
    }
    desiredAddress = (PCWSTR)inputBuffer;

    //
    // Step 2
    // Validate the received address and get its 16 byte value form
    //
    IN6_ADDR ipv6AddressStorage;
    UINT32 conversionStatus = IPv6ToBleIPAddressV6StringToValue(desiredAddress,
                                    &ipv6AddressStorage.u.Byte[0]
                              );
    if (conversionStatus != NO_ERROR)
    {
        status = STATUS_INVALID_PARAMETER;
        goto Exit;
    }

    //
    // Step 3
    // Get the device context and mesh list head
    // 
    PIPV6_TO_BLE_DEVICE_CONTEXT deviceContext = IPv6ToBleGetContextFromDevice(
                                                    globalWdfDeviceObject
                                                );
    PLIST_ENTRY entry = deviceContext->meshListHead->Flink;

    NT_ASSERT(entry);

    //
    // Step 4
    // Traverse the list and remove the entry if we find it
    // 
    while (entry != deviceContext->meshListHead)
    {
        PMESH_LIST_ENTRY meshListEntry = CONTAINING_RECORD(entry,
                                                           MESH_LIST_ENTRY,
                                                           listEntry
                                                           );
        // Compare the memory (byte arrays)        
        if (RtlCompareMemory(meshListEntry->ipv6Address,
            (UINT8*)&ipv6AddressStorage.u.Byte[0],
            IPV6_ADDRESS_LENGTH))
        {
            // Found it, now remove it
            isInList = TRUE;
            BOOLEAN removed = RemoveEntryList(entry);
            if (!removed)
            {
                status = STATUS_UNSUCCESSFUL;
                TraceEvents(TRACE_LEVEL_ERROR, TRACE_RUNTIME_LIST, "Removing mesh list entry from list failed %!STATUS!", status);
                goto Exit;
            }
            ExFreePoolWithTag(meshListEntry, IPV6_TO_BLE_MESH_LIST_TAG);
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
        WdfSpinLockAcquire(deviceContext->meshListModifiedLock);
        deviceContext->meshListModified = TRUE;
        WdfSpinLockRelease(deviceContext->meshListModifiedLock);
    }

    //
    // Step 6
    // If the mesh list is now empty and the callouts were registered,
    // unregister the callouts. Doesn't matter about the white list.
    //
    if (IsListEmpty(deviceContext->meshListHead) &&
        deviceContext->calloutsRegistered)
    {
        IPv6ToBleCalloutsUnregister();
        deviceContext->calloutsRegistered = FALSE;
    }

Exit:

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

    // Get the device context
    PIPV6_TO_BLE_DEVICE_CONTEXT deviceContext = IPv6ToBleGetContextFromDevice(
                                                    globalWdfDeviceObject
                                                );

    // Clean up the linked list
    while (!IsListEmpty(deviceContext->whiteListHead))
    {
        PLIST_ENTRY entry = deviceContext->whiteListHead->Flink;
        PWHITE_LIST_ENTRY whiteListEntry = CONTAINING_RECORD(entry,
                                                             WHITE_LIST_ENTRY,
                                                             listEntry
                                                             );
        entry = RemoveHeadList(deviceContext->whiteListHead);   // remove from list
        entry = 0;
        ExFreePoolWithTag(whiteListEntry, IPV6_TO_BLE_WHITE_LIST_TAG); // free memory
        whiteListEntry = 0;  // free pointer

        TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_RUNTIME_LIST, "%!FUNC! Exit");
    }
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

    // Get the device context
    PIPV6_TO_BLE_DEVICE_CONTEXT deviceContext = IPv6ToBleGetContextFromDevice(
        globalWdfDeviceObject
    );

    // Clean up the linked list
    while (!IsListEmpty(deviceContext->meshListHead))
    {
        PLIST_ENTRY entry = deviceContext->meshListHead->Flink;
        PMESH_LIST_ENTRY meshListEntry = CONTAINING_RECORD(entry,
                                                           MESH_LIST_ENTRY,
                                                           listEntry
                                                           );
        entry = RemoveHeadList(deviceContext->meshListHead); // remove from list
        entry = 0;
        ExFreePoolWithTag(meshListEntry, IPV6_TO_BLE_MESH_LIST_TAG); // free memory
        meshListEntry = 0;  // free pointer        
    }

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_RUNTIME_LIST, "%!FUNC! Exit");
}