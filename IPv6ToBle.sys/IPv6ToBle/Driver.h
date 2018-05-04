/*++

Module Name:

    driver.h

Abstract:

    This file contains the definitions for the main driver logic. This includes
	the driver entry point, driver event callbacks, and global variables.

Environment:

    Kernel-mode Driver Framework

--*/

//-----------------------------------------------------------------------------
// This definition is CRUCIAL at compilation time. Leave uncommented to compile
// border router device-only code; comment out if compiling for the Pi/IoT Core
// node devices. 
//
// We split the code based on the role, not the version of the OS or chipset
// architecture. This permits flexibility with any future version of Windows or
// chipsets.
//-----------------------------------------------------------------------------

#define BORDER_ROUTER

//-----------------------------------------------------------------------------
// Custom structures
//-----------------------------------------------------------------------------

//
// This structure holds handles to the memory pools used to create
// NET_BUFFER_LIST and NET_BUFFER structures. NDIS uses special pools for
// performance reasons and so kernel executive memory is not fragmented.
//
typedef struct _NDIS_POOL_DATA {
    HANDLE	ndisHandle;		// NDIS_HANDLE
    HANDLE	nblPoolHandle;	// NDIS_HANDLE
    HANDLE	nbPoolHandle;	// NDIS_HANDLE
} NDIS_POOL_DATA, *PNDIS_POOL_DATA;

//
// Structures to contain entries for runtime versions of the lists: the list
// of trusted external devices (the white list) and the list of devices in the
// mesh network (the mesh list).
//
// These lists are only used on the border router device.
//
typedef struct _WHITE_LIST_ENTRY
{
    IN6_ADDR    ipv6Address;	// The IPv6 address
    ULONG       scopeId;        // The scope ID of the address
    LIST_ENTRY	listEntry;		// Links this list entry to the list
} WHITE_LIST_ENTRY, *PWHITE_LIST_ENTRY;

typedef struct _MESH_LIST_ENTRY
{
    IN6_ADDR    ipv6Address;	// The IPv6 address
    ULONG       scopeId;        // The scope ID of the address
    LIST_ENTRY	listEntry;		// Links this list entry to the list
} MESH_LIST_ENTRY, *PMESH_LIST_ENTRY;

//-----------------------------------------------------------------------------
// Global variables and objects (with a "g" prefix).
//
// Note: there are numerous global objects for two reasons: to keep things
// simpler, and because this is a software-only, non-PnP driver that only has 
// one control device object. For most drivers, data like this would be stored
// in the context space of whatever object they belong to (e.g. a device
// object).
//-----------------------------------------------------------------------------

//
// Required objects for the callout driver
//
WDFDRIVER gWdfDriverObject;         // The WDFDRIVER object for the driver
WDFDEVICE gWdfDeviceObject;		    // Our main device object itself
PDEVICE_OBJECT gWdmDeviceObject;	// WDM device object to go with above

WDFKEY gParametersKey;			    // Driver's framework registry key object
WDFKEY gWhiteListKey;			    // White list key, child of parametersKey
WDFKEY gMeshListKey;		        // Key of mesh list, child of parametersKey

//
// Objects for the WFP callouts
//
UINT32 gInboundIpPacketV6CalloutId;  // Runtime IT, inbound IP packet v6 callout
UINT32 gOutboundIpPacketV6CalloutId; // Runtime ID,outbound IP packet v6 callout

BOOLEAN gCalloutsRegistered;         // Tracker for whether callouts registered

HANDLE gFilterEngineHandle;	        // Handle to the WFP filter engine
HANDLE gInjectionHandleNetwork;     // Handle for injecting packets

//
// Objects for listening for packets
//
WDFQUEUE	gListenRequestQueue;    // Queue to listen for inbound IPv6 packets
WDFSPINLOCK gListenRequestQueueLock; // Lock to access listen request queue

//
// Objects for kernel mode network I/O
//
NDIS_POOL_DATA* gNdisPoolData;	    // NDIS memory pools (see Helpers_NDIS.h) 

//
// Objects for the runtime white list and mesh list
//
PLIST_ENTRY	gWhiteListHead;		    // Head of the white list
PLIST_ENTRY	gMeshListHead;		    // Head of the mesh list   

BOOLEAN gWhiteListModified;         // Tracker for whether white list changed
BOOLEAN gMeshListModified;          // Tracker for whether mesh list changed
WDFSPINLOCK gWhiteListModifiedLock; // Lock to check if white list changed
WDFSPINLOCK gMeshListModifiedLock;  // Lock to check if mesh list changed

//
// Periodic timer object
//
WDFTIMER gRegistryTimer;            // Timer to flush runtime lists to registry
                                    // periodically (if they changed), to avoid
                                    // data loss 

//-----------------------------------------------------------------------------
// WDFDRIVER Events
//-----------------------------------------------------------------------------
EXTERN_C_START

_IRQL_requires_(PASSIVE_LEVEL)
_IRQL_requires_same_
DRIVER_INITIALIZE DriverEntry;

_IRQL_requires_max_(PASSIVE_LEVEL)
_IRQL_requires_same_
EVT_WDF_DRIVER_UNLOAD IPv6ToBleEvtDriverUnload;

//-----------------------------------------------------------------------------
// Function to initialize global objects
//-----------------------------------------------------------------------------

_IRQL_requires_max_(PASSIVE_LEVEL)
_IRQL_requires_same_
NTSTATUS
IPv6ToBleDriverInitGlobalObjects();

//-----------------------------------------------------------------------------
// Functions for periodic timer to flush runtime lists to the registry
//-----------------------------------------------------------------------------

_IRQL_requires_max_(PASSIVE_LEVEL)
_IRQL_requires_same_
NTSTATUS
IPv6ToBleDriverInitTimer();

_IRQL_requires_max_(DISPATCH_LEVEL)
_IRQL_requires_same_
EVT_WDF_TIMER IPv6ToBleTimerCheckAndFlushLists;

EXTERN_C_END

//-----------------------------------------------------------------------------
// Other defines, including memory pool tags (which are read in reverse)
//-----------------------------------------------------------------------------

#define IPV6_ADDRESS_LENGTH 16

#define IPV6_TO_BLE_NDIS_TAG		(UINT32)'TNBI'	// 'Ipv6 Ble Ndis Tag'
#define IPV6_TO_BLE_NBL_TAG			(UINT32)'BNBI'	// 'Ipv6 Ble Net Buffer'
#define IPV6_TO_BLE_WHITE_LIST_TAG	(UINT32)'LWBI'	// 'Ipv6 Ble White List'
#define IPV6_TO_BLE_MESH_LIST_TAG	(UINT32)'LMBI'	// 'Ipv6 Ble Mesh List'