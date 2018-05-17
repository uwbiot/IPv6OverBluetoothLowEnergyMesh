/*++

Module Name:

	Helpers_Registry

Abstract:

	This file contains implementations for registry helper functions.
    
    This file and its header are only used on the gateway device.

Environment:

	Kernel-mode Driver Framework

--*/

#include "Includes.h"
#include "Helpers_Registry.tmh" // auto-generated tracing file

_Use_decl_annotations_
NTSTATUS
IPv6ToBleRegistryOpenParametersKey()
/*++
Routine Description:

    Opens the driver parameters registry key assigned to the driver by the
    system at driver creation time.

Arguments:

    None. Accesses global variables defined in Driver.h.

Return Value:

    Returns STATUS_SUCCESS if the key was opened successfully. Otherwise, an
    appropriate NTSTATUS error code.

--*/
{
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HELPERS_REGISTRY, "%!FUNC! Entry");

    NTSTATUS status = STATUS_SUCCESS;

    // Open the key; the API creates the key if it is not there so this would
    // only fail if we lack permissions, low resources, or some such issue
    status = WdfDriverOpenParametersRegistryKey(gWdfDriverObject,
                                                KEY_ALL_ACCESS,
                                                WDF_NO_OBJECT_ATTRIBUTES,
                                                &gParametersKey
                                                );
    if (!NT_SUCCESS(status)) {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_HELPERS_REGISTRY, "Opening parameters registry key failed with this status code: %!STATUS!", status);
    }
    
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HELPERS_REGISTRY, "%!FUNC! Exit");

    return status;
}

_Use_decl_annotations_
NTSTATUS
IPv6ToBleRegistryOpenWhiteListKey()
/*++
Routine Description:

	Opens the white list registry key or creates it if it does not exist. The
	key is opened with all permissions, enabling the caller to either read or
	write the key.

Arguments:

	None. Accesses global variables defined in Driver.h.

Return Value:

	Returns STATUS_SUCCESS if the key was opened successfully. Otherwise, an
	appropriate NTSTATUS error code.

--*/
{
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HELPERS_REGISTRY, "%!FUNC! Entry");

	NTSTATUS status = STATUS_SUCCESS;

	// Define the key name
	DECLARE_CONST_UNICODE_STRING(whiteListKeyName,
								 L"TrustedExternalDeviceWhiteList"
								 );

	// Open the white list key. This API either creates the key if it doesn't 
	// exist or opens the key if it does exist. The parent key must exist, and
	// it does since that is the key assigned to the driver by the system in
	// DriverEntry. But the caller must have OPENED the parent key before
    // calling this function.
	status = WdfRegistryCreateKey(gParametersKey,
		                          &whiteListKeyName,
		                          KEY_ALL_ACCESS,
		                          REG_OPTION_NON_VOLATILE,
		                          NULL,
		                          WDF_NO_OBJECT_ATTRIBUTES,
		                          &gWhiteListKey
	                              );
	if (!NT_SUCCESS(status))
	{
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_HELPERS_REGISTRY, "Opening white list registry key failed %!STATUS!", status);
	}

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HELPERS_REGISTRY, "%!FUNC! Exit");

	return status;
}

_Use_decl_annotations_
NTSTATUS
IPv6ToBleRegistryOpenMeshListKey()
/*++
Routine Description:

	Opens the mesh list registry key or creates it if it does not exist. The
	key is opened with all permissions, enabling the caller to either read or
	write the key.

Arguments:

	None. Accesses global variables defined in Driver.h.

Return Value:

	Returns STATUS_SUCCESS if the key was opened successfully. Otherwise, an
	appropriate NTSTATUS error code.

--*/
{
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HELPERS_REGISTRY, "%!FUNC! Entry");

	NTSTATUS status = STATUS_SUCCESS;

	// Define the key name
	DECLARE_CONST_UNICODE_STRING(meshListKeyName, L"MeshDeviceList");

	// Open the mesh list key. This API either creates the key if it doesn't 
	// exist or opens the key if it does exist. The parent key must exist, and
	// it does since that is the key assigned to the driver by the system in
	// DriverEntry. But the caller must have OPENED the parent key before
    // calling this function.
	status = WdfRegistryCreateKey(gParametersKey,
		                          &meshListKeyName,
		                          KEY_ALL_ACCESS,
		                          REG_OPTION_NON_VOLATILE,
		                          NULL,
		                          WDF_NO_OBJECT_ATTRIBUTES,
		                          &gMeshListKey
	                              );
	if (!NT_SUCCESS(status))
	{
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_HELPERS_REGISTRY, "Opening mesh list registry key failed %!STATUS!", status);
    }

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HELPERS_REGISTRY, "%!FUNC! Exit");

	return status;
}

