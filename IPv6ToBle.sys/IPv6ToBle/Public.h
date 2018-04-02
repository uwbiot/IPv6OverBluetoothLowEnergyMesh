/*++

Module Name:

    public.h

Abstract:

    This module contains the common declarations shared by driver
    and user applications.

Environment:

    user and kernel

--*/

//-----------------------------------------------------------------------------
// Arbitrary code for device type to use with custom IOCTL definitions
//-----------------------------------------------------------------------------

#define FILE_DEVICE_IPV6_TO_BLE 0xDEDE	// A reference to King DeDeDe

//-----------------------------------------------------------------------------
// Define IOCTL device control codes to use for commands to and from the
// usermode app. Second argument (FunctionCode) is arbitrarily chosen.
//-----------------------------------------------------------------------------

//
// First IOCTL: Listen for incoming or outgoing IPv6 packets to send up to the
// usermode app and redirect OUT over Bluetooth Low Energy.
//
// Used on the border router device and the IoT core devices.
//
// Sent by the packet processing background app.
//

#define IOCTL_IPV6_TO_BLE_LISTEN_NETWORK_V6 CTL_CODE(FILE_DEVICE_IPV6_TO_BLE, 0x8081, METHOD_BUFFERED, FILE_ANY_ACCESS)

//
// Second IOCTL: Inject a given IPv6 packet into the inbound data path.
//
// Used on both the border router AND the Pi/IoT device.
//
// Sent by the packet processing background app.
//
#define IOCTL_IPV6_TO_BLE_INJECT_INBOUND_NETWORK_V6 CTL_CODE(FILE_DEVICE_IPV6_TO_BLE, 0x8082, METHOD_BUFFERED, FILE_ANY_ACCESS)

//
// Third IOCTL: Inject a given IPv6 packet into the outbound data path.
//
// Used on the border router device.
//
// Sent by the packet processing background app.
//
#define IOCTL_IPV6_TO_BLE_INJECT_OUTBOUND_NETWORK_V6 CTL_CODE(FILE_DEVICE_IPV6_TO_BLE, 0x8083, METHOD_BUFFERED, FILE_ANY_ACCESS)

//
// Fourth IOCTL: Add to the white list of trusted external IPv6 addresses in the registry
//
// Used on the border router device.
//
// Sent by the provisioning manager app.
//
#define IOCTL_IPV6_TO_BLE_ADD_TO_WHITE_LIST CTL_CODE(FILE_DEVICE_IPV6_TO_BLE, 0x8084, METHOD_BUFFERED, FILE_ANY_ACCESS)

//
// Fifth IOCTL: Remove from the white list of trusted external IPv6 addresses in the registry
//
// Used on the border router device.
//
// Sent by the provisioning manager app.
//
#define IOCTL_IPV6_TO_BLE_REMOVE_FROM_WHITE_LIST CTL_CODE(FILE_DEVICE_IPV6_TO_BLE, 0x8085, METHOD_BUFFERED, FILE_ANY_ACCESS)

//
// Sixth IOCTL: Add to the list of internal mesh IPv6 addresses in the registry
//
// Used on the border router device.
//
// Sent by the provisioning manager app.
//
#define IOCTL_IPV6_TO_BLE_ADD_TO_MESH_LIST CTL_CODE(FILE_DEVICE_IPV6_TO_BLE, 0x8086, METHOD_BUFFERED, FILE_ANY_ACCESS)

//
// Seventh IOCTL: Remove from the white list of internal mesh IPv6 addresses in the registry
//
// Used on the border router device.
//
// Sent by the provisioning manager app.
//
#define IOCTL_IPV6_TO_BLE_REMOVE_FROM_MESH_LIST CTL_CODE(FILE_DEVICE_IPV6_TO_BLE, 0x8087, METHOD_BUFFERED, FILE_ANY_ACCESS)

//
// Eighth IOCTL: Purge the white list, both runtime list and registry.
// 
// Used on the border router device.
//
// Sent by the provisioning manager app.
//
#define IOCTL_IPV6_TO_BLE_PURGE_WHITE_LIST CTL_CODE(FILE_DEVICE_IPV6_TO_BLE, 0x8088, METHOD_BUFFERED, FILE_ANY_ACCESS)

//
// Ninth IOCTL: Purge the mesh list, both runtime list and registry.
// 
// Used on the border router device.
//
// Sent by the provisioning manager app.
//
#define IOCTL_IPV6_TO_BLE_PURGE_MESH_LIST CTL_CODE(FILE_DEVICE_IPV6_TO_BLE, 0x8089, METHOD_BUFFERED, FILE_ANY_ACCESS)
