using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IPv6ToBleDriverInterfaceForDesktop
{
    /// <summary>
    /// This class contains definitions for custom IOCTLs to communicate with
    /// IPv6ToBle.sys, the WFP callout driver for the IPv6 Over Bluetooth Low
    /// Energy Mesh project.
    /// </summary>
    class IPv6ToBleIoctl
    {
        /// <summary>
        /// Private constants for IOCTL definitions.
        /// </summary>
        private const int
            FILE_DEVICE_IPV6_TO_BLE = 0xDEDE,       // Driver device type
            METHOD_BUFFERED = 0,            // Buffered I/O
            FILE_ANY_ACCESS = 0;            // Read/write access

        /// <summary>
        /// Redefinition of the CTL_CODE macro from winioctl.h.
        /// For more information about the original macro, see
        /// https://docs.microsoft.com/windows-hardware/drivers/kernel/defining-i-o-control-codes
        /// This is a private method. If we were to allow client programs of 
        /// this library to define any IOCTL they wanted in a general way, this 
        /// would be public. But, we only make the nine IOCTLs for 
        /// IPV6OverBluetoothMesh public and construct them using this.
        /// </summary>
        private static int CTL_CODE(
            int DeviceType,
            int Function,
            int Method,
            int Access
        )
        {
            return (((DeviceType) << 16) | ((Access) << 14)
                | ((Function) << 2) | (Method));
        }

        /// <summary>
        /// Public IOCTL definitions as defined in Public.h of IPv6ToBle.sys.
        /// </summary>
        public static int IOCTL_IPV6_TO_BLE_LISTEN_NETWORK_V6 =
            CTL_CODE(
                FILE_DEVICE_IPV6_TO_BLE,
                0x8081,
                METHOD_BUFFERED,
                FILE_ANY_ACCESS
                );

        public static int IOCTL_IPV6_TO_BLE_INJECT_INBOUND_NETWORK_V6 =
            CTL_CODE(
                FILE_DEVICE_IPV6_TO_BLE,
                0x8082,
                METHOD_BUFFERED,
                FILE_ANY_ACCESS
                );

        public static int IOCTL_IPV6_TO_BLE_INJECT_OUTBOUND_NETWORK_V6 =
            CTL_CODE(
                FILE_DEVICE_IPV6_TO_BLE,
                0x8083,
                METHOD_BUFFERED,
                FILE_ANY_ACCESS
                );

        public static int IOCTL_IPV6_TO_BLE_ADD_TO_WHITE_LIST =
            CTL_CODE(
                FILE_DEVICE_IPV6_TO_BLE,
                0x8084,
                METHOD_BUFFERED,
                FILE_ANY_ACCESS
                );

        public static int IOCTL_IPV6_TO_BLE_REMOVE_FROM_WHITE_LIST =
            CTL_CODE(
                FILE_DEVICE_IPV6_TO_BLE,
                0x8085,
                METHOD_BUFFERED,
                FILE_ANY_ACCESS
                );

        public static int IOCTL_IPV6_TO_BLE_ADD_TO_MESH_LIST =
            CTL_CODE(
                FILE_DEVICE_IPV6_TO_BLE,
                0x8086,
                METHOD_BUFFERED,
                FILE_ANY_ACCESS
                );

        public static int IOCTL_IPV6_TO_BLE_REMOVE_FROM_MESH_LIST =
            CTL_CODE(
                FILE_DEVICE_IPV6_TO_BLE,
                0x8087,
                METHOD_BUFFERED,
                FILE_ANY_ACCESS
                );

        public static int IOCTL_IPV6_TO_BLE_PURGE_WHITE_LIST =
            CTL_CODE(
                FILE_DEVICE_IPV6_TO_BLE,
                0x8088,
                METHOD_BUFFERED,
                FILE_ANY_ACCESS
                );
        public static int IOCTL_IPV6_TO_BLE_PURGE_MESH_LIST =
            CTL_CODE(
                FILE_DEVICE_IPV6_TO_BLE,
                0x8089,
                METHOD_BUFFERED,
                FILE_ANY_ACCESS
                );
    }
}
