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
IPv6ToBleRegistryOpenWhiteListKey()
/*++
Routine Description:

	Opens the white list registry key or creates it if it does not exist. The
	key is opened with all permissions, enabling the caller to either read or
	write the key.

    This function can be called at PASSIVE_LEVEL from Driver Entry.

    This function can also be called from the RegistryAssignWhiteList function,
    which is called from the periodic timer callback at DISPATCH_LEVEL.

Arguments:

	ParametersKey - the driver's registry key assigned to it at driver object
	creation time. This is just a reference to a global variable, but including
	it as an input parameter makes things clearer.

	WhiteListKey - the registry key for the white list. This key is a child of
	the driver's main parameters key.

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
	// DriverEntry.
	status = WdfRegistryCreateKey(parametersKey,
		&whiteListKeyName,
		KEY_ALL_ACCESS,
		REG_OPTION_NON_VOLATILE,
		NULL,
		WDF_NO_OBJECT_ATTRIBUTES,
		&whiteListKey
	);
	if (!NT_SUCCESS(status))
	{
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_HELPERS_REGISTRY, "Opening white list registry key failed %!STATUS!", status);
		goto Exit;
	}

Exit:

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HELPERS_REGISTRY, "%!FUNC! Exit");

	return status;
}

_Use_decl_annotations_
NTSTATUS
IPv6ToBleRegistryOpenMeshListKey()
/*++
Routine Description:

	Opens the registry key for the mesh list or creates the key if it does not.

    This function can be called at PASSIVE_LEVEL from Driver Entry.

    This function can also be called from the RegistryAssignWhiteList function,
    which is called from the periodic timer callback at DISPATCH_LEVEL.

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
	// DriverEntry.
	status = WdfRegistryCreateKey(parametersKey,
		&meshListKeyName,
		KEY_ALL_ACCESS,
		REG_OPTION_NON_VOLATILE,
		NULL,
		WDF_NO_OBJECT_ATTRIBUTES,
		&meshListKey
	);
	if (!NT_SUCCESS(status))
	{
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_HELPERS_REGISTRY, "Opening mesh list registry key failed %!STATUS!", status);
        goto Exit;
    }

Exit:

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HELPERS_REGISTRY, "%!FUNC! Exit");

	return status;
}

_Use_decl_annotations_
NTSTATUS
IPv6ToBleRegistryRetrieveWhiteList()
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

	WDFCOLLECTION whiteListAddresses = NULL;
	WDF_OBJECT_ATTRIBUTES addressStringsAttributes;

	ULONG i;
	ULONG count;
    BOOLEAN keyOpened = FALSE;
	IN6_ADDR ipv6AddressStorage;	// struct to store an IPv6 address

	PIPV6_TO_BLE_DEVICE_CONTEXT deviceContext;

	//
	// Step 1
	// Retrieve the white list key. It is a REG_MULTI_SZ key, or multi strings
	// key. Assign its string contents to the runtime context for the device.
	// 
	// NOTE: If this operation fails for the white list, we skip to the mesh
	// list since there may be something there even if the white list is empty.
	//

	// Declare the name of the value we're querying from the key
	DECLARE_CONST_UNICODE_STRING(whiteListValueName, L"WhiteList");

	// Create a collection to store the retrieved white list addresses
	status = WdfCollectionCreate(WDF_NO_OBJECT_ATTRIBUTES,
		                         &whiteListAddresses
	                             );
	if (!NT_SUCCESS(status))
	{
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_HELPERS_REGISTRY, "WDFCOLLECTION creation failed during %!FUNC! with %!STATUS!", status);
		goto Exit;
	}

	// Set the collection to be the parent of the retrieved string objects
	WDF_OBJECT_ATTRIBUTES_INIT(&addressStringsAttributes);
	addressStringsAttributes.ParentObject = whiteListAddresses;

	// Open the key	
	status = IPv6ToBleRegistryOpenWhiteListKey();
	if (!NT_SUCCESS(status))
	{
		goto Exit;
	}
	keyOpened = TRUE;

	// Query the white list key. Fails first time driver is installed or if the
	// user purged the list and rebooted because the key exists but is empty.
	status = WdfRegistryQueryMultiString(whiteListKey,
		                                &whiteListValueName,
		                                &addressStringsAttributes,
		                                whiteListAddresses
	                                    );
	if (!NT_SUCCESS(status))
	{
		// If the key is empty, status will be STATUS_RESOURCE_DATA_NOT_FOUND.
        TraceEvents(TRACE_LEVEL_WARNING, TRACE_HELPERS_REGISTRY, "Querying white list failed because it was empty %!STATUS!", status);
		goto Exit;
	}

	//
	// Step 2
	// Set the white list key's contents to the runtime context. We don't have 
	// to worry about synchronizing access to the lists because this function 
	// is either called from DriverEntry, or later from the context of the 
	// EvtDeviceIoControl callback, and access to those is synchronized by the
	// framework.
	//

	// Get the context
	deviceContext = IPv6ToBleGetContextFromDevice(wdfDeviceObject);

	// Since the list is non-empty, we can walk the list, get the strings, and
	// assign them to the context. No need for synchronization because this
	// function is called from DriverEntry, which is synchronized.
	count = WdfCollectionGetCount(whiteListAddresses);
	for (i = 0; i < count; i++)
	{

		// Get the string from the collection retrieved from the registry
		DECLARE_UNICODE_STRING_SIZE(currentIpv6Address, INET6_ADDRSTRLEN);
		WDFSTRING currentWdfString = (WDFSTRING)WdfCollectionGetItem(
			whiteListAddresses,
			i
		);
		WdfStringGetUnicodeString(currentWdfString, &currentIpv6Address);

        // Convert the string to its 16-byte value
        UINT32 conversionStatus = IPv6ToBleIPAddressV6StringToValue(
            currentIpv6Address.Buffer,
            (UCHAR*)&ipv6AddressStorage.u.Byte[0]
        );

        // Create the list entry and add it
        if (conversionStatus == NO_ERROR)
		{

			// Using non-paged pool because the list may be accessed at
            // IRQL = DISPATCH_LEVEL
			PWHITE_LIST_ENTRY entry = (PWHITE_LIST_ENTRY)ExAllocatePoolWithTag(
				NonPagedPool,
				sizeof(WHITE_LIST_ENTRY),
				IPV6_TO_BLE_WHITE_LIST_TAG
			);
			if (!entry)
			{
				status = STATUS_INSUFFICIENT_RESOURCES;
                TraceEvents(TRACE_LEVEL_ERROR, TRACE_HELPERS_REGISTRY, "Allocating white list entry for address retrieved from registry failed %!STATUS!", status);
				goto Exit;
			}

			// Add the entry to the list
			InsertHeadList(deviceContext->whiteListHead, entry->listEntry);

			// Insert the address into the entry
			entry->ipv6Address = (UINT8*)(ipv6AddressStorage.u.Byte[0]);
		}
        else
        {
            goto Exit;
        }

		// Zero out the storage structure for next time
		RtlZeroMemory(&ipv6AddressStorage, sizeof(IN6_ADDR));
	}

Exit:
    // Clean up any memory allocated if we failed at some point
    if (!NT_SUCCESS(status))
    {
        IPv6ToBleRuntimeListDestroyWhiteList();
    }

    // Close the key
    if (keyOpened)
    {
        WdfRegistryClose(whiteListKey);
    }

    // Clean up the collection object and its children
    if (whiteListAddresses)
    {
        WdfObjectDelete(whiteListAddresses);
    }

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HELPERS_REGISTRY, "%!FUNC! Exit");

    return status;
}

_Use_decl_annotations_
NTSTATUS
IPv6ToBleRegistryRetrieveMeshList()
/*++
Routine Description:

	Loads information about the trusted external device mesh list. If it has
	values in it, assign them to the runtime context so we don't have to keep
	accessing the registry.

    This function is only called from Driver Entry so it should be called at
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

	WDFCOLLECTION meshListAddresses = NULL;
	WDF_OBJECT_ATTRIBUTES addressStringsAttributes;

	ULONG i;
	ULONG count;
    BOOLEAN keyOpened = FALSE;
	IN6_ADDR ipv6AddressStorage;	// struct to store an IPv6 address

	PIPV6_TO_BLE_DEVICE_CONTEXT deviceContext;

    // Declare the name of the value we're querying from the key
    DECLARE_CONST_UNICODE_STRING(meshListValueName, L"MeshList");

	// Create a collection to store the retrieved mesh list addresses
	status = WdfCollectionCreate(WDF_NO_OBJECT_ATTRIBUTES, &meshListAddresses);
	if (!NT_SUCCESS(status))
	{
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_HELPERS_REGISTRY, "WDFCOLLECTION creation failed during %!FUNC! with %!STATUS!", status);
		goto Exit;
	}

	// Set the collection to be the parent of the retrieved string objects
	WDF_OBJECT_ATTRIBUTES_INIT(&addressStringsAttributes);
	addressStringsAttributes.ParentObject = meshListAddresses;

	// Open the key	
	status = IPv6ToBleRegistryOpenMeshListKey();
	if (!NT_SUCCESS(status))
	{
		goto Exit;
	}
	keyOpened = TRUE;

	// Query the white list key. Fails first time driver is installed or if the
	// user purged the list and rebooted because the key exists but is empty.
	status = WdfRegistryQueryMultiString(meshListKey,
		&meshListValueName,
		&addressStringsAttributes,
		meshListAddresses
	);
	if (!NT_SUCCESS(status))
	{
		// If the key is empty, status will be STATUS_RESOURCE_DATA_NOT_FOUND.
        TraceEvents(TRACE_LEVEL_WARNING, TRACE_HELPERS_REGISTRY, "Querying mesh list failed because it was empty %!STATUS!", status);
		goto Exit;
	}

	// Get the device context
	deviceContext = IPv6ToBleGetContextFromDevice(wdfDeviceObject);

	// Since the list is non-empty, we can walk the list, get the strings, and
	// assign them to the context. No need for synchronization because this
	// function is called from DriverEntry, which is synchronized.
	count = WdfCollectionGetCount(meshListAddresses);
	for (i = 0; i < count; i++)
	{

		// Get the string from the collection retrieved from the registry
		DECLARE_UNICODE_STRING_SIZE(currentIpv6Address, INET6_ADDRSTRLEN);
		WDFSTRING currentWdfString = (WDFSTRING)WdfCollectionGetItem(
			meshListAddresses,
			i
		);
		WdfStringGetUnicodeString(currentWdfString, &currentIpv6Address);

		// Convert the string to its 16-byte value
        UINT32 conversionStatus = IPv6ToBleIPAddressV6StringToValue(
            currentIpv6Address.Buffer,
            &ipv6AddressStorage.u.Byte[0]
        );

		// Create the list entry and add it
		if (conversionStatus == NO_ERROR)
		{
            // Using non-paged pool because the list may be accessed at
            // IRQL = DISPATCH_LEVEL
			PMESH_LIST_ENTRY entry = (PMESH_LIST_ENTRY)ExAllocatePoolWithTag(
				NonPagedPool,
				sizeof(MESH_LIST_ENTRY),
				IPV6_TO_BLE_MESH_LIST_TAG
			);
			if (!entry)
			{
				status = STATUS_INSUFFICIENT_RESOURCES;
                TraceEvents(TRACE_LEVEL_ERROR, TRACE_HELPERS_REGISTRY, "Allocating mesh list entry for address retrieved from registry failed %!STATUS!", status);
				goto Exit;
			}

			// Add the entry to the list
			InsertHeadList(deviceContext->meshListHead, entry->listEntry);

			// Insert the address into the entry
			entry->ipv6Address = (UINT8*)(ipv6AddressStorage.u.Byte[0]);
        }
        else
        {
            goto Exit;
        }

		// Zero out the storage structure for next time
		RtlZeroMemory(&ipv6AddressStorage, sizeof(IN6_ADDR));
	}

Exit:
	// Clean up any memory allocated if we failed at some point
	if (!NT_SUCCESS(status))
	{
		IPv6ToBleRuntimeListDestroyMeshList();
	}

    // Close the key
    if (keyOpened)
    {
        WdfRegistryClose(meshListKey);
    }

    // Clean up the collection object and its children
    if (meshListAddresses)
    {
        WdfObjectDelete(meshListAddresses);
    }

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HELPERS_REGISTRY, "%!FUNC! Exit");

	return status;
}

_Use_decl_annotations_
NTSTATUS
IPv6ToBleRegistryAssignWhiteList()
/*++
Routine Description:

    Assigns the runtime white list to the registry and overwrites what is
    there. Converts the byte values used during runtime back to string forms
    for storage.

    This function is called from the periodic timer callback function, which
    is called at DISPATCH_LEVEL.

Arguments:

    None. Accesses global variables defined in Driver.h.

Return Value:

    STATUS_SUCCESS if successful; appropriate NTSTATUS error codes otherwise.

--*/
{
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HELPERS_REGISTRY, "%!FUNC! Entry");

    NTSTATUS status = STATUS_SUCCESS;
    BOOLEAN keyOpened = FALSE;  
    WDFCOLLECTION addressCollection = { 0 };
    WDF_OBJECT_ATTRIBUTES attributes;
    IN6_ADDR addressStorage = { 0 };

    // Get the device context
    PIPV6_TO_BLE_DEVICE_CONTEXT deviceContext = IPv6ToBleGetContextFromDevice(
                                                    wdfDeviceObject
                                                );

    //
    // Step 1
    // Check for empty list (counts as success)
    //
    if (IsListEmpty(deviceContext->whiteListHead))
    {
        TraceEvents(TRACE_LEVEL_WARNING, TRACE_HELPERS_REGISTRY, "White list is empty - nothing to write to registry %!STATUS!", status);
        goto Exit;
    }

    //
    // Step 2
    // Open the key
    //
    status = IPv6ToBleRegistryOpenWhiteListKey();
    if (!NT_SUCCESS(status))
    {
        goto Exit;
    }
    keyOpened = TRUE;

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
    PLIST_ENTRY entry = deviceContext->whiteListHead->Flink;
    while (entry != deviceContext->whiteListHead)
    {
        // Get the struct that contains this entry
        PWHITE_LIST_ENTRY whiteListEntry = CONTAINING_RECORD(entry, 
                                                             WHITE_LIST_ENTRY,
                                                             listEntry
                                                             );

        // Retrieve the IPv6 address in byte form and store it in the struct
        RtlCopyMemory(addressStorage.u.Byte, 
                      &whiteListEntry->ipv6Address, 
                      IPV6_ADDRESS_LENGTH
                      );

        // Declare a unicode string to hold the to-be converted address
        DECLARE_UNICODE_STRING_SIZE(currentIpv6AddressString, 
                                   INET6_ADDRSTRLEN
                                   );

        // Convert the address to a string
        RtlIpv6AddressToStringW(&addressStorage, 
                                currentIpv6AddressString.Buffer
                                );

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

        // Zero the memory of the address storage structure for next pass
        RtlZeroMemory(&addressStorage, sizeof(IN6_ADDR));
    }

    //
    // Step 5
    // Assign the collection of string objects to the registry key
    //

    // Declare the name of the value we're assigning to the key
    DECLARE_CONST_UNICODE_STRING(whiteListValueName, L"WhiteList");

    // Assign the collection of strings to the registry key's value
    status = WdfRegistryAssignMultiString(whiteListKey, 
                                          &whiteListValueName, 
                                          addressCollection
                                          );
    if (!NT_SUCCESS(status))
    {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_HELPERS_REGISTRY, "WdfRegistryAssignMultiString failed during %!FUNC! with %!STATUS!", status);
    }

