/*++

Module Name:

    device.h

Abstract:

    This file contains the device definitions. This driver is a non-PnP driver,
	so it only has one control device and no Device Add callback.

Environment:

    Kernel-mode Driver Framework

--*/

#include "Public.h"
#include "Helpers_NDIS.h"

EXTERN_C_START

//-----------------------------------------------------------------------------
// Structures to contain entries for runtime versions of the lists: the list
// of trusted external devices (the white list) and the list of devices in the
// mesh network (the mesh list).
//
// These lists are only used on the gateway device.
//-----------------------------------------------------------------------------

typedef struct _WHITE_LIST_ENTRY
{
	UINT8*		ipv6Address;	// The IPv6 address
	PLIST_ENTRY	listEntry;		// Links this list entry to the list
} WHITE_LIST_ENTRY, *PWHITE_LIST_ENTRY;

typedef struct _MESH_LIST_ENTRY
{
	UINT8*		ipv6Address;	// The IPv6 address
	PLIST_ENTRY	listEntry;		// Links this list entry to the list
} MESH_LIST_ENTRY, *PMESH_LIST_ENTRY;

//-----------------------------------------------------------------------------
// A device context for holding implementation-specific data. A context is
// essentially runtime storage space for the driver to hold independent of any
// other threads, processes, etc.
//
// Note: Spin locks automatically raise the thread to DISPATCH_LEVEL when they
// are acquired; therefore, they must be released as soon as possible.
//-----------------------------------------------------------------------------

typedef struct _IPV6_TO_BLE_DEVICE_CONTEXT
{	
	WDFQUEUE	listenRequestQueue;	// Queue to listen for inbound IPv6 packets
    WDFSPINLOCK listenRequestQueueLock; // Lock to access listen request queue

    NDIS_POOL_DATA* ndisPoolData;	// NDIS memory pools (see Helpers_NDIS.h) 

	PLIST_ENTRY	whiteListHead;		// Head of the white list
	PLIST_ENTRY	meshListHead;		// Head of the mesh list
    
    BOOLEAN calloutsRegistered;     // Tracker for whether callouts registered

    BOOLEAN whiteListModified;      // Tracker for whether white list changed
    BOOLEAN meshListModified;       // Tracker for whether mesh list changed
    WDFSPINLOCK whiteListModifiedLock;  // Lock to check if white list changed
    WDFSPINLOCK meshListModifiedLock;   // Lock to check if mesh list changed

    WDFTIMER registryTimer;         // Timer to flush runtime lists to registry
                                    // periodically (if they changed), to avoid
                                    // data loss  

} IPV6_TO_BLE_DEVICE_CONTEXT, *PIPV6_TO_BLE_DEVICE_CONTEXT;

// This macro will generate an inline function called 
// IPv6ToBleGetContextFromDevice which will be used to get a pointer to the
// device context memory in a type safe manner.
WDF_DECLARE_CONTEXT_TYPE_WITH_NAME(IPV6_TO_BLE_DEVICE_CONTEXT,
								   IPv6ToBleGetContextFromDevice
								   )

//-----------------------------------------------------------------------------
// Function to initialize the device and its callbacks
//-----------------------------------------------------------------------------

_IRQL_requires_(PASSIVE_LEVEL)
_IRQL_requires_same_
_Check_return_
_Success_(return == STATUS_SUCCESS)
NTSTATUS
IPv6ToBleControlDeviceCreate(
    _In_	WDFDRIVER		Driver
);

//-----------------------------------------------------------------------------
// Event callback to clean up any memory allocated for objects in the device
// context space when the device is unloaded.
//-----------------------------------------------------------------------------

EVT_WDF_DEVICE_CONTEXT_CLEANUP IPv6ToBleDeviceCleanup;

//-----------------------------------------------------------------------------
// Timer event callback function to flush the runtime lists to the registry
//-----------------------------------------------------------------------------

EVT_WDF_TIMER IPv6ToBleDeviceTimerCheckAndFlushLists;

EXTERN_C_END