_Use_decl_annotations_
NTSTATUS
IPv6ToBleRegistryCheckBorderRouterFlag()
/*++
Routine Description:

	Attempts to read the configuration flag from the registry. The default value
	in the INF file is set to 0 for false, meaning that the default configuration
	of the driver is to behave as a non-border router device.

	On the border router, the user will have to manually change the registry key
	to 1 for true.

	This decision is to minimize having to change this key, as it is presumed
	that there will be a much lower ratio of border router(s) to devices in the
	mesh.

Arguments:

	None. Accesses the global parameters key variable.

Return Value:

	STATUS_SUCCESS if the operation was successful; appropriate NTSTATUS error
	code otherwise.

--*/
{
	NTSTATUS status = STATUS_SUCCESS;
	BOOLEAN parametersKeyOpened = FALSE;

	// Open the parameters key
	status = IPv6ToBleRegistryOpenParametersKey();
	if (!NT_SUCCESS(status))
	{
		TraceEvents(TRACE_LEVEL_ERROR, TRACE_HELPERS_REGISTRY, "Could not open the parameters key, %!STATUS!", status);
		goto Exit;
	}
	parametersKeyOpened = TRUE;

	// Query the key
	DECLARE_CONST_UNICODE_STRING(borderRouterFlagValueName, L"Border Router");
	ULONG borderRouterFlagValue = 0;
	status = WdfRegistryQueryULong(gParametersKey,
								   &borderRouterFlagValueName,
								   &borderRouterFlagValue
								   );
	if (!NT_SUCCESS(status))
	{
		// The key was empty or the value did not exist
		TraceEvents(TRACE_LEVEL_ERROR, TRACE_HELPERS_REGISTRY, "Could not load the border router flag value from the driver's parameters key, %!STATUS!", status);
		goto Exit;
	}

	// Set the border router flag according to the value in the registry
	if (borderRouterFlagValue == 0)
	{
		gBorderRouterFlag = FALSE;
	}
	else if (borderRouterFlagValue == 1)
	{
		gBorderRouterFlag = TRUE;
	}
	else
	{
		// Invalid value, return invalid parameter which causes the driver to
		// fail to load
		status = STATUS_INVALID_PARAMETER;
		TraceEvents(TRACE_LEVEL_ERROR, TRACE_HELPERS_REGISTRY, "Value stored in the parameters key for the border router flag was invalid. It must be 0 for non-BR and 1 for BR. Status = %!STATUS!", status);
	}

Exit:

	return status;
}

