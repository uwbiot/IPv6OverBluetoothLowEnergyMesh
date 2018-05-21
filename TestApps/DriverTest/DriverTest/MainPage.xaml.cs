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
using IPv6ToBleDriverInterfaceForUWP;
using IPv6ToBleDriverInterfaceForUWP.DeviceIO;

using Microsoft.Win32.SafeHandles;      // Safe file handles
using System.Threading;                 // Asynchronous I/O and thread pool
using System.Runtime.InteropServices;   // Marhsalling interop calls
using System.Diagnostics;
using System.Net.Sockets;
using System.Net;
using System.Text;

namespace DriverTest
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
            SafeFileHandle device = null;
            try
            {
                device = DeviceIO.OpenDevice("\\\\.\\IPv6ToBle", true);
            }
            catch
            {
                int code = Marshal.GetLastWin32Error();

                DisplayErrorDialog("Could not open a handle to the driver, " +
                                    "error code: " + code.ToString()
                                    );
                return;
            }

            IAsyncResult result = DeviceIO.BeginGetPacketFromDriverAsync<byte[]>(device, IPv6ToBleIoctl.IOCTL_IPV6_TO_BLE_LISTEN_NETWORK_V6, 1280, IPv6ToBleListenCompletionCallback, null);
        }

        /// <summary>
        /// Delegate callback function for handling asynchronous I/O
        /// completion events.
        /// 
        /// For more info on this function, see
        /// https://msdn.microsoft.com/library/system.threading.iocompletioncallback
        /// </summary>
        private void IPv6ToBleListenCompletionCallback(
            IAsyncResult result
        )
        {
            //
            // Step 1
            // Retrieve the async result's...result
            //
            byte[] packet = null;
            try
            {
                packet = DeviceIO.EndGetPacketFromDriverAsync<byte[]>(result);
            }
            catch (Exception e)
            {
                Debug.WriteLine("Exception occurred." + e.Message);
                return;
            }


            //
            // Step 2
            // Send the packet over Bluetooth provided it's not null
            //
            if (packet != null)
            {
                Debug.WriteLine("Successfully got a packet from the driver.");
            }
            else
            {
                Debug.WriteLine("Packet was null.");
                return;
            }

            // Test: Send another listening request to the driver
            //
            // Step 1
            // Open the handle to the driver with Overlapped async I/O flagged
            //
            SafeFileHandle device = null;
            try
            {
                device = DeviceIO.OpenDevice("\\\\.\\IPv6ToBle", true);
            }
            catch
            {
                int code = Marshal.GetLastWin32Error();

                DisplayErrorDialog("Could not open a handle to the driver, " +
                                    "error code: " + code.ToString()
                                    );
                return;
            }

            IAsyncResult newResult = DeviceIO.BeginGetPacketFromDriverAsync<byte[]>(device, IPv6ToBleIoctl.IOCTL_IPV6_TO_BLE_LISTEN_NETWORK_V6, 1280, IPv6ToBleListenCompletionCallback, null);
        }

        /// <summary>
        /// Sends IOCTL_IPV6_TO_BLE_INJECT_INBOUND_NETWORK_V6 to the driver.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private unsafe void Button_2_Inject_Inbound_Click(object sender, RoutedEventArgs e)
        {
            
        }

        /// <summary>
        /// Sends IOCTL_IPV6_INJECT_OUTBOUND_NETWORK_V6 to the driver
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private unsafe void Button_3_Inject_Outbound_Click(object sender, RoutedEventArgs e)
        {
            
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
            SafeFileHandle device = null;
            try
            {
                device = DeviceIO.OpenDevice("\\\\.\\IPv6ToBle", false);
            }
            catch
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
            String whiteListAddress = "fe80::b826:1c8b:ccbb:32f0%11";

            // Send the IOCTL
            bool result = DeviceIO.SynchronousControl(device, IPv6ToBleIoctl.IOCTL_IPV6_TO_BLE_ADD_TO_WHITE_LIST, whiteListAddress);
            if (!result)
            {
                int error = Marshal.GetLastWin32Error();

                DisplayErrorDialog("Adding to white list failed with this " +
                                    "error code: " + error.ToString());
            }
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
            SafeFileHandle device = null;
            try
            {
                device = DeviceIO.OpenDevice("\\\\.\\IPv6ToBle", false);
            }
            catch
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
            String whiteListAddress = "fe80::b826:1c8b:ccbb:32f0%10";

            // Send the IOCTL
            bool result = DeviceIO.SynchronousControl(device, IPv6ToBleIoctl.IOCTL_IPV6_TO_BLE_REMOVE_FROM_WHITE_LIST, whiteListAddress);
            if (!result)
            {
                int error = Marshal.GetLastWin32Error();

                DisplayErrorDialog("Removing from white list failed with this " +
                                    "error code: " + error.ToString());
            }
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
            SafeFileHandle device = null;
            try
            {
                device = DeviceIO.OpenDevice("\\\\.\\IPv6ToBle", false);
            }
            catch
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
            String meshListAddress = "fe80::3ff8:d2ff:feeb:27b8%2";

            // Send the IOCTL
            bool result = DeviceIO.SynchronousControl(device, IPv6ToBleIoctl.IOCTL_IPV6_TO_BLE_ADD_TO_MESH_LIST, meshListAddress);
            if (!result)
            {
                int error = Marshal.GetLastWin32Error();

                DisplayErrorDialog("Adding to mesh list failed with this " +
                                    "error code: " + error.ToString());
            }
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
            SafeFileHandle device = null;
            try
            {
                device = DeviceIO.OpenDevice("\\\\.\\IPv6ToBle", false);
            }
            catch
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
            String meshListAddress = "fe80::3ff8:d2ff:feeb:27b8%2";

            // Send the IOCTL
            bool result = DeviceIO.SynchronousControl(device, IPv6ToBleIoctl.IOCTL_IPV6_TO_BLE_REMOVE_FROM_MESH_LIST, meshListAddress);
            if (!result)
            {
                int error = Marshal.GetLastWin32Error();

                DisplayErrorDialog("Removing from mesh list failed with this " +
                                    "error code: " + error.ToString());
            }
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
            SafeFileHandle device = null;
            try
            {
                device = DeviceIO.OpenDevice("\\\\.\\IPv6ToBle", false);
            }
            catch
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

            // Send the IOCTL
            bool result = DeviceIO.SynchronousControl(device, IPv6ToBleIoctl.IOCTL_IPV6_TO_BLE_PURGE_WHITE_LIST);
            if (!result)
            {
                int error = Marshal.GetLastWin32Error();

                DisplayErrorDialog("Purging white list failed with this " +
                                    "error code: " + error.ToString());
            }
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
            SafeFileHandle device = null;
            try
            {
                device = DeviceIO.OpenDevice("\\\\.\\IPv6ToBle", false);
            }
            catch
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

            // Send the IOCTL
            bool result = DeviceIO.SynchronousControl(device, IPv6ToBleIoctl.IOCTL_IPV6_TO_BLE_PURGE_MESH_LIST);
            if (!result)
            {
                int error = Marshal.GetLastWin32Error();

                DisplayErrorDialog("Purging mesh list failed with this " +
                                    "error code: " + error.ToString());
            }
        }

        private void Button_10_send_udp_packet_Click(object sender, RoutedEventArgs e)
        {
            //Socket sock = new Socket(AddressFamily.InterNetworkV6,
            //                         SocketType.Dgram,
            //                         ProtocolType.Udp
            //                         );

            //IPAddress serverAddr = IPAddress.Parse("fe80::3ff8:d2ff:feeb:27b8%2");

            //IPEndPoint endPoint = new IPEndPoint(serverAddr, 11000);

            //string text = "Hello";
            //byte[] send_buffer = Encoding.ASCII.GetBytes(text);

            //sock.SendTo(send_buffer, endPoint);

            SendUdp(11000, "fe80::3ff8:d2ff:feeb:27b8%2", 11000, Encoding.ASCII.GetBytes("Hello"));
        }

        private static void SendUdp(
            int     sourcePort,
            String  destinationIPv6Address,
            int     destinationPort,
            byte[]  packet
        )
        {
            using (UdpClient client = new UdpClient(sourcePort))
            {
                client.Send(packet,
                            packet.Length,
                            destinationIPv6Address,
                            destinationPort
                            );
            }
        }

        private void Button_11_query_mesh_role_Click(object sender, RoutedEventArgs e)
        {
            //
            // Step 1
            // Open the handle to the driver for synchronous I/O
            //
            SafeFileHandle device = null;
            try
            {
                device = DeviceIO.OpenDevice("\\\\.\\IPv6ToBle", false);
            }
            catch
            {
                int code = Marshal.GetLastWin32Error();

                DisplayErrorDialog("Could not open a handle to the driver, " +
                                    "error code: " + code.ToString()
                                    );
                return;
            }

            //
            // Step 2
            // Ask the driver what this device's role is

            // Send the IOCTL
            bool isBorderRouter = false;
            bool result = false;
            try
            {
                result = DeviceIO.SynchronousControl(device,
                                                      IPv6ToBleIoctl.IOCTL_IPV6_TO_BLE_QUERY_MESH_ROLE,
                                                      out isBorderRouter
                                                      );
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
            
            if (!result)
            {
                int error = Marshal.GetLastWin32Error();

                DisplayErrorDialog("Querying driver for mesh role failed " +
                                   "with this error code: " + error.ToString()
                                   );
            }
            else
            {
                // The operation succeeded, now check the role
                if (isBorderRouter)
                {
                    DisplayErrorDialog("This device is a border router.");
                }
                else
                {
                    DisplayErrorDialog("This device is not a border router.");
                }
            }
        }
    }
}
