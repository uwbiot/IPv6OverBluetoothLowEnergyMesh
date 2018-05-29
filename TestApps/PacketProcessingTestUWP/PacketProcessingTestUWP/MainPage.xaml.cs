using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Net;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Threading;
using System.ComponentModel;
using System.Runtime.InteropServices;

using Microsoft.Win32.SafeHandles;

using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.ApplicationModel.Background;

//
// Namespaces for this project
//
using IPv6ToBleBluetoothGattLibraryForUWP.Client;
using IPv6ToBleBluetoothGattLibraryForUWP.Server;
using IPv6ToBleBluetoothGattLibraryForUWP.Characteristics;
using IPv6ToBleBluetoothGattLibraryForUWP.Helpers;
using IPv6ToBleSixLowPanLibraryForUWP;
using IPv6ToBleDriverInterfaceForUWP;
using IPv6ToBleDriverInterfaceForUWP.DeviceIO;

namespace PacketProcessingTestUWP
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        #region Local variables
        //---------------------------------------------------------------------
        // General variables
        //---------------------------------------------------------------------

        // A list of this device's link-local IPv6 addresses (in case it has
        // more than one adapter with an IPv6 address)
        private List<IPAddress> localIPv6AddressesForDesktop = null;

        // This device's generated link-local IPv6 address
        private IPAddress generatedLocalIPv6AddressForNode = null;

        //---------------------------------------------------------------------
        // Bluetooth variables
        //---------------------------------------------------------------------

        // The GATT server to receive packets and provide information to
        // remote devices
        //private IPv6ToBleGattServer gattServer = null;

        // The local packet write characteristic, part of the packet processing
        // service in the GATT server
        //private IPv6ToBlePacketWriteCharacteristic localPacketWriteCharacteristic;

        // A FIFO queue of recent messages/packets to use with managed flooding
        private Queue<byte[]> messageCache = null;

        // A lock for sending a packet over BLE, to prevent race conditions
        // where multiple threads receiving packets from the driver try to 
        // send packets out
        private object bleSendingLock = new object();

        // Testing count for sending requests
        private int count = 0;

        // A device enumerator to find nearby devices on startup
        private DeviceEnumerator enumerator;

        // Dictionary of Bluetooth LE device objects and their IP addresses to 
        // match found device information
        private Dictionary<IPAddress, DeviceInformation> supportedBleDevices = new Dictionary<IPAddress, DeviceInformation>();

        // Tracker to know when device enumeration is complete
        private bool enumerationCompleted = false;

        #endregion

        #region UI stuff
        //---------------------------------------------------------------------
        // UI stuff
        //---------------------------------------------------------------------

        public MainPage()
        {
            this.InitializeComponent();
        }

        private async void startButton_Click(object sender, RoutedEventArgs e)
        {
            overallStatusBox.Text = "Starting...";

            //
            // Step 1
            // Acquire this device's IPv6 address from the local Bluetooth radio
            //
            //generatedLocalIPv6AddressForNode = await IPv6AddressFromBluetoothAddress.GenerateAsync(2);
            //if (generatedLocalIPv6AddressForNode == null)
            //{
            //    overallStatusBox.Text = "Could not generate the local IPv6 address.";
            //    throw new Exception();
            //}

            localIPv6AddressesForDesktop = GetLocalIPv6AddressesOnDesktop();
            if (localIPv6AddressesForDesktop == null)
            {
                overallStatusBox.Text = "Could not acquire the local IPv6 address(es).";
                throw new Exception();
            }

            //
            // Step 3
            // Spin up the GATT server service to listen for later replies
            // over Bluetooth LE
            //
            //gattServer = new IPv6ToBleGattServer();
            //bool gattServerStarted = false;
            //try
            //{
            //    gattServerStarted = await gattServer.StartAsync();
            //}
            //catch (Exception ex)
            //{
            //    overallStatusBox.Text = "Error starting GATT server with this" +
            //                            "message: " + ex.Message;
            //}

            //if (!gattServerStarted)
            //{
            //    overallStatusBox.Text = "Could not start the GATT server.";
            //    throw new Exception();
            //}
            //else
            //{
            //    Debug.WriteLine("GATT server started.");
            //}

            //// Get a handle to the local packet write characteristic
            //localPacketWriteCharacteristic = gattServer.PacketProcessingService.PacketWriteCharacteristic;

            //// Subscribe to the characteristic's "packet received" event
            //localPacketWriteCharacteristic.PropertyChanged += WatchForPacketReception;

            //
            // Step 4
            // Enumerate nearby supported devices
            //
            await EnumerateNearbySupportedDevices();

            //
            // Step 5
            // Initialize the message cache for 10 messages
            //
            messageCache = new Queue<byte[]>(10);

            //
            // Step 6
            // Send 10 initial listening requests to the driver
            //
            //for(int i = 0; i < 10; i++)
            for (int i = 0; i < 1; i++)
            {
                Debug.WriteLine($"Sending listening request {++count}");
                SendListenRequestToDriver();
                Thread.Sleep(100); // Wait 1/10 second to start another one
            }

            overallStatusBox.Text = "Finished starting.";
        }

        private void stopButton_Click(object sender, RoutedEventArgs e)
        {
            //
            // Step 1
            // Shut down Bluetooth resources upon exit
            //

            // Unsubscribe from the local packet write characteristic's
            // packet received event
            // localPacketWriteCharacteristic.PropertyChanged -= WatchForPacketReception;

            //
            // Step 2
            // Stop device enumeration if it is still in progress when this
            // background app is canceled
            //
            if (enumerator != null)
            {
                enumerator.StopSupportedDeviceEnumerator();
                enumerator.EnumerationCompleted -= WatchForEnumerationCompletion;
            }

            overallStatusBox.Text = "Stopped.";
        }
        #endregion

        #region Device discovery helpers

        /// <summary>
        /// Finds nearby devices that are running the Internet Protocol Support
        /// Service (IPSS) and the IPv6ToBle packet processing service.
        /// </summary>
        private async Task EnumerateNearbySupportedDevices()
        {
            // Start the supported device enumerator and subscribe to the
            // EnumerationCompleted event
            enumerator = new DeviceEnumerator();

            Debug.WriteLine("Looking for nearby supported devices...");

            enumerator.EnumerationCompleted += WatchForEnumerationCompletion;
            enumerator.StartSupportedDeviceEnumerator();

            // Spin while enumeration is in progress
            while (!enumerationCompleted) ;

            Debug.WriteLine("Enumeration of nearby supported devices" +
                            " complete."
                            );            

            // Stop the device watcher            
            enumerator.StopSupportedDeviceEnumerator();

            // Filter found devices for supported ones
            Debug.WriteLine("Filtering devices for supported services...");

            await enumerator.PopulateSupportedDevices();

            Debug.WriteLine("Filtering for supported devices complete.");

            if (enumerator.SupportedBleDevices != null)
            {
                supportedBleDevices = enumerator.SupportedBleDevices;
                Debug.WriteLine($"Found {supportedBleDevices.Count} devices");
            }
            else
            {
                Debug.WriteLine("No nearby supported devices found.");
            }

            if (enumerator != null)
            {
                enumerator.EnumerationCompleted -= WatchForEnumerationCompletion;
                enumerator = null;
            }
        }

        /// <summary>
        /// A small helper method to watch for the device watcher's enumeration
        /// completion event.
        /// </summary>
        private void WatchForEnumerationCompletion(
            object                      sender,
            PropertyChangedEventArgs    eventArgs
        )
        {
            if(sender.GetType() == typeof(DeviceEnumerator))
            {
                if (eventArgs.PropertyName == "EnumerationComplete")
                {
                    enumerationCompleted = true;
                }
            }            
        }

        #endregion

        #region Packet event listeners

        /// <summary>
        /// Awaits the reception of a packet on the packet write characteristic
        /// of the packet write service of the local GATT server.
        /// 
        /// This is so a server can know when a packet has been received and
        /// deal with it accordingly.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="eventArgs"></param>
        private void WatchForPacketReception(
            object                      sender,
            PropertyChangedEventArgs    eventArgs
        )
        {
            //if (sender == localPacketWriteCharacteristic)
            //{
            //    if (eventArgs.PropertyName == "Packet")
            //    {
            //        byte[] packet = localPacketWriteCharacteristic.Packet;

            //        Debug.WriteLine("Received this packet over " +
            //                         "Bluetooth: " + Utilities.BytesToString(packet));

            //        // Only send it back out if this device is not the destination;
            //        // in other words, if this device is a middle router in the
            //        // subnet
            //        IPAddress destinationAddress = GetDestinationAddressFromPacket(
            //                                            packet
            //                                        );

            //        // Check if the packet is NOT for this device
            //        bool packetIsForThisDevice = false;

            //        packetIsForThisDevice = IPAddress.Equals(destinationAddress, generatedLocalIPv6AddressForNode);

            //        if (!packetIsForThisDevice)
            //        {
            //            // Check if the message is in the local message cache or not
            //            if (messageCache.Contains(packet))
            //            {
            //                Debug.WriteLine("This packet is not for this device and" +
            //                                " has been seen before."
            //                                );
            //                return;
            //            }

            //            // If this message has not been seen before, add it to the
            //            // message queue and remove the oldest if there would now
            //            // be more than 10
            //            if (messageCache.Count < 10)
            //            {
            //                messageCache.Enqueue(packet);
            //            }
            //            else
            //            {
            //                messageCache.Dequeue();
            //                messageCache.Enqueue(packet);
            //            }

            //           await SendPacketOverBluetoothLE(packet,
            //                                           destinationAddress
            //                                           );
            //        }
            //        else
            //        {
            //            // It's for this device. Check if it has been seen before
            //            // or not.

            //            // Check if the message is in the local message cache or not
            //            if (messageCache.Contains(packet))
            //            {
            //                Debug.WriteLine("This packet is for this device, but " +
            //                                "has been seen before."
            //                                );
            //                return;
            //            }

            //            // If this message has not been seen before, add it to the
            //            // message queue and remove the oldest if there would now
            //            // be more than 10
            //            if (messageCache.Count < 10)
            //            {
            //                messageCache.Enqueue(packet);
            //            }
            //            else
            //            {
            //                messageCache.Dequeue();
            //                messageCache.Enqueue(packet);
            //            }

            //            // Send the packet to the driver for inbound injection
            //            SendPacketToDriverForInboundInjection(packet);
            //        }
            //    }
            // }
        }

        #endregion

        #region Bluetooth LE operations
        /// <summary>
        /// Sends a packet out over Bluetooth Low Energy. Spins up a watcher
        /// for advertisements from nearby servers who can receive the packet.
        /// </summary>
        /// <param name="packet"></param>
        internal async Task SendPacketOverBluetoothLE(
            byte[] packet,
            IPAddress destinationAddress
        )
        {
            Debug.WriteLine("Starting to send packet over BLE.");

            // 
            // Step 1
            // Check if there are any supported devices to which to write
            //
            if(supportedBleDevices == null || supportedBleDevices.Count == 0)
            {
                // Re-scan if there is no one on record to which to send (as
                // another device may have come online since the last time)
                Debug.WriteLine("There were no remote devices to which to " +
                                "write this packet. Re-scanning in case new" +
                                " ones have come online since the last time."
                                );

                await EnumerateNearbySupportedDevices();

                // If still nothing, then do nothing
                if(supportedBleDevices == null || supportedBleDevices.Count == 0)
                {
                    Debug.WriteLine("Still no remote devices to which to " +
                                    "write this packet. Aborting attempt."
                                    );
                    return;
                }                
            }

            //
            // Step 2
            // Check if the packet is for a device immediately in range of
            // this device (optimization to avoid unnecessary future
            // broadcasts if target is a neighbor of this device)
            //
            bool targetIsNeighbor = false;
            bool packetTransmittedSuccessfully = false;

            foreach (IPAddress address in supportedBleDevices.Keys)
            {
                if(IPAddress.Equals(address, destinationAddress))
                {
                    targetIsNeighbor = true;

                    try
                    {
                        // Send the packet to the device if it is the target
                        packetTransmittedSuccessfully = await PacketWriter.WritePacketAsync(supportedBleDevices[address],
                                                                                            packet
                                                                                            );
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine("Exception ocurred while trying to " +
                                        "transmit the packet over BLE. \n" +
                                        "\tException message: " + e.Message
                                        );
                    }

                    // Check transmission status
                    if (!packetTransmittedSuccessfully)
                    {
                        Debug.WriteLine("Could not transmit this packet: " +
                                        Utilities.BytesToString(packet) +
                                        "\n\t to this address: " +
                                        destinationAddress.ToString()
                                        );
                    }
                    else
                    {
                        // We successfully transmitted the packet! Cue fireworks.
                        Debug.WriteLine("Successfully transmitted this " +
                                        "packet:" + Utilities.BytesToString(packet) +
                                        "\n\t to this address:" +
                                        destinationAddress.ToString()
                                        );
                    }

                    break;
                }
            }

            //
            // Step 3
            // Send the packet to all devices in range if the target is not an
            // immediate neighbor
            //
            if(!targetIsNeighbor)
            {
                foreach(DeviceInformation device in supportedBleDevices.Values)
                {
                    try
                    {
                        packetTransmittedSuccessfully = await PacketWriter.WritePacketAsync(device,
                                                                                            packet
                                                                                            );
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine("Exception ocurred while trying to " +
                                        "transmit the packet over BLE. \n" +
                                        "Exception: " + e.Message
                                        );
                    }                    

                    // Check transmission status
                    if (!packetTransmittedSuccessfully)
                    {
                        Debug.WriteLine("Could not transmit this packet: " +
                                        Utilities.BytesToString(packet) +
                                        " to this address: " +
                                        destinationAddress.ToString()
                                        );
                    }
                    else
                    {
                        // We successfully transmitted the packet! Cue fireworks.
                        Debug.WriteLine("Successfully transmitted this " +
                                       "packet:" + Utilities.BytesToString(packet) +
                                        " to this address:" +
                                        destinationAddress.ToString()
                                        );
                    }
                }
            }            
        }
        #endregion

        #region Driver operations
        
        private void SendListenRequestToDriver()
        {
            //
            // Step 1
            // Open an async handle to the driver
            //
            SafeFileHandle device = null;
            try
            {
                device = DeviceIO.OpenDevice("\\\\.\\IPv6ToBle",
                                             true    // async
                                             );
            }
            catch (Win32Exception e)
            {
                Debug.WriteLine("Error opening handle to the driver. " +
                                "Error code: " + e.NativeErrorCode
                                );
                return;
            }

            //
            // Step 2
            // Begin an asynchronous operation to get a packet from the driver
            //
            IAsyncResult listenResult = DeviceIO.BeginGetPacketFromDriverAsync<byte[]>(
                                            device,
                                            IPv6ToBleIoctl.IOCTL_IPV6_TO_BLE_LISTEN_NETWORK_V6,
                                            1280, // 1280 bytes max
                                            PacketListenCompletionCallback,
                                            null // no specific state to track
                                        );    
            if(listenResult == null)
            {
                Debug.WriteLine("Invalid request to listen for a packet.");
            }
        }

        /// <summary>
        /// This callback is invoked when one of our previously sent listening
        /// requests is completed, indicating an async I/O operation has 
        /// finished. 
        /// 
        /// In other words, we should have a packet from the driver if the
        /// operation was successful.
        /// 
        /// This method is invoked by the thread pool thread that was waiting
        /// on the operation.
        /// </summary>
        private async void PacketListenCompletionCallback(
            IAsyncResult result
        )
        {
            //
            // Step 1
            // Retrieve the async result's...result
            //
            byte[] packet;
            try
            {
                packet = DeviceIO.EndGetPacketFromDriverAsync<byte>(result);
            }
            catch(Exception e)
            {
                Debug.WriteLine("Exception occurred. Source: " +
                                 e.Source + " " + "Message: " +
                                 e.Message
                                 );
                return;
            }
            

            //
            // Step 2
            // Send the packet over Bluetooth provided it's not null
            //
            if(packet != null)
            {
                IPAddress destinationAddress = GetDestinationAddressFromPacket(packet);
                if (destinationAddress != null)
                {
                    Debug.WriteLine("Packet received from driver. Packet length: " +
                                    packet.Length + ", Destination: " + 
                                    destinationAddress.ToString() + ", Contents: " +
                                    Utilities.BytesToString(packet)
                                    );

                    await SendPacketOverBluetoothLE(packet,
                                                    destinationAddress
                                                    );
                }
            }
            else
            {
                Debug.WriteLine("Packet received from driver was null. Some " +
                                "error must have occurred. Nothing to send over " +
                                "BLE."
                                );
                return;
            }

            //
            // Step 3
            // Send another listening request to the driver to replace this one
            //
            //Debug.WriteLine($"Sending listening request {++count}");
            //SendListenRequestToDriver();
        }

        private void SendPacketToDriverForInboundInjection(byte[] packet)
        {
            //
            // Step 1
            // Open a synchronous handle to the driver
            //
            SafeFileHandle device = null;
            try
            {
                device = DeviceIO.OpenDevice("\\\\.\\IPv6ToBle",
                                             false  // synchronous
                                             );
            }
            catch (Win32Exception e)
            {
                Debug.WriteLine("Error opening handle to the driver. " +
                                "Error code: " + e.NativeErrorCode
                                );
                return;
            }

            //
            // Step 2
            // Send the packet to the driver for it to inject into the inbound
            // network stack
            //
            DeviceIO.SynchronousControl(device,
                                        IPv6ToBleIoctl.IOCTL_IPV6_TO_BLE_INJECT_INBOUND_NETWORK_V6
                                        );
        }

        #endregion

        #region IPv6 address helpers
        /// <summary>
        /// Gets the local IPv6 addresses. This will retrieve the link-local
        /// IPv6 addresses from all devices if there is more than one
        /// adapter installed.
        /// 
        /// This method is modified from the accepted answer on this post on 
        /// Stack Overflow:
        /// 
        /// https://stackoverflow.com/questions/11411486/how-to-get-ipv4-and-ipv6-address-of-local-machine
        /// 
        /// NOTE: This method is for the border router only, as the other
        /// devices in the subnet use their generated IPv6 address that is
        /// based on the Bluetooth radio ID.
        /// </summary>
        /// <returns></returns>
        private List<IPAddress> GetLocalIPv6AddressesOnDesktop()
        {
            List<IPAddress> localAddresses = new List<IPAddress>();

            string hostNameString = Dns.GetHostName();
            IPHostEntry ipEntry = Dns.GetHostEntry(hostNameString);
            IPAddress[] addresses = ipEntry.AddressList;

            bool hasAtLeastOneIPv6Address = false;

            // Loop through all IPv6 addresses if this PC has more than one
            // interface for them
            foreach (IPAddress address in addresses)
            {
                if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                {
                    localAddresses.Add(address);
                    if (!hasAtLeastOneIPv6Address)
                    {
                        hasAtLeastOneIPv6Address = true;
                    }
                }
            }

            if (hasAtLeastOneIPv6Address)
            {
                return localAddresses;
            }
            else
            {
                return null;
            }
        }

        public IPAddress GetDestinationAddressFromPacket(byte[] packet)
        {
            if (packet.Length >= 49)
            {
                // Get the destination IPv6 address from the packet.
                // The destination address is the last 16 bytes of the
                // 40-byte long IPv6 header, so it is bytes 23-39
                byte[] destinationAddressBytes = new byte[16];
                Array.ConstrainedCopy(packet,
                                      23,
                                      destinationAddressBytes,
                                      0,
                                      16
                                      );
                return new IPAddress(destinationAddressBytes);
            }

            Debug.WriteLine("Packet was not long enough to extract " +
                            " IPv6 destination address."
                            );
            return null;
        }
        #endregion
    }
}