_Use_decl_annotations_
NTSTATUS
IPv6ToBleRegistryRetrieveRuntimeList(
	_In_ ULONG TargetList
)
/*++
Routine Description:

	Loads information about the trusted external device white list. If it has
	values in it, assign them to the runtime context so we don't have to keep
	accessing the registry.

    This function is only called from DriverEntry so it should be called at
    PASSIVE_LEVEL.

Arguments:

	None. Accesses global variables defined in Driver.h.

Return Value:

	Returns STATUS_SUCCESS if the list was populated, and an appropriate
	NTSTATUS error code otherwise. If there is nothing stored in the key yet,
	or the list is empty, we return that error. The driver then sits and waits
	for enough information to act and does not fail its driver entry. This will
	happen either the very first time the driver is installed, or if the user
	deletes all entries in the list during runtime and reboots.

	If this occurs, the driver finishes its driver entry and does not register
	its callouts and filters yet. They would be registered later once the
	driver receives further addresses from the usermode app and each list has
	at least one address.
--*/
{
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HELPERS_REGISTRY, "%!FUNC! Entry");

	NTSTATUS status = STATUS_SUCCESS;

	WDFCOLLECTION listAddresses = NULL;
	WDF_OBJECT_ATTRIBUTES addressStringsAttributes;

	ULONG i;
	ULONG count;

    BOOLEAN parametersKeyOpened = FALSE;
    BOOLEAN listKeyOpened = FALSE;

	IN6_ADDR ipv6AddressStorage;

	// Validate input
	if (TargetList != WHITE_LIST && TargetList != MESH_LIST)
	{
		status = STATUS_INVALID_PARAMETER;
		TraceEvents(TRACE_LEVEL_ERROR, TRACE_HELPERS_REGISTRY, "Invalid list option during %!FUNC! with %!STATUS!", status);
		goto Exit;
	}

	//
	// Step 1
	// Retrieve the list key. It is a REG_MULTI_SZ key, or multi strings
	// key. Assign its string contents to the runtime context for the device.
	// 
	// NOTE: If this operation fails for the white list, DriverEntry skips to the
	// mesh list since there may be something there even if the white list is 
	// empty.
	//

	// Declare the name of the value we're querying from the key
	DECLARE_CONST_UNICODE_STRING(whiteListValueName, L"WhiteList");
	DECLARE_CONST_UNICODE_STRING(meshListValueName, L"MeshList");
	const UNICODE_STRING listValueName = (TargetList == WHITE_LIST ? whiteListValueName : meshListValueName);

	// Create a collection to store the retrieved white list addresses
	status = WdfCollectionCreate(WDF_NO_OBJECT_ATTRIBUTES,
		                         &listAddresses
	                             );
	if (!NT_SUCCESS(status))
	{
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_HELPERS_REGISTRY, "WDFCOLLECTION creation failed during %!FUNC! with %!STATUS!", status);
		goto Exit;
	}

	// Set the collection to be the parent of the retrieved string objects
	WDF_OBJECT_ATTRIBUTES_INIT(&addressStringsAttributes);
	addressStringsAttributes.ParentObject = listAddresses;

    // Open the parent key    
    status = IPv6ToBleRegistryOpenParametersKey();
    if (!NT_SUCCESS(status))
    {
        goto Exit;
    }
    parametersKeyOpened = TRUE;

    // Open the list key
	if (TargetList == WHITE_LIST)
	{
		status = IPv6ToBleRegistryOpenWhiteListKey();
		if (!NT_SUCCESS(status))
		{
			goto Exit;
		}
	}
	else
	{
		status = IPv6ToBleRegistryOpenMeshListKey();
		if (!NT_SUCCESS(status))
		{
			goto Exit;
		}
	}	
	listKeyOpened = TRUE;

	// Query the list key. Fails first time driver is installed or if the
	// user purged the list and rebooted because the key exists but is empty.
	if (TargetList == WHITE_LIST)
	{
		status = WdfRegistryQueryMultiString(gWhiteListKey,
											 &listValueName,
											 &addressStringsAttributes,
											 listAddresses
											 );
		if (!NT_SUCCESS(status))
		{
			// If the key is empty, status will be STATUS_RESOURCE_DATA_NOT_FOUND.
			TraceEvents(TRACE_LEVEL_WARNING, TRACE_HELPERS_REGISTRY, "Querying white list failed because it was empty %!STATUS!", status);
			goto Exit;
		}
	}
	else
	{
		status = WdfRegistryQueryMultiString(gMeshListKey,
											 &listValueName,
											 &addressStringsAttributes,
											 listAddresses
											 );
		if (!NT_SUCCESS(status))
		{
			// If the key is empty, status will be STATUS_RESOURCE_DATA_NOT_FOUND.
			TraceEvents(TRACE_LEVEL_WARNING, TRACE_HELPERS_REGISTRY, "Querying mesh list failed because it was empty %!STATUS!", status);
			goto Exit;
		}
	}
	

	//
	// Step 2
	// Set the list key's contents to the runtime list. We don't have 
	// to worry about synchronizing access to the lists because this function 
	// is either called from DriverEntry, or later from the context of the 
	// EvtDeviceIoControl callback, and access to those is synchronized by the
	// framework.
	//

	// Since the list is non-empty, we can walk the list, get the strings, and
	// assign them to the context. No need for synchronization because this
	// function is called from DriverEntry, which is synchronized.
	count = WdfCollectionGetCount(listAddresses);
	for (i = 0; i < count; i++)
	{

		// Get the string from the collection retrieved from the registry. It
        // should be null-terminated already if it was stored there correctly
        // in the first place.
		DECLARE_UNICODE_STRING_SIZE(currentIpv6Address, INET6_ADDRSTRLEN);
		WDFSTRING currentWdfString = (WDFSTRING)WdfCollectionGetItem(
			                              listAddresses,
			                              i
		                              );
		WdfStringGetUnicodeString(currentWdfString, &currentIpv6Address);

        // Defensively null-terminate the string
        currentIpv6Address.Length = min(currentIpv6Address.Length,
                                        currentIpv6Address.MaximumLength - sizeof(WCHAR)
                                        );
        currentIpv6Address.Buffer[currentIpv6Address.Length / sizeof(WCHAR)] = UNICODE_NULL;

        // Convert the string to its 16-byte value and scope ID
		ULONG scopeId = 0;
		USHORT port = 0;
        status = RtlIpv6StringToAddressExW(currentIpv6Address.Buffer,
                                          &ipv6AddressStorage,
										  &scopeId,
										  &port
                                          );

        // Create the list entry and add it
        if (NT_SUCCESS(status))
		{
			if (TargetList == WHITE_LIST)
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
		}
        else
        {
            TraceEvents(TRACE_LEVEL_ERROR, TRACE_HELPERS_REGISTRY, "Converting IPv6 string to address failed during %!FUNC! with %!STATUS!", status);
            goto Exit;
        }

		// Zero out the storage structure for next time
		RtlZeroMemory(&ipv6AddressStorage, sizeof(IN6_ADDR));
	}

