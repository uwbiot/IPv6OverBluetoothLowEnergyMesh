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
using IPv6ToBleBluetoothGattLibraryForUWP;
using IPv6ToBleAdvLibraryForUWP;
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

        // Booleans to track whether the packet has finished transmitting, and
        // whether it was successful
        private bool packetTransmissionFinished = false;
        private bool packetTransmittedSuccessfully = false;

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
        private IPv6ToBleGattServer gattServer = null;

        // A Bluetooth Advertisement publisher to let nearby devices know we 
        // can receive a packet
        private IPv6ToBleAdvPublisherPacketReceive packetReceiver = null;

        // A FIFO queue of recent messages/packets to use with managed flooding
        private Queue<byte[]> messageCache = null;

        // Testing count for sending requests
        private int count = 0;

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
            //    gattServerStarted = Task.Run(gattServer.StartAsync).Result;
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

            //
            // Step 4
            // Set up the Bluetooth advertiser to let neighbors know we can
            // receive a packet if need be. For testing, this is always on.
            //
            //packetReceiver = new IPv6ToBleAdvPublisherPacketReceive();
            //packetReceiver.Start();

            //
            // Step 6
            // Initialize the message cache for 10 messages
            //
            messageCache = new Queue<byte[]>(10);
            
            //
            // Step 7
            // Send 10 initial listening requests to the driver
            //
            for(int i = 0; i < 10; i++)
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
            if (packetReceiver != null)
            {
                packetReceiver.Stop();
            }

            overallStatusBox.Text = "Stopped.";
        }
        #endregion

        #region Bluetooth advertisement event listeners

        /// <summary>
        /// Awaits the asynchronous packet transmission operations by listening
        /// for the event from the Adv watcher that signals whether the packet
        /// transmitted successfully.
        /// 
        /// This is so a client can know when the packet has finished the
        /// transmission process.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="eventArgs"></param>
        /// <returns></returns>
        private void WatchForPacketTransmission(
            IPv6ToBleAdvWatcherPacketWrite sender,
            PropertyChangedEventArgs eventArgs
        )
        {
            if (eventArgs.PropertyName == "TransmittedSuccessfully")
            {
                packetTransmissionFinished = true;
                packetTransmittedSuccessfully = sender.TransmittedSuccessfully;
            }

        }

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
            IPv6ToBlePacketWriteCharacteristic localPacketWriteCharacteristic,
            PropertyChangedEventArgs eventArgs
        )
        {
            if (eventArgs.PropertyName == "Packet")
            {
                byte[] packet = localPacketWriteCharacteristic.Packet;

                // Only send it back out if this device is not the destination;
                // in other words, if this device is a middle router in the
                // subnet
                IPAddress destinationAddress = GetDestinationAddressFromPacket(
                                                    packet
                                                );

                // Check if the packet is NOT for this device
                bool packetIsForThisDevice = false;

                packetIsForThisDevice = IPAddress.Equals(destinationAddress, generatedLocalIPv6AddressForNode);

                //foreach (IPAddress address in localIPv6AddressesForDesktop)
                //{
                //    if (IPAddress.Equals(address, destinationAddress))
                //    {
                //        packetIsForThisDevice = true;
                //    }
                //}

                if (!packetIsForThisDevice)
                {
                    // Check if the message is in the local message cache or not
                    if (messageCache.Contains(packet))
                    {
                        Debug.WriteLine("This packet has been seen before.");
                        return;
                    }

                    // If this message has not been seen before, add it to the
                    // message queue and remove the oldest if there would now
                    // be more than 10
                    if (messageCache.Count < 10)
                    {
                        messageCache.Enqueue(packet);
                    }
                    else
                    {
                        messageCache.Dequeue();
                        messageCache.Enqueue(packet);
                    }

                    SendPacketOverBluetoothLE(packet, destinationAddress);
                }
                else
                {
                    // It's for this device. Check if it has been seen before
                    // or not.

                    // Check if the message is in the local message cache or not
                    if (messageCache.Contains(packet))
                    {
                        //Debug.WriteLine("This packet has been seen before.");
                        overallStatusBox.Text = "This packet has been seen before.";
                        return;
                    }

                    // If this message has not been seen before, add it to the
                    // message queue and remove the oldest if there would now
                    // be more than 10
                    if (messageCache.Count < 10)
                    {
                        messageCache.Enqueue(packet);
                    }
                    else
                    {
                        messageCache.Dequeue();
                        messageCache.Enqueue(packet);
                    }

                    // DISPLAY THE PACKET!!
                    packetContentBox.Text = "Received this packet over " +
                                            "Bluetooth: " + Utilities.BytesToString(packet);

                    // Send the packet to the driver for inbound injection
                    SendPacketToDriverForInboundInjection(packet);
                }
            }
        }

        #endregion

        #region Bluetooth LE operations
        /// <summary>
        /// Sends a packet out over Bluetooth Low Energy. Spins up a watcher
        /// for advertisements from nearby servers who can receive the packet.
        /// </summary>
        /// <param name="packet"></param>
        public void SendPacketOverBluetoothLE(
            byte[] packet,
            IPAddress destinationAddress
        )
        {
            overallStatusBox.Text = "Starting to send packet over BLE.";

            // Create a packet watcher to watch for nearby recipients of the
            // packet
            IPv6ToBleAdvWatcherPacketWrite packetWriter = new IPv6ToBleAdvWatcherPacketWrite();

            // Start the watcher. This causes it to write the
            // packet when it finds a suitable recipient.
            packetWriter.Start(packet,
                               destinationAddress
                               );

            // Wait for the watcher to signal the event that the
            // packet has transmitted. This should happen after the
            // watcher receives an advertisement(s) OR a timeout of
            // 5 seconds occurs.
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            while (stopwatch.Elapsed < TimeSpan.FromSeconds(5)) ;
            stopwatch.Stop();

            // Check transmission status
            if (!packetTransmittedSuccessfully)
            {
                transmissionStatusBox.Text = "Could not transmit this packet: " +
                                            Utilities.BytesToString(packet) +
                                            " to this address: " +
                                            destinationAddress.ToString();
            }
            else
            {
                // We successfully transmitted the packet! Cue fireworks.
                transmissionStatusBox.Text = "Successfully transmitted this " +
                               "packet:" + Utilities.BytesToString(packet) +
                                "to this address:" +
                                destinationAddress.ToString();
            }

            // Stop the watcher
            packetWriter.Stop();

            // Reset the packet booleans for next time
            packetTransmissionFinished = false;
            packetTransmittedSuccessfully = false;
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
        private unsafe void PacketListenCompletionCallback(
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
                                 e.Message + " " + "Stack trace: " +
                                 e.StackTrace
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
                    SendPacketOverBluetoothLE(packet,
                                              destinationAddress
                                              );
                }
            }
            else
            {
                overallStatusBox.Text = "Packet was null. Some error must have" +
                                        "occurred. Nothing to send over BLE.";
                return;
            }

            //
            // Step 3
            // Send another listening request to the driver to replace this one
            //
            Debug.WriteLine($"Sending listening request {++count}");
            SendListenRequestToDriver();
        }

        private unsafe void SendPacketToDriverForInboundInjection(byte[] packet)
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
