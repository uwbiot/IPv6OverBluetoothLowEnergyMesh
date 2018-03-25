using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// Namespace for Platform Invoke (PInvoke) interop services
using System.Runtime.InteropServices;

namespace IPv6ToBleInteropLibrary
{
    /// <summary>
    /// This class contains constants and functions to interface with the WFP
    /// callout driver (IPv6ToBle.sys).
    /// 
    /// These three functions are required for interfacing with the driver:
    /// 
    /// CreateFile()        Opens a handle to the driver
    /// CloseHandle()       Closes the handle to the driver
    /// DeviceIoControl()   Sends an I/O control code (IOCTL)
    /// 
    /// CreateFile() uses this device type code defined in Public.h of the
    /// driver (IPv6ToBle.sys):
    /// 
    /// FILE_DEVICE_IPV6_TO_BLE                         0xDEDE
    /// 
    /// DeviceIoControl() is overloaded for each required IOCTL. These nine
    /// IOCTLs are defined in Public.h of the driver.
    /// 
    /// The first three are used by the background packet processing app, while
    /// the other six are used by the BluetoothMeshManager provisioning app.
    /// 
    /// IOCTL_IPV6_TO_BLE_LISTEN_NETWORK_V6             0x8081
    /// IOCTL_IPV6_TO_BLE_INJECT_INBOUND_NETWORK_V6     0x8082
    /// IOCTL_IPV6_TO_BLE_INJECT_OUTBOUND_NETWORK_V6    0x8083 
    /// IOCTL_IPV6_TO_BLE_ADD_TO_WHITE_LIST             0x8084
    /// IOCTL_IPV6_TO_BLE_REMOVE_FROM_WHITE_LIST        0x8085
    /// IOCTL_IPV6_TO_BLE_ADD_TO_MESH_LIST              0x8086
    /// IOCTL_IPV6_TO_BLE_REMOVE_FROM_MESH_LIST         0x8087
    /// IOCTL_IPV6_TO_BLE_DESTROY_WHITE_LIST            0x8088
    /// IOCTL_IPV6_TO_BLE_DESTROY_MESH_LIST             0x8089
    /// 
    /// 
    /// 
    /// </summary>
    public class DriverInterfaceDefinitions
    {
        //
        // Constants for CreateFile() and IOCTL definitions. These are 
        // selective redefinitions from winioctl.h (only the ones we need).
        //
        public const int
            INVALID_HANDLE_VALUE    = (-1),
            NULL                    = 0,
            ERROR_SUCCESS           = 0,
            GENERIC_READ            = unchecked((int)0x80000000),
            GENERIC_WRITE           = 0x40000000,
            FILE_SHARE_READ         = 0x00000001,
            OPEN_EXISTING           = 3,            // Device must exist to open
            FILE_FLAG_OVERLAPPED    = 0x40000000,   // Open for overlapped I/O
            METHOD_BUFFERED         = 0,            // Buffered I/O
            FILE_ANY_ACCESS         = 0;
    }
}
