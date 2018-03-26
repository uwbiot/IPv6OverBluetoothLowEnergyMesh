using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IPv6ToBleInteropLibrary
{
    using Microsoft.Win32.SafeHandles;
    using System.IO;

    // Namespace for Platform Invoke (PInvoke) interop services
    using System.Runtime.InteropServices;

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
    /// DeviceIoControl() uses thes nine IOCTLs defined in Public.h of the
    /// driver. It is also overloaded for either String or Byte[] data.
    /// 
    /// The first three are used by the background packet processing app, while
    /// the other six are used by the BluetoothMeshManager provisioning app.
    /// 
    /// IOCTL_IPV6_TO_BLE_LISTEN_NETWORK_V6      
    /// IOCTL_IPV6_TO_BLE_INJECT_INBOUND_NETWORK_V6    
    /// IOCTL_IPV6_TO_BLE_INJECT_OUTBOUND_NETWORK_V6   
    /// IOCTL_IPV6_TO_BLE_ADD_TO_WHITE_LIST            
    /// IOCTL_IPV6_TO_BLE_REMOVE_FROM_WHITE_LIST       
    /// IOCTL_IPV6_TO_BLE_ADD_TO_MESH_LIST            
    /// IOCTL_IPV6_TO_BLE_REMOVE_FROM_MESH_LIST         
    /// IOCTL_IPV6_TO_BLE_DESTROY_WHITE_LIST          
    /// IOCTL_IPV6_TO_BLE_DESTROY_MESH_LIST    
    /// 
    /// </summary>
    public class IPv6ToBleDriverInterface
    {
        /// <summary>
        /// Public constants for CreateFile(). These are selective 
        /// redefinitions from winioctl.h (only the ones we need).
        /// </summary>
        public const int
            INVALID_HANDLE_VALUE    = (-1),
            NULL                    = 0,
            ERROR_SUCCESS           = 0,
            GENERIC_READ            = unchecked((int)0x80000000),
            GENERIC_WRITE           = 0x40000000,
            FILE_SHARE_READ         = 0x00000001,
            OPEN_EXISTING           = 3,            // Device must exist to open
            FILE_FLAG_OVERLAPPED    = 0x40000000;   // Open for overlapped I/O  

        /// <summary>
        /// CreateFile(). Opens a handle to the driver.
        /// 
        /// For more information about the original function, see
        /// https://msdn.microsoft.com/library/windows/desktop/aa363858
        /// </summary>
        [DllImport("Kernel32.dll", CharSet = CharSet.Unicode, 
            SetLastError = true)]
        public static extern SafeFileHandle CreateFile(
            String          lpFileName,
            int             dwDesiredAccess,
            int             dwShareMode,
            IntPtr          lpSecurityAttributes,
            int             dwCreationDisposition,
            int             dwFlagsAndAttributes,
            SafeFileHandle  hTemplateFile
        );

        /// <summary>
        /// CloseHandle(). Closes the driver's handle.
        /// 
        /// For more information about the original function, see
        /// https://msdn.microsoft.com/library/windows/desktop/ms724211
        /// </summary>
        /// <param name="hHandle"></param>
        /// <returns></returns>
        [DllImport("Kernel32.dll", ExactSpelling = true, 
            CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool CloseHandle(
            SafeFileHandle  hHandle
        );

        /// <summary>
        /// Private constants for IOCTL definitions.
        /// </summary>
        private const int
            FILE_DEVICE_IPV6_TO_BLE = 0xDEDE,       // Driver device type
            METHOD_BUFFERED         = 0,            // Buffered I/O
            FILE_ANY_ACCESS         = 0;            // Read/write access

        /// <summary>
        /// Redefinition of the CTL_CODE macro from winioctl.h.
        /// For more information about the original macro, see
        /// https://docs.microsoft.com/windows-hardware/drivers/kernel/defining-i-o-control-codes
        /// If we were to allow client programs of this library to define
        /// any IOCTL they wanted in a general way, this would be public. But,
        /// we only make the nine IOCTLs for IPV6OverBluetoothMesh public and
        /// construct them using this.
        /// </summary>
        private static int CTL_CODE(
            int DeviceType,
            int Function,
            int Method,
            int Access
        )
        {
            return ( ((DeviceType) << 16) | ((Access) << 14) 
                | ((Function) << 2) | (Method) );
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

        public static int IOCTL_IPV6_TO_BLE_DESTROY_WHITE_LIST =
            CTL_CODE(
                FILE_DEVICE_IPV6_TO_BLE,
                0x8088,
                METHOD_BUFFERED,
                FILE_ANY_ACCESS
                );
        public static int IOCTL_IPV6_TO_BLE_DESTROY_MESH_LIST =
            CTL_CODE(
                FILE_DEVICE_IPV6_TO_BLE,
                0x8089,
                METHOD_BUFFERED,
                FILE_ANY_ACCESS
                );

        /// <summary>
        /// DeviceIoControl() overloaded for byte[] input/output buffer data.
        /// This version is used by the packet processing app to pass packets
        /// back and forth with the driver.
        /// 
        /// This version of DeviceIoControl() is to be used with these IOCTLs:
        /// 
        /// IOCTL_IPV6_TO_BLE_LISTEN_NETWORK_V6      
        /// IOCTL_IPV6_TO_BLE_INJECT_INBOUND_NETWORK_V6    
        /// IOCTL_IPV6_TO_BLE_INJECT_OUTBOUND_NETWORK_V6
        /// 
        /// For more information about this function, see
        /// https://msdn.microsoft.com/library/windows/desktop/aa363216.
        /// </summary>
        [DllImport("Kernel32.dll", SetLastError = true)]
        public static extern bool DeviceIoControl(
            SafeFileHandle  hDevice, 
            int             dwIoControlCode, 
            byte[]          lpInBuffer,
            int             nInBufferSize, 
            byte[]          lpOutBuffer, 
            int             nOutBufferSize, 
            out int         lpBytesReturned, 
            IntPtr          lpOverlapped
        );

        /// <summary>
        /// DeviceIoControl() overloaded for String input/output buffer data.
        /// This version is used by the packet processing app to pass packets
        /// back and forth with the driver.
        /// 
        /// This version of DeviceIoControl() is to be used with these IOCTLs:
        /// 
        /// IOCTL_IPV6_TO_BLE_ADD_TO_WHITE_LIST            
        /// IOCTL_IPV6_TO_BLE_REMOVE_FROM_WHITE_LIST       
        /// IOCTL_IPV6_TO_BLE_ADD_TO_MESH_LIST            
        /// IOCTL_IPV6_TO_BLE_REMOVE_FROM_MESH_LIST         
        /// IOCTL_IPV6_TO_BLE_DESTROY_WHITE_LIST          
        /// IOCTL_IPV6_TO_BLE_DESTROY_MESH_LIST 
        /// 
        /// Note: the latter two IOCTLs don't use the input or output buffers,
        /// but they use this version of this function just as a default.
        /// 
        /// For more information about this function, see
        /// https://msdn.microsoft.com/library/windows/desktop/aa363216.
        /// </summary>
        [DllImport("Kernel32.dll", CharSet = CharSet.Unicode, 
            SetLastError = true)]
        public static extern bool DeviceIoControl(
            SafeFileHandle  hDevice, 
            int             dwIoControlCode, 
            String          lpInBuffer,
            int             nInBufferSize, 
            String          lpOutBuffer, 
            int             nOutBufferSize, 
            out int         lpBytesReturned, 
            IntPtr          lpOverlapped
        );        
    }
}
