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
using IPv6ToBleSixLowPanLibraryForUWP;
using IPv6ToBleBluetoothGattLibraryForUWP.Helpers;

using Microsoft.Win32.SafeHandles;      // Safe file handles
using System.Threading;                 // Asynchronous I/O and thread pool
using System.Runtime.InteropServices;   // Marhsalling interop calls
using System.Diagnostics;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.ComponentModel;

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

            // Testing - try setting a higher number for max thread pool completion ports
            //ThreadPool.SetMaxThreads(10000, 10000);

            ThreadPool.GetMaxThreads(out maxThreadPoolWorkers, out maxThreadPoolCompletionPorts);
            Debug.WriteLine($"Max thread pool workers: {maxThreadPoolWorkers}, " +
                            $"max thread pool completion ports: {maxThreadPoolCompletionPorts}"
                            );
        }

        // Private variables for performance testing
        private int maxThreadPoolWorkers = 0;
        private int maxThreadPoolCompletionPorts = 0;
        
        private int testingNumPackets = 0;
        private int testingPayloadLength = 0;

        private Stopwatch stopwatch = new Stopwatch();

        private volatile int numPacketsReceived = 0;

        private volatile int numThreadsActive = 0;
        private object numThreadsActiveLock = new object();

        public int NumThreadsActive
        {
            get
            {
                return numThreadsActive;
            }
            set
            {
                if(numThreadsActive != value)
                {
                    numThreadsActive = value;
                }
                OnNumThreadsActiveChanged(new PropertyChangedEventArgs("NumThreadsActive"));
            }
        }

        // Event to notify when the number of threads has changed
        private void OnNumThreadsActiveChanged(PropertyChangedEventArgs args)
        {
            NumThreadsActiveChanged?.Invoke(this, args);
        }

        public event PropertyChangedEventHandler NumThreadsActiveChanged;

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
            if (testingNumPackets == 0)
            {
                DisplayErrorDialog("Number of test packets is zero. Enter a " +
                                   "valid value."
                                   );
                return;
            }

            for (int i = 0; i < testingNumPackets; i++)
            {
                SendListeningRequestToDriver();
            }           
        }

        private void SendListeningRequestToDriver()
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

            lock(numThreadsActiveLock)
            {
                NumThreadsActive++;
            }            

            IAsyncResult result = DeviceIO.BeginGetPacketFromDriverAsync<byte[]>(device, 
                                                                                IPv6ToBleIoctl.IOCTL_IPV6_TO_BLE_LISTEN_NETWORK_V6, 
                                                                                1280, 
                                                                                IPv6ToBleListenCompletionCallback, 
                                                                                null
                                                                                );
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
            lock(numThreadsActiveLock)
            {
                NumThreadsActive--;
            }            

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
                numPacketsReceived++;
                //Debug.WriteLine($"Received packet {numPacketsReceived}" +
                //                $" from the driver."
                //                );
            }
            else
            {
                Debug.WriteLine("Packet was null.");
                return;
            }

            //
            // Compress the packet (test)
            //
            //Debug.WriteLine("Original packet size: " + packet.Length);
            //Debug.WriteLine("Original packet contents: " + Utilities.BytesToString(packet));
            //Debug.WriteLine("Compressing packet...");

            //HeaderCompression compression = new HeaderCompression();

            //int processedHeaderLength = 0;
            //int payloadLength = 0;
            //byte[] compressedPacket = compression.CompressHeaderIphc(packet,
            //                                                         out processedHeaderLength,
            //                                                         out payloadLength
            //                                                         );

            //Debug.WriteLine("Compression of packet complete.");

            //if(compressedPacket == null)
            //{
            //    Debug.WriteLine("Error occurred during packet compression. " +
            //                    "Unable to compress."
            //                    );
            //}
            //else
            //{
            //    Debug.WriteLine("Compressed packet size: " + compressedPacket.Length);
            //    Debug.WriteLine("Compressed packet contents: " + Utilities.BytesToString(compressedPacket));
            //}

            ////
            //// Decompress the compressed packet back into its original form (test)
            ////
            //Debug.WriteLine("Decompressing packet...");

            //byte[] uncompressedPacket = compression.UncompressHeaderIphc(compressedPacket,
            //                                                             processedHeaderLength,
            //                                                             payloadLength
            //                                                             );

            //if (uncompressedPacket == null)
            //{
            //    Debug.WriteLine("Error occurred during packet decompression.");
            //}
            //else
            //{
            //    Debug.WriteLine("Uncompressed packet size: " + uncompressedPacket.Length);
            //    Debug.WriteLine("Uncompressed packet contents: " + Utilities.BytesToString(uncompressedPacket));

            //    Debug.WriteLine("Decompressed packet size is same as original: " + (packet.Length == uncompressedPacket.Length));
            //    Debug.WriteLine("Decompressed packet is identical to the original: " + (Utilities.PacketsEqual(packet, uncompressedPacket)));
            //}

            // Test: Send another listening request to the driver. Comment out
            // if sending a fixed number in the first place; uncomment if
            // sending and testing an arbitrarily large number of packets.

            //if (isTesting)
            //{
            //    SendListeningRequestToDriver();
            //}
        }

        /// <summary>
        /// Private function to watch for when the number of active threads
        /// reaches 0
        /// </summary>
        private void ListenForBackgroundThreadCompletion(
            object sender,
            PropertyChangedEventArgs args
         )
        {
            if(args.PropertyName == "NumThreadsActive")
            {
                lock(numThreadsActiveLock)
                {
                    if (NumThreadsActive == 0)
                    {
                        stopwatch.Stop();

                        Debug.WriteLine($"Test complete. Received {numPacketsReceived}" +
                                           $" packets from the driver in" +
                                           $" {stopwatch.Elapsed.Milliseconds}" +
                                           $" milliseconds."
                                           );
                        numPacketsReceived = 0;

                        stopwatch = new Stopwatch();
                    }
                }                
            }
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
            if(testingPayloadLength == 0)
            {
                DisplayErrorDialog("Payload length is zero. Enter a valid " +
                                   "value."
                                   );
                return;
            }
            if(testingNumPackets == 0)
            {
                DisplayErrorDialog("Must set a number of requested packets.");
                return;
            }

            using (UdpClient client = new UdpClient(AddressFamily.InterNetworkV6))
            {
                // Set up a test payload based on the desired payload length
                StringBuilder payload = new StringBuilder();
                int count = 0;
                for(int i = 0; i < testingPayloadLength; i++)
                {
                    payload.Append($"{count}");

                    // Cycle through 0-9
                    if(count == 9)
                    {
                        count = 0;
                    }
                    else
                    {
                        count++;
                    }
                }

                byte[] test = Encoding.ASCII.GetBytes(payload.ToString());
                Debug.WriteLine($"Test payload length: {test.Length}.");
                    

                IPAddress multicastAddress = IPAddress.Parse("ff02::1");
                IPEndPoint endPoint = new IPEndPoint(multicastAddress, 11000);
                client.JoinMulticastGroup(multicastAddress);

                // Start timing, assuming listening requests have been sent
                // previously
                stopwatch.Start();

                for (int i = 0; i < testingNumPackets; i++)
                {                        
                    client.Send(test, test.Length, endPoint);
                }              
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

        private void button_12_stop_packet_test_Click(object sender, RoutedEventArgs e)
        {
            NumThreadsActiveChanged -= ListenForBackgroundThreadCompletion;
        }

        private void button_13_start_packet_test_Click(object sender, RoutedEventArgs e)
        {
            NumThreadsActiveChanged += ListenForBackgroundThreadCompletion;
        }

        /// <summary>
        /// Invoked after a user is done typing in the packet input box, to
        /// validate the input (done typing means hit enter or clicked away).
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void packetNumberInputBox_LostFocus(object sender, RoutedEventArgs e)
        {
            bool isAnInt = int.TryParse(packetNumberInputBox.Text, out testingNumPackets);
            if(!isAnInt)
            {
                DisplayErrorDialog("Invalid number for requested packets. " +
                                   "Input must be an integer. Try again."
                                   );
                goto ErrorCleanup;
            }
            if (testingNumPackets > maxThreadPoolCompletionPorts)
            {
                DisplayErrorDialog("Number of packets requested exceeds " +
                                   "the system max number of thread pool" +
                                   " I/O completion ports. Max number of " +
                                   $"completion ports: {maxThreadPoolCompletionPorts}."
                                   );
                goto ErrorCleanup;
            }
            if (testingNumPackets < 1)
            {
                DisplayErrorDialog("Number of packets must be at least one. " +
                                   "Try again."
                                   );
                goto ErrorCleanup;
            }

            return;

        ErrorCleanup:

            testingNumPackets = 0;
            packetNumberInputBox.Text = "";
        }

        /// <summary>
        /// Invoked after a user is done typing in the payload length box, to
        /// validate the input (done typing means hit enter or clicked away).
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void payloadLengthInputBox_LostFocus(object sender, RoutedEventArgs e)
        {
            int[] validPayloadLengths = { 2, 16, 64, 512, 1024 };
            bool isAnInt = int.TryParse(payloadLengthInputBox.Text, out testingPayloadLength);
            if (!isAnInt || !validPayloadLengths.Contains(testingPayloadLength))
            {
                DisplayErrorDialog("Payload length was either not an " +
                                   "integer or was not a valid payload " +
                                   "length. Length must be 2, 16, 64, 512, " +
                                   "or 1024. Try again."
                                   );
                testingPayloadLength = 0;
                payloadLengthInputBox.Text = "";
            }
        }
    }
}