Exit:
    // Clean up any memory allocated if we failed at some point
    if (!NT_SUCCESS(status))
    {
        IPv6ToBleRuntimeListPurgeRuntimeList(TargetList);
    }

    // Close the keys
    if (parametersKeyOpened)
    {
        WdfRegistryClose(gParametersKey);
    }
    if (listKeyOpened)
    {
		if (TargetList == WHITE_LIST)
		{
			WdfRegistryClose(gWhiteListKey);
		}
		else
		{
			WdfRegistryClose(gMeshListKey);
		}        
    }

    // Clean up the collection object and its children
    if (listAddresses)
    {
        WdfObjectDelete(listAddresses);
    }

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HELPERS_REGISTRY, "%!FUNC! Exit");

    return status;
}

_Use_decl_annotations_
NTSTATUS
IPv6ToBleRegistryAssignRuntimeList(
	_In_ ULONG TargetList
)
/*++
Routine Description:

    Assigns the runtime white list to the registry and overwrites what is
    there. Converts the byte values used during runtime back to string forms
    for storage.

    This function is called from the periodic timer callback function, which
    is called at DISPATCH_LEVEL.

Arguments:

    TargetList - Determines which runtime list on which to operate. 0 for white
		list, 1 for mesh list.

Return Value:

    STATUS_SUCCESS if successful; appropriate NTSTATUS error codes otherwise.

--*/
{
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HELPERS_REGISTRY, "%!FUNC! Entry");

    NTSTATUS status = STATUS_SUCCESS;
    BOOLEAN parametersKeyOpened = FALSE;
    BOOLEAN listKeyOpened = FALSE;  
    WDFCOLLECTION addressCollection = { 0 };
    WDF_OBJECT_ATTRIBUTES attributes;

	// Validate input
	if (TargetList != WHITE_LIST && TargetList != MESH_LIST)
	{
		status = STATUS_INVALID_PARAMETER;
		TraceEvents(TRACE_LEVEL_ERROR, TRACE_HELPERS_REGISTRY, "Invalid list option during %!FUNC! with %!STATUS!", status);
		goto Exit;
	}

    //
    // Step 1
    // Check for empty list (counts as success)
    //
	if (TargetList == WHITE_LIST)
	{
		if (IsListEmpty(gWhiteListHead))
		{
			TraceEvents(TRACE_LEVEL_WARNING, TRACE_HELPERS_REGISTRY, "White list is empty - nothing to write to registry %!STATUS!", status);
			goto Exit;
		}
	} 
	else
	{
		if (IsListEmpty(gMeshListHead))
		{
			TraceEvents(TRACE_LEVEL_WARNING, TRACE_HELPERS_REGISTRY, "Mesh list is empty - nothing to write to registry %!STATUS!", status);
			goto Exit;
		}
	}
    

    //
    // Step 2
    // Open the key
    //

    // Open the parent key    
    status = IPv6ToBleRegistryOpenParametersKey();
    if (!NT_SUCCESS(status))
    {
        goto Exit;
    }
    parametersKeyOpened = TRUE;

    // Open the list key
	// Open the list key
	if (TargetList == WHITE_LIST)
	{
		status = IPv6ToBleRegistryOpenWhiteListKey();
		if (!NT_SUCCESS(status))
		{
			goto Exit;
		}
	}
	else
	{
		status = IPv6ToBleRegistryOpenMeshListKey();
		if (!NT_SUCCESS(status))
		{
			goto Exit;
		}
	}
    listKeyOpened = TRUE;

    //
    // Step 3
    // Create the collection object that will be
    // assigned to the key (it is a collection of strings; the key is a 
    // REG_MULTI_SZ key)
    //
    status = WdfCollectionCreate(WDF_NO_OBJECT_ATTRIBUTES, &addressCollection);
    if (!NT_SUCCESS(status))
    {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_HELPERS_REGISTRY, "WDFCOLLECTION creation failed during %!FUNC! with %!STATUS!", status);
        goto Exit;
    }

    //
    // Step 4
    // Traverse the list and add each entry's IPv6 address to the WDFCOLLECTION
    //
	PLIST_ENTRY entry = TargetList == WHITE_LIST ? gWhiteListHead->Flink : gMeshListHead->Flink;
	if (!IsListEmpty(TargetList == WHITE_LIST ? gWhiteListHead : gMeshListHead))
	{
		while (entry != (TargetList == WHITE_LIST ? gWhiteListHead : gMeshListHead))
		{
			// Get the struct that contains this entry
			
			// This is the closest we can get to dynamic type declaration at
			// runtime
			union {
				PWHITE_LIST_ENTRY whiteListEntry;
				PMESH_LIST_ENTRY meshListEntry;
			} runtimeListEntry;

			if (TargetList == WHITE_LIST)
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

			// Declare a unicode string to hold the to-be converted address
			DECLARE_UNICODE_STRING_SIZE(currentIpv6AddressString,
										INET6_ADDRSTRLEN
										);

			// Convert the address to a string (function null-terminates it)
			ULONG currentIpv6AddressStringLength = INET6_ADDRSTRLEN;
			status = RtlIpv6AddressToStringExW(TargetList == WHITE_LIST ? &runtimeListEntry.whiteListEntry->ipv6Address : &runtimeListEntry.meshListEntry->ipv6Address,
											   TargetList == WHITE_LIST ? runtimeListEntry.whiteListEntry->scopeId : runtimeListEntry.meshListEntry->scopeId,
											   0,
											   (PWSTR)currentIpv6AddressString.Buffer,
											   &currentIpv6AddressStringLength
											   );
			if (!NT_SUCCESS(status))
			{
				TraceEvents(TRACE_LEVEL_WARNING, TRACE_HELPERS_REGISTRY, "Converting IPv6 address to string failed during %!FUNC! with %!STATUS!", status);
				goto Exit;
			}

			// Assign the actual number of bytes written to the UNICODE_STRING
			// structure. Unicode characters are 2 bytes each and the
			// conversion function reported the total number of CHARACTERS
			// written to the buffer, so that is the length. Also, the max
			// length must be a power of 2 accordingly, which is why
			// INET6_ADDRSTRLEN is defined as 65 (64 + null terminator).
			currentIpv6AddressString.Length = (sizeof(BYTE) * 2) * (USHORT)currentIpv6AddressStringLength;

			// Initialize and create the WDF string object from the Unicode string
			WDFSTRING currentIpv6AddressWdfString;
			WDF_OBJECT_ATTRIBUTES_INIT(&attributes);
			attributes.ParentObject = addressCollection;

			status = WdfStringCreate(&currentIpv6AddressString,
									 &attributes,
									 &currentIpv6AddressWdfString
									 );
			if (!NT_SUCCESS(status))
			{
				TraceEvents(TRACE_LEVEL_ERROR, TRACE_HELPERS_REGISTRY, "WdfStringCreate failed during %!FUNC! with %!STATUS!", status);
				goto Exit;
			}

			// Add the WDFSTRING to the WDFCOLLECTION
			status = WdfCollectionAdd(addressCollection,
									  currentIpv6AddressWdfString
									  );
			if (!NT_SUCCESS(status))
			{
				TraceEvents(TRACE_LEVEL_ERROR, TRACE_HELPERS_REGISTRY, "WdfCollectionAdd failed during %!FUNC! with %!STATUS!", status);
				goto Exit;
			}

			// Advance the list
			entry = entry->Flink;
		}
    }
    
    //
    // Step 5
    // Assign the collection of string objects to the registry key
    //

    // Declare the name of the value we're assigning to the key
	DECLARE_CONST_UNICODE_STRING(whiteListValueName, L"WhiteList");
	DECLARE_CONST_UNICODE_STRING(meshListValueName, L"MeshList");
	const UNICODE_STRING listValueName = (TargetList == WHITE_LIST ? whiteListValueName : meshListValueName);

    // Assign the collection of strings to the registry key's value
    status = WdfRegistryAssignMultiString(TargetList == WHITE_LIST ? gWhiteListKey : gMeshListKey, 
                                          &listValueName, 
                                          addressCollection
                                          );
    if (!NT_SUCCESS(status))
    {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_HELPERS_REGISTRY, "WdfRegistryAssignMultiString failed during %!FUNC! with %!STATUS!", status);
    }

