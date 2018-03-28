using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// IPv6ToBle Interop Library and related namespaces
using IPv6ToBleInteropLibrary;

using Microsoft.Win32.SafeHandles;      // Safe file handles
using System.Threading;                 // Asynchronous I/O and thread pool
using System.Runtime.InteropServices;   // Marhsalling interop calls

namespace IPv6ToBleMeshManager
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            this.InitializeComponent();
        }

        /// <summary>
        /// Sends the IOCTL_IPV6_TO_BLE_LISTEN_NETWORK_V6 to the driver.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private unsafe void Button_1_Listen_Click(object sender, RoutedEventArgs e)
        {
            //
            // Step 1
            // Open the handle to the driver with Overlapped async I/O flagged
            //
            SafeFileHandle driverHandle = IPv6ToBleDriverInterface.CreateFile(
                "\\\\.\\IPv6ToBle",
                IPv6ToBleDriverInterface.GENERIC_READ | IPv6ToBleDriverInterface.GENERIC_WRITE,
                IPv6ToBleDriverInterface.FILE_SHARE_READ | IPv6ToBleDriverInterface.FILE_SHARE_WRITE,
                IntPtr.Zero,
                IPv6ToBleDriverInterface.OPEN_EXISTING,
                IPv6ToBleDriverInterface.FILE_FLAG_OVERLAPPED,  // async
                IntPtr.Zero
            );
            
            if(driverHandle.IsInvalid)
            {
                Console.Error.WriteLine("Could not open handle to driver.\n");
                throw new FileNotFoundException();
            }

            //
            // Step 2
            // Prepare for asynchronous I/O
            //

            // Bind the driver handle to the Windows Thread Pool. Really, we're
            // binding the handle to an I/O completion port owned by the thread
            // pool.
            bool handleBound = ThreadPool.BindHandle(driverHandle);
            if(!handleBound)
            {
                Console.Error.WriteLine("Could not bind driver handle to a" +
                    "thread pool I/O completion port.\n");
            }


            // Create a NativeOverlapped pointer to pass to DeviceIoControl().
            // Using NULL for the second parameter in Pack() because we pass
            // data in the DeviceIoControl parameters instead.
            Overlapped overlapped = new Overlapped();
            NativeOverlapped* nativeOverlapped = overlapped.Pack(IPv6ToBleListenCompletionCallback, null);

            //
            // Step 3
            // Send the IOCTL to request the driver to listen for a packet and
            // give us the packet through the request's output buffer
            //
            // Note about asynchronous DeviceIoControl: when using overlapped
            // I/O, the call immediately returns with a boolean result. If
            // the result is true, the operation completed synchronously. If
            // false, it may be executing asynchronously. The error code is
            // ERROR_IO_PENDING for async I/O; otherwise it's a failure code.
            //
            // If DeviceIoControl() executes synchronously for some reason or
            // fails to execute asynchronously, the NativeOverlapped pointer
            // must still be freed.
            //

            // Set up the byte array to hold a packet (always use 1280 bytes
            // to make sure we can receive the maximum Bluetooth packet size)
            byte[] packet = new byte[1280];
            int bytesReceived = 0;

            // System-defined ERROR_IO_PENDING == 997
            const int ErrorIoPending = 997;

            // Send the IOCTL
            bool listenResult = IPv6ToBleDriverInterface.DeviceIoControl(
                                    driverHandle,
                                    IPv6ToBleDriverInterface.IOCTL_IPV6_TO_BLE_LISTEN_NETWORK_V6,
                                    null,
                                    0,
                                    packet,
                                    (sizeof(byte) * 1280),
                                    out bytesReceived,
                                    nativeOverlapped
                                    );
            if(listenResult)
            {
                // Operation completed synchronously for some reason
                Console.Error.WriteLine("DeviceIoControl executed " +
                    "synchronously despite overlapped I/O flag.\n");
                Overlapped.Unpack(nativeOverlapped);
                Overlapped.Free(nativeOverlapped);
            }
            else
            {
                int error = Marshal.GetLastWin32Error();

                // TEST
                if(error == ErrorIoPending)
                {
                    Console.WriteLine("Successfully sent a buffer to the" +
                        "driver and the driver pended it! \n");
                }
                // TEST

                if(error != ErrorIoPending)
                {
                    // Failed to execute DeviceIoControl with overlapped I/O
                    Console.Error.WriteLine("Failed to execute " +
                        "DeviceIoControl asynchronously with error code" +
                        error + "\n");
                    Overlapped.Unpack(nativeOverlapped);
                    Overlapped.Free(nativeOverlapped);
                }
            }

            // Close the driver handle
            IPv6ToBleDriverInterface.CloseHandle(driverHandle);

            //
            // Step 4
            // Compress the IPv6 header of the received packet
            //

            // TODO: Implement header compression library

            //
            // Step 5
            // Send the compressed packet over the BLE Mesh
            //

            // TODO: Implement Mesh functionality

        }

        /// <summary>
        /// Delegate callback function for handling asynchronous I/O
        /// completion events.
        /// 
        /// This function cleans up the NativeOverlapped structure by unpacking 
        /// and freeing it.
        /// 
        /// For more info on this function, see
        /// https://msdn.microsoft.com/library/system.threading.iocompletioncallback
        /// </summary>
        private unsafe void IPv6ToBleListenCompletionCallback(
            uint errorCode,
            uint numBytes,
            NativeOverlapped* nativeOverlapped
        )
        {
            Overlapped.Unpack(nativeOverlapped);
            Overlapped.Free(nativeOverlapped);
        }
    }
}
