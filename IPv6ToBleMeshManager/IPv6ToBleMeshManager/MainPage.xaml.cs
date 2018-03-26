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

// IPv6ToBle Interop Library
using IPv6ToBleInteropLibrary;
using Microsoft.Win32.SafeHandles;

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
        private void Button_1_Listen_Click(object sender, RoutedEventArgs e)
        {
            //
            // Step 1
            // Open the handle to the driver
            //
            int driverHandle = IPv6ToBleDriverInterface.CreateFile(
                "\\\\.\\IPv6ToBle",
                IPv6ToBleDriverInterface.GENERIC_READ | IPv6ToBleDriverInterface.GENERIC_WRITE,
                IPv6ToBleDriverInterface.FILE_SHARE_READ,
                IntPtr.Zero,
                IPv6ToBleDriverInterface.OPEN_EXISTING,
                IPv6ToBleDriverInterface.FILE_FLAG_OVERLAPPED,
                0
            );
            
            if(driverHandle == IPv6ToBleDriverInterface.INVALID_HANDLE_VALUE)
            {
                Console.Error.WriteLine("Could not open handle to driver\n");
                throw new FileNotFoundException();
            }

            //
            // Step 2
            // Set up the Overlapped structure, which is required for
            // asynchronous operations with DeviceIoControl()
            //



            //
            // Step 3
            // Send the IOCTL to request the driver to listen for a packet and
            // give us the packet through the request's output buffer. This
            // is an asynchronous operation using Overlapped I/O.
            //

        }
    }
}