Exit:

    // Close the keys if they were opened
    if (parametersKeyOpened)
    {
        WdfRegistryClose(gParametersKey);
    }
    if (listKeyOpened)
    {
        WdfRegistryClose(TargetList == WHITE_LIST ? gWhiteListKey : gMeshListKey);
    }

    // Clean up collection object
    if (addressCollection)
    {
        WdfObjectDelete(addressCollection);
    }

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HELPERS_REGISTRY, "%!FUNC! Exit");

    return status;
}

_Use_decl_annotations_
VOID
IPv6ToBleRegistryFlushWhiteListWorkItemEx(
    _In_     PVOID        IoObject,
    _In_opt_ PVOID        Context,
    _In_     PIO_WORKITEM IoWorkItem
)
/*++
Routine Description:

    A callback routine associated with a work item for a system worker thread.
    Does the actual calling of AssignWhiteList() at IRQL == PASSIVE_LEVEL, then
    frees the work item. The system worker thread is scheduled in the device
    timer callback.

Arguments:

    Parameter - the work item object previously allocated in the timer func.

Return Value:

    None.

--*/
{
    UNREFERENCED_PARAMETER(IoObject);   // The WDM device object, unused
    UNREFERENCED_PARAMETER(Context);    

    NTSTATUS status = STATUS_SUCCESS;
    

#if DBG
    KIRQL irql = KeGetCurrentIrql();
#endif // DBG

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HELPERS_REGISTRY, "%!FUNC! Entry");

    //
    // Step 1
    // Assign the runtime white list to the registry
    //

    // No need to check this for failure, as if it fails we'll try again 
    // later; plus, the function itself will log a trace error if it fails
    status = IPv6ToBleRegistryAssignRuntimeList(WHITE_LIST);

    // Reset the flag for next check if we succeeded
    if (NT_SUCCESS(status))
    {
        WdfSpinLockAcquire(gWhiteListModifiedLock);
        gWhiteListModified = FALSE;
        WdfSpinLockRelease(gWhiteListModifiedLock);
    }

    NT_ASSERT(irql == KeGetCurrentIrql());

    //
    // Step 2
    // Free the work item
    //
    IoFreeWorkItem(IoWorkItem);

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HELPERS_REGISTRY, "%!FUNC! Exit");
}

