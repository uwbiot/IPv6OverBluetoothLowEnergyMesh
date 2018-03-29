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
// Global variables and objects
//-----------------------------------------------------------------------------

//
// Required objects for the callout driver in global space
//
WDFDEVICE wdfDeviceObject;		// Our main device object itself
PDEVICE_OBJECT wdmDeviceObject;	// WDM device object for registering callouts

WDFKEY parametersKey;			// The driver's framework registry key object
WDFKEY whiteListKey;			// Key of white list, child of parametersKey
WDFKEY meshListKey;				// Key of mesh list, child of parametersKey

UINT32 inboundIpPacketV6CalloutId;// Runtime IT, inbound IP packet v6 callout
UINT32 outboundIpPacketV6CalloutId; // Runtime ID,outbound IP packet v6 callout


HANDLE filterEngineHandle;		// Handle to the WFP filter engine
HANDLE injectionHandle;			// Handle for injecting packet at network layer

//-----------------------------------------------------------------------------
// WDFDRIVER Events
//-----------------------------------------------------------------------------
EXTERN_C_START

DRIVER_INITIALIZE DriverEntry;

EVT_WDF_DRIVER_UNLOAD IPv6ToBleEvtDriverUnload;

EXTERN_C_END

//-----------------------------------------------------------------------------
// Other defines, including memory pool tags (which are read in reverse)
//-----------------------------------------------------------------------------

#define IPV6_ADDRESS_LENGTH 16

#define IPV6_TO_BLE_NDIS_TAG		(UINT32)'TNBI'	// 'Ipv6 Ble Ndis Tag'
#define IPV6_TO_BLE_NBL_TAG			(UINT32)'BNBI'	// 'Ipv6 Ble Net Buffer'
#define IPV6_TO_BLE_WHITE_LIST_TAG	(UINT32)'LWBI'	// 'Ipv6 Ble White List'
#define IPV6_TO_BLE_MESH_LIST_TAG	(UINT32)'LMBI'	// 'Ipv6 Ble Mesh List'