Exit:

    // Close the key if it were opened
    if (keyOpened)
    {
        WdfRegistryClose(whiteListKey);
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
NTSTATUS
IPv6ToBleRegistryAssignMeshList()
/*++
Routine Description:

    Assigns the runtime mesh list to the registry and overwrites what is
    there. Converts the byte values used during runtime back to string forms
    for storage.

    This function is called from the periodic timer callback function, which
    is called at DISPATCH_LEVEL.

Arguments:

    None. Accesses global variables defined in Driver.h.

Return Value:

    STATUS_SUCCESS if successful; appropriate NTSTATUS error codes otherwise.

--*/
{
    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HELPERS_REGISTRY, "%!FUNC! Entry");

    NTSTATUS status = STATUS_SUCCESS;
    BOOLEAN keyOpened = FALSE;
    WDFCOLLECTION addressCollection = { 0 };
    WDF_OBJECT_ATTRIBUTES attributes;
    IN6_ADDR addressStorage;

    // Get the device context
    PIPV6_TO_BLE_DEVICE_CONTEXT deviceContext = IPv6ToBleGetContextFromDevice(
                                                    wdfDeviceObject
                                                );

    //
    // Step 1
    // Check for empty list (counts as success)
    //
    if (IsListEmpty(deviceContext->meshListHead))
    {
        TraceEvents(TRACE_LEVEL_WARNING, TRACE_HELPERS_REGISTRY, "Mesh list is empty - nothing to write to registry %!STATUS!", status);
        goto Exit;
    }

    //
    // Step 2
    // Open the key
    //
    status = IPv6ToBleRegistryOpenMeshListKey();
    if (!NT_SUCCESS(status))
    {
        goto Exit;
    }
    keyOpened = TRUE;

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
    PLIST_ENTRY entry = deviceContext->meshListHead->Flink;
    while (entry != deviceContext->meshListHead)
    {
        // Get the struct that contains this entry
        PMESH_LIST_ENTRY meshListEntry = CONTAINING_RECORD(entry,
                                                           MESH_LIST_ENTRY,
                                                           listEntry
                                                           );

        // Retrieve the IPv6 address in byte form and store it in the struct
        RtlCopyMemory(addressStorage.u.Byte,
                      &meshListEntry->ipv6Address,
                      IPV6_ADDRESS_LENGTH
                      );

        // Declare a unicode string to hold the to-be converted address
        DECLARE_UNICODE_STRING_SIZE(currentIpv6AddressString,
                                    INET6_ADDRSTRLEN
                                    );

        // Convert the address to a string
        RtlIpv6AddressToStringW(&addressStorage,
                                currentIpv6AddressString.Buffer
                                );
        if (!NT_SUCCESS(status))
        {
            TraceEvents(TRACE_LEVEL_ERROR, TRACE_HELPERS_REGISTRY, "Converting IPv6 address to string failed during %!FUNC! with %!STATUS!", status);
            goto Exit;
        }

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

    //
    // Step 5
    // Assign the collection of string objects to the registry key
    //

    // Declare the name of the value we're assigning to the key
    DECLARE_CONST_UNICODE_STRING(meshListValueName, L"MeshList");

    // Assign the collection of strings to the registry key's value
    status = WdfRegistryAssignMultiString(meshListKey,
                                          &meshListValueName,
                                          addressCollection
                                          );
    if (!NT_SUCCESS(status))
    {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_HELPERS_REGISTRY, "WdfRegistryAssignMultiString failed during %!FUNC! with %!STATUS!", status);
    }

Exit:

    // Close the key if it were opened
    if (keyOpened)
    {
        WdfRegistryClose(meshListKey);
    }

    // Clean up collection object
    if (addressCollection)
    {
        WdfObjectDelete(addressCollection);
    }

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HELPERS_REGISTRY, "%!FUNC! Exit");

    return status;
}