_Use_decl_annotations_
VOID
IPv6ToBleRegistryFlushMeshListWorkItemEx(
    _In_     PVOID        IoObject,
    _In_opt_ PVOID        Context,
    _In_     PIO_WORKITEM IoWorkItem
)
/*++
Routine Description:

    A callback routine associated with a work item for a system worker thread.
    Does the actual calling of AssignMeshList() at IRQL == PASSIVE_LEVEL, then
    frees the work item. The system worker thread is scheduled in the device
    timer callback.

Arguments:

    Parameter - the work item object previously allocated in the timer func.

Return Value:

    None.

--*/
{
    UNREFERENCED_PARAMETER(IoObject);   // the WDM device object, unused
    UNREFERENCED_PARAMETER(Context);

    NTSTATUS status = STATUS_SUCCESS;
    

#if DBG
    KIRQL irql = KeGetCurrentIrql();
#endif // DBG

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HELPERS_REGISTRY, "%!FUNC! Entry");

    //
    // Step 1
    // Assign the runtime mesh list to the registry
    //

    // No need to check this for failure, as if it fails we'll try again 
    // later; plus, the function itself will log a trace error if it fails
    status = IPv6ToBleRegistryAssignRuntimeList(MESH_LIST);

    // Reset the flag for next check if we succeeded
    if (NT_SUCCESS(status))
    {
        WdfSpinLockAcquire(gMeshListModifiedLock);
        gMeshListModified = FALSE;
        WdfSpinLockRelease(gMeshListModifiedLock);
    }

    NT_ASSERT(irql == KeGetCurrentIrql());

    //
    // Step 2
    // Free the work item
    //
    IoFreeWorkItem(IoWorkItem);

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HELPERS_REGISTRY, "%!FUNC! Exit");
}