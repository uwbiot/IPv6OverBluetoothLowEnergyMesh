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
using System.Diagnostics;

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
        /// Displays an error dialog since UI apps don't have console access.
        /// Uses asynchronous dispatching to update the app's GUI
        /// </summary>
        /// <param name="errorText"></param>
        private async void DisplayErrorDialog(string errorText)
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
            {
                ContentDialog errorDialog = new ContentDialog()
                {
                    Title = "You broked it!",
                    Content = errorText,
                    CloseButtonText = "OK"
                };

                await errorDialog.ShowAsync();
            });
        }

        /// <summary>
        /// Sends IOCTL_IPV6_TO_BLE_LISTEN_NETWORK_V6 to the driver.
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
                int code = Marshal.GetLastWin32Error();

                DisplayErrorDialog("Could not open a handle to the driver, " +
                                    "error code: " + code.ToString()
                                    );
                return;
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
                Debug.WriteLine("Could not bind driver handle to a thread " +
                    "pool I/O completion port.\n");
            }

            // Set up the byte array to hold a packet (always use 1280 bytes
            // to make sure we can receive the maximum Bluetooth packet size)
            byte[] packet = new byte[1280];            
            int bytesReceived = 0;

            // Create a NativeOverlapped pointer to pass to DeviceIoControl().
            // Using NULL for the second parameter in Pack() because we pass
            // data in the DeviceIoControl parameters instead.
            Overlapped overlapped = new Overlapped();
            NativeOverlapped* nativeOverlapped = overlapped.Pack(IPv6ToBleListenCompletionCallback, 
                                                                null
                                                                );

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
                Debug.WriteLine("DeviceIoControl executed synchronously " +
                    "despite overlapped I/O flag.\n");
                Overlapped.Unpack(nativeOverlapped);
                Overlapped.Free(nativeOverlapped);
            }
            else
            {
                int error = Marshal.GetLastWin32Error();

                if(error != ErrorIoPending)
                {
                    // Failed to execute DeviceIoControl with overlapped I/O
                    Debug.WriteLine("Failed to execute " +
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
        /// This function cleans up the NativeOverlapped structure by 
        /// unpacking  and freeing it.
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

        /// <summary>
        /// Sends IOCTL_IPV6_TO_BLE_INJECT_INBOUND_NETWORK_V6 to the driver.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private unsafe void Button_2_Inject_Inbound_Click(object sender, RoutedEventArgs e)
        {
            //
            // Step 1
            // Open the handle to the driver for synchronous I/O
            //
            SafeFileHandle driverHandle = IPv6ToBleDriverInterface.CreateFile(
                "\\\\.\\IPv6ToBle",
                IPv6ToBleDriverInterface.GENERIC_READ | IPv6ToBleDriverInterface.GENERIC_WRITE,
                IPv6ToBleDriverInterface.FILE_SHARE_READ | IPv6ToBleDriverInterface.FILE_SHARE_WRITE,
                IntPtr.Zero,
                IPv6ToBleDriverInterface.OPEN_EXISTING,
                0,                                          // synchronous
                IntPtr.Zero
            );

            if (driverHandle.IsInvalid)
            {
                int code = Marshal.GetLastWin32Error();

                DisplayErrorDialog("Could not open a handle to the driver, " +
                                    "error code: " + code.ToString()
                                    );
                return;
            }

            //
            // Step 2
            // Send the given packet to the driver
            //

            // TODO: in the packet processing app, receive a packet and send it
            // to the driver for inbound injection

            // Close the driver handle
            IPv6ToBleDriverInterface.CloseHandle(driverHandle);
        }

        /// <summary>
        /// Sends IOCTL_IPV6_INJECT_OUTBOUND_NETWORK_V6 to the driver
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private unsafe void Button_3_Inject_Outbound_Click(object sender, RoutedEventArgs e)
        {
            //
            // Step 1
            // Open the handle to the driver for synchronous I/O
            //
            SafeFileHandle driverHandle = IPv6ToBleDriverInterface.CreateFile(
                "\\\\.\\IPv6ToBle",
                IPv6ToBleDriverInterface.GENERIC_READ | IPv6ToBleDriverInterface.GENERIC_WRITE,
                IPv6ToBleDriverInterface.FILE_SHARE_READ | IPv6ToBleDriverInterface.FILE_SHARE_WRITE,
                IntPtr.Zero,
                IPv6ToBleDriverInterface.OPEN_EXISTING,
                0,                                          // synchronous
                IntPtr.Zero
            );

            if (driverHandle.IsInvalid)
            {
                int code = Marshal.GetLastWin32Error();

                DisplayErrorDialog("Could not open a handle to the driver, " +
                                    "error code: " + code.ToString()
                                    );
                return;
            }

            //
            // Step 2
            // Send the given packet to the driver
            //

            // TODO: in the packet processing app, receive a packet and send it
            // to the driver for outbound injection

            // Close the driver handle
            IPv6ToBleDriverInterface.CloseHandle(driverHandle);
        }

        /// <summary>
        /// Sends IOCTL_IPV6_TO_BLE_ADD_TO_WHITE_LIST to the driver
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private unsafe void Button_4_Add_To_White_List_Click(object sender, RoutedEventArgs e)
        {
            //
            // Step 1
            // Open the handle to the driver for synchronous I/O
            //
            SafeFileHandle driverHandle = IPv6ToBleDriverInterface.CreateFile(
                "\\\\.\\IPv6ToBle",
                IPv6ToBleDriverInterface.GENERIC_READ | IPv6ToBleDriverInterface.GENERIC_WRITE,
                IPv6ToBleDriverInterface.FILE_SHARE_READ | IPv6ToBleDriverInterface.FILE_SHARE_WRITE,
                IntPtr.Zero,
                IPv6ToBleDriverInterface.OPEN_EXISTING,
                0,  // synchronous
                IntPtr.Zero
            );

            if (driverHandle.IsInvalid)
            {
                int code = Marshal.GetLastWin32Error();

                DisplayErrorDialog("Could not open a handle to the driver, " +
                                    "error code: " + code.ToString()
                                    );
                return;
            }

            //
            // Step 2
            // Send the supplied IPv6 address for the white list to the driver
            // to add it to the list
            //

            // Hard-coded for testing, would normally acquire from an
            // authenticated service or other source
            String whiteListAddress = "fe80::71c4:225:d048:9476%15";
            int bytesReturned = 0;

            // Send the IOCTL
            bool result = IPv6ToBleDriverInterface.DeviceIoControl(
                                    driverHandle,
                                    IPv6ToBleDriverInterface.IOCTL_IPV6_TO_BLE_ADD_TO_WHITE_LIST,
                                    whiteListAddress,
                                    sizeof(char) * whiteListAddress.Length,
                                    "",
                                    0,
                                    out bytesReturned, // Not returning bytes
                                    null
                                    );
            if (!result)
            {
                int error = Marshal.GetLastWin32Error();

                DisplayErrorDialog("Adding to white list failed with this " +
                                    "error code: " + error.ToString());
            }

            // Close the driver handle
            IPv6ToBleDriverInterface.CloseHandle(driverHandle);
        }

        /// <summary>
        /// Sends IOCTL_IPV6_TO_BLE_REMOVE_FROM_WHITE_LIST to the driver
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private unsafe void Button_5_Remove_From_White_List_Click(object sender, RoutedEventArgs e)
        {
            //
            // Step 1
            // Open the handle to the driver for synchronous I/O
            //
            SafeFileHandle driverHandle = IPv6ToBleDriverInterface.CreateFile(
                "\\\\.\\IPv6ToBle",
                IPv6ToBleDriverInterface.GENERIC_READ | IPv6ToBleDriverInterface.GENERIC_WRITE,
                IPv6ToBleDriverInterface.FILE_SHARE_READ | IPv6ToBleDriverInterface.FILE_SHARE_WRITE,
                IntPtr.Zero,
                IPv6ToBleDriverInterface.OPEN_EXISTING,
                0,  // synchronous
                IntPtr.Zero
            );

            if (driverHandle.IsInvalid)
            {
                int code = Marshal.GetLastWin32Error();

                DisplayErrorDialog("Could not open a handle to the driver, " +
                                    "error code: " + code.ToString()
                                    );
                return;
            }

            //
            // Step 2
            // Send the supplied IPv6 address for the white list to the driver
            //

            // Hard-coded for testing, would normally acquire from an
            // authenticated service or other source
            String whiteListAddress = "fe80::71c4:225:d048:9476%15";
            int bytesReturned = 0;

            // Send the IOCTL
            bool result = IPv6ToBleDriverInterface.DeviceIoControl(
                                    driverHandle,
                                    IPv6ToBleDriverInterface.IOCTL_IPV6_TO_BLE_REMOVE_FROM_WHITE_LIST,
                                    whiteListAddress,
                                    sizeof(char) * whiteListAddress.Length,
                                    "",
                                    0,
                                    out bytesReturned, // Not returning bytes
                                    null
                                    );
            if (!result)
            {
                int error = Marshal.GetLastWin32Error();

                DisplayErrorDialog("Removing from white list failed with this" +
                                    "error code: " + error.ToString());
            }

            // Close the driver handle
            IPv6ToBleDriverInterface.CloseHandle(driverHandle);
        }

        /// <summary>
        /// Sends IOCTL_IPV6_TO_BLE_ADD_TO_MESH_LIST to the driver
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private unsafe void Button_6_Add_To_Mesh_List_Click(object sender, RoutedEventArgs e)
        {
            //
            // Step 1
            // Open the handle to the driver for synchronous I/O
            //
            SafeFileHandle driverHandle = IPv6ToBleDriverInterface.CreateFile(
                "\\\\.\\IPv6ToBle",
                IPv6ToBleDriverInterface.GENERIC_READ | IPv6ToBleDriverInterface.GENERIC_WRITE,
                IPv6ToBleDriverInterface.FILE_SHARE_READ | IPv6ToBleDriverInterface.FILE_SHARE_WRITE,
                IntPtr.Zero,
                IPv6ToBleDriverInterface.OPEN_EXISTING,
                0,  // synchronous
                IntPtr.Zero
            );

            if (driverHandle.IsInvalid)
            {
                int code = Marshal.GetLastWin32Error();

                DisplayErrorDialog("Could not open a handle to the driver, " +
                                    "error code: " + code.ToString()
                                    );
                return;
            }

            //
            // Step 2
            // Send the supplied IPv6 address for the white list to the driver
            // to add it to the list
            //

            // Hard-coded for testing, would normally acquire from an
            // authenticated service or other source
            String meshListAddress = "fe80::3515:de40:9fe2:caaf%3";
            int bytesReturned = 0;

            // Send the IOCTL
            bool result = IPv6ToBleDriverInterface.DeviceIoControl(
                                    driverHandle,
                                    IPv6ToBleDriverInterface.IOCTL_IPV6_TO_BLE_ADD_TO_MESH_LIST,
                                    meshListAddress,
                                    sizeof(char) * meshListAddress.Length,
                                    "",
                                    0,
                                    out bytesReturned, // Not returning bytes
                                    null
                                    );
            if (!result)
            {
                int error = Marshal.GetLastWin32Error();

                DisplayErrorDialog("Adding to mesh list failed with this " +
                                    "error code: " + error.ToString());
            }

            // Close the driver handle
            IPv6ToBleDriverInterface.CloseHandle(driverHandle);
        }

        /// <summary>
        /// Sends IOCTL_IPV6_TO_BLE_REMOVE_FROM_MESH_LIST to the driver
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private unsafe void Button_7_Remove_From_Mesh_List_Click(object sender, RoutedEventArgs e)
        {
            //
            // Step 1
            // Open the handle to the driver for synchronous I/O
            //
            SafeFileHandle driverHandle = IPv6ToBleDriverInterface.CreateFile(
                "\\\\.\\IPv6ToBle",
                IPv6ToBleDriverInterface.GENERIC_READ | IPv6ToBleDriverInterface.GENERIC_WRITE,
                IPv6ToBleDriverInterface.FILE_SHARE_READ | IPv6ToBleDriverInterface.FILE_SHARE_WRITE,
                IntPtr.Zero,
                IPv6ToBleDriverInterface.OPEN_EXISTING,
                0,  // synchronous
                IntPtr.Zero
            );

            if (driverHandle.IsInvalid)
            {
                int code = Marshal.GetLastWin32Error();

                DisplayErrorDialog("Could not open a handle to the driver, " +
                                    "error code: " + code.ToString()
                                    );
                return;
            }

            //
            // Step 2
            // Send the supplied IPv6 address for the mesh list to the driver
            //

            // Hard-coded for testing, would normally acquire from an
            // authenticated service or other source
            String meshListAddress = "fe80::3515:de40:9fe2:caaf%3";
            int bytesReturned = 0;

            // Send the IOCTL
            bool result = IPv6ToBleDriverInterface.DeviceIoControl(
                                    driverHandle,
                                    IPv6ToBleDriverInterface.IOCTL_IPV6_TO_BLE_REMOVE_FROM_MESH_LIST,
                                    meshListAddress,
                                    sizeof(char) * meshListAddress.Length,
                                    "",
                                    0,
                                    out bytesReturned, // Not returning bytes
                                    null
                                    );
            if (!result)
            {
                int error = Marshal.GetLastWin32Error();

                DisplayErrorDialog("Removing from mesh list failed with this" +
                                    "error code: " + error.ToString());
            }

            // Close the driver handle
            IPv6ToBleDriverInterface.CloseHandle(driverHandle);
        }

        /// <summary>
        /// Sends IOCTL_IPV6_TO_BLE_PURGE_WHITE_LIST to the driver
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private unsafe void Button_8_Purge_White_List_Click(object sender, RoutedEventArgs e)
        {
            //
            // Step 1
            // Open the handle to the driver for synchronous I/O
            //
            SafeFileHandle driverHandle = IPv6ToBleDriverInterface.CreateFile(
                "\\\\.\\IPv6ToBle",
                IPv6ToBleDriverInterface.GENERIC_READ | IPv6ToBleDriverInterface.GENERIC_WRITE,
                IPv6ToBleDriverInterface.FILE_SHARE_READ | IPv6ToBleDriverInterface.FILE_SHARE_WRITE,
                IntPtr.Zero,
                IPv6ToBleDriverInterface.OPEN_EXISTING,
                0,  // synchronous
                IntPtr.Zero
            );

            if (driverHandle.IsInvalid)
            {
                int code = Marshal.GetLastWin32Error();

                DisplayErrorDialog("Could not open a handle to the driver, " +
                                    "error code: " + code.ToString()
                                    );
                return;
            }

            //
            // Step 2
            // Send the IOCTL
            //

            // Hard-coded for testing, would normally acquire from an
            // authenticated service or other source
            int bytesReturned = 0;

            // Send the IOCTL
            bool result = IPv6ToBleDriverInterface.DeviceIoControl(
                                    driverHandle,
                                    IPv6ToBleDriverInterface.IOCTL_IPV6_TO_BLE_PURGE_WHITE_LIST,
                                    "",
                                    0,
                                    "",
                                    0,
                                    out bytesReturned, // Not returning bytes
                                    null
                                    );
            if (!result)
            {
                int error = Marshal.GetLastWin32Error();

                DisplayErrorDialog("Purging white list failed with this " +
                                    "error code: " + error.ToString());
            }

            // Close the driver handle
            IPv6ToBleDriverInterface.CloseHandle(driverHandle);
        }

        /// <summary>
        /// Sends IOCTL_IPV6_TO_BLE_PURGE_MESH_LIST to the driver
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private unsafe void Button_9_Purge_Mesh_List_Click(object sender, RoutedEventArgs e)
        {
            //
            // Step 1
            // Open the handle to the driver for synchronous I/O
            //
            SafeFileHandle driverHandle = IPv6ToBleDriverInterface.CreateFile(
                "\\\\.\\IPv6ToBle",
                IPv6ToBleDriverInterface.GENERIC_READ | IPv6ToBleDriverInterface.GENERIC_WRITE,
                IPv6ToBleDriverInterface.FILE_SHARE_READ | IPv6ToBleDriverInterface.FILE_SHARE_WRITE,
                IntPtr.Zero,
                IPv6ToBleDriverInterface.OPEN_EXISTING,
                0,  // synchronous
                IntPtr.Zero
            );

            if (driverHandle.IsInvalid)
            {
                int code = Marshal.GetLastWin32Error();

                DisplayErrorDialog("Could not open a handle to the driver, " +
                                    "error code: " + code.ToString()
                                    );
                return;
            }

            //
            // Step 2
            // Send the IOCTL
            //

            // Hard-coded for testing, would normally acquire from an
            // authenticated service or other source
            int bytesReturned = 0;

            // Send the IOCTL
            bool result = IPv6ToBleDriverInterface.DeviceIoControl(
                                    driverHandle,
                                    IPv6ToBleDriverInterface.IOCTL_IPV6_TO_BLE_PURGE_MESH_LIST,
                                    "",
                                    0,
                                    "",
                                    0,
                                    out bytesReturned, // Not returning bytes
                                    null
                                    );
            if (!result)
            {
                int error = Marshal.GetLastWin32Error();

                DisplayErrorDialog("Purging mesh list failed with this " +
                                    "error code: " + error.ToString());
            }

            // Close the driver handle
            IPv6ToBleDriverInterface.CloseHandle(driverHandle);
        }
    }
}
