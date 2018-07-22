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
using Windows.Storage.Streams;

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

namespace PacketProcessing
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
        private GattServer gattServer = null;

        // A bool to track when the GATT server is started or not
        private bool gattServerStarted = false;

        // The local packet write characteristic, part of the packet processing
        // service in the GATT server
        private IPv6ToBlePacketWriteCharacteristic localPacketWriteCharacteristic;

        // The local characteristic to receive a compressed header's length
        private CompressedHeaderLengthCharacteristic localCompressedHeaderLengthCharacteristic;

        // The local characteristic to receive the payload length of a
        // compressed header
        private PayloadLengthCharacteristic localPayloadLengthCharacteristic;

        // A FIFO queue of recent messages/packets to use with managed flooding
        private Queue<byte[]> messageCache = null;

        // Testing count for sending requests
        private int count = 0;

        // A device enumerator to find nearby devices on startup
        private DeviceEnumerator enumerator;

        // Dictionary of Bluetooth LE device objects and their IP addresses to 
        // match found device information
        private Dictionary<IPAddress, DeviceInformation> supportedBleDevices = new Dictionary<IPAddress, DeviceInformation>();

        // Tracker to know when device enumeration is complete
        private bool enumerationCompleted = false;

        //---------------------------------------------------------------------
        // IPv6 packet variables
        //---------------------------------------------------------------------

        // An IPv6 packet. Max size is 1280 bytes, the MTU for Bluetooth in
        // general.
        private byte[] packet = new byte[1280];

        // Getter and setter for the packet
        public byte[] Packet
        {
            get
            {
                return packet;
            }

            private set
            {
                if (!Utilities.PacketsEqual(value, packet))
                {
                    packet = value;
                }
            }
        }

        // The compressed header length for a packet received over BLE that has
        // had its header compressed
        private int compressedHeaderLength = 0;

        public int CompressedHeaderLength
        {
            get
            {
                return compressedHeaderLength;
            }
            private set
            {
                if (compressedHeaderLength != value)
                {
                    compressedHeaderLength = value;
                }
            }
        }

        // The payload length for a packet received over BLE that has had its
        // header compressed. Needed to decompress on this side.
        private int payloadLength = 0;

        public int PayloadLength
        {
            get
            {
                return payloadLength;
            }
            private set
            {
                if (payloadLength != value)
                {
                    payloadLength = value;
                }
            }
        }

        // A header compression/decompression object from the 6LoWPAN library
        HeaderCompression headerCompression = new HeaderCompression();

        //---------------------------------------------------------------------
        // Testing variables
        //---------------------------------------------------------------------

        private Stopwatch bleTransmissionTimer = new Stopwatch();
        private Stopwatch bleReceptionTimer = new Stopwatch();

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
            generatedLocalIPv6AddressForNode = await StatelessAddressConfiguration.GenerateLinkLocalAddressFromBlThRadioIdAsync(2);
            if (generatedLocalIPv6AddressForNode == null)
            {
                overallStatusBox.Text = "Could not generate the local IPv6 address.";
                throw new Exception();
            }

            //localIPv6AddressesForDesktop = GetLocalIPv6AddressesOnDesktop();
            //if (localIPv6AddressesForDesktop == null)
            //{
            //    overallStatusBox.Text = "Could not acquire the local IPv6 address(es).";
            //    throw new Exception();
            //}

            //
            // Step 3
            // Enumerate nearby supported devices
            //
            await EnumerateNearbySupportedDevices();

            //
            // Step 4
            // Spin up the GATT server service to listen for later replies
            // over Bluetooth LE
            //
            gattServer = new GattServer();

            gattServerStarted = await StartGattServer();

            if (!gattServerStarted)
            {
                Debug.WriteLine("Aborting Init() because GATT server could " +
                                "not be started."
                                );
                return;
            }

            //
            // Step 5
            // Initialize the message cache for 10 messages
            //
            messageCache = new Queue<byte[]>(10);

            //
            // Step 6
            // Send 10 initial listening requests to the driver
            //
            //Debug.WriteLine($"Sending listening request {++count}");
            //SendListenRequestToDriver();

            overallStatusBox.Text = "Finished starting.";
        }

        /// <summary>
        /// A helper method to start the local GATT server.
        /// </summary>
        private async Task<bool> StartGattServer()
        {
            bool started = false;
            try
            {
                started = await gattServer.StartAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error starting GATT server with this" +
                                 "message: " + ex.Message);
            }

            if (!started)
            {
                Debug.WriteLine("Could not start the GATT server.");
            }
            else
            {
                Debug.WriteLine("GATT server started.");
            }

            // Get a handle to the local packet write characteristic and its siblings
            localPacketWriteCharacteristic = gattServer.PacketProcessingService.PacketWriteCharacteristic;
            localCompressedHeaderLengthCharacteristic = gattServer.PacketProcessingService.CompressedHeaderLengthCharacteristic;
            localPayloadLengthCharacteristic = gattServer.PacketProcessingService.PayloadLengthCharacteristic;

            // Subscribe to the characteristic's "packet received" event
            localPacketWriteCharacteristic.PropertyChanged += WatchForPacketReception;

            return started;
        }

        /// <summary>
        /// A helper method to stop the local GATT server.
        /// </summary>
        private void StopGattServer()
        {
            if (gattServerStarted)
            {
                gattServer.Stop();

                gattServerStarted = false;

                // Unsubscribe from the local packet write characteristic's
                // packet received event
                localPacketWriteCharacteristic.PropertyChanged -= WatchForPacketReception;
            }
        }

        private void stopButton_Click(object sender, RoutedEventArgs e)
        {
            //
            // Step 1
            // Shut down Bluetooth resources upon exit
            //
            StopGattServer();

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
            object sender,
            PropertyChangedEventArgs eventArgs
        )
        {
            if (sender.GetType() == typeof(DeviceEnumerator))
            {
                if (eventArgs.PropertyName == "EnumerationComplete")
                {
                    enumerationCompleted = true;
                }
            }
        }

        #endregion

        #region Bluetooth packet reception

        /// <summary>
        /// Awaits the reception of a packet on the packet write characteristic
        /// of the packet write service of the local GATT server.
        /// 
        /// This is so a server can know when a packet has been received and
        /// deal with it accordingly.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="eventArgs"></param>
        private async void WatchForPacketReception(
            object sender,
            PropertyChangedEventArgs eventArgs
        )
        {
            if (sender == localPacketWriteCharacteristic)
            {
                if (eventArgs.PropertyName == "Value")
                {
                    // Get the received packet
                    Packet = GattHelpers.ConvertBufferToByteArray(localPacketWriteCharacteristic.Value);

                    // Get the other two characteristics' info for decompressing
                    // the packet
                    //DataReader reader = DataReader.FromBuffer(localCompressedHeaderLengthCharacteristic.Value);
                    //CompressedHeaderLength = reader.ReadInt32();
                    //reader = DataReader.FromBuffer(localPayloadLengthCharacteristic.Value);
                    //PayloadLength = reader.ReadInt32();

                    Debug.WriteLine("Received this packet over " +
                                     "Bluetooth: " + Utilities.BytesToString(packet));

                    // TESTING: Start the timer for header decompression to time
                    // total transmission/reception time
                    //bleReceptionTimer.Start();

                    //// Decompress the packet
                    //try
                    //{
                    //    headerCompression.UncompressHeaderIphc(packet,
                    //                                       compressedHeaderLength,
                    //                                       payloadLength
                    //                                       );
                    //}
                    //catch (Exception e)
                    //{
                    //    Debug.WriteLine("Exception occurred during header " +
                    //                    "decompression. Message: " + e.Message
                    //                    );
                    //    bleReceptionTimer.Stop();
                    //    return;
                    //}

                    //bleReceptionTimer.Stop();
                    //Debug.WriteLine($"Header decompression took {bleReceptionTimer.ElapsedMilliseconds} milliseconds.");

                    // Only send it back out if this device is not the destination;
                    // in other words, if this device is a middle router in the
                    // subnet
                    IPAddress destinationAddress = GetDestinationAddressFromPacket(
                                                        packet
                                                    );

                    // Check if the packet is NOT for this device
                    bool packetIsForThisDevice = false;

                    packetIsForThisDevice = IPAddress.Equals(destinationAddress, generatedLocalIPv6AddressForNode);

                    if (!packetIsForThisDevice)
                    {
                        // Check if the message is in the local message cache or not
                        if (messageCache.Contains(packet))
                        {
                            Debug.WriteLine("This packet is not for this device and" +
                                            " has been seen before."
                                            );
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

                        await SendPacketOverBluetoothLE(packet,
                                                        destinationAddress
                                                        );
                    }
                    else
                    {
                        // It's for this device. Check if it has been seen before
                        // or not.

                        // Check if the message is in the local message cache or not
                        if (messageCache.Contains(packet))
                        {
                            Debug.WriteLine("This packet is for this device, but " +
                                            "has been seen before."
                                            );
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

                        // Send the packet to the driver for inbound injection
                        SendPacketToDriverForInboundInjection(packet);
                    }
                }
            }
        }

        #endregion

        #region Bluetooth packet transmission
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
            if (supportedBleDevices == null || supportedBleDevices.Count == 0)
            {
                // Re-scan if there is no one on record to which to send (as
                // another device may have come online since the last time)
                Debug.WriteLine("There were no remote devices to which to " +
                                "write this packet. Re-scanning in case new" +
                                " ones have come online since the last time."
                                );

                await EnumerateNearbySupportedDevices();

                // If still nothing, then do nothing
                if (supportedBleDevices == null || supportedBleDevices.Count == 0)
                {
                    Debug.WriteLine("Still no remote devices to which to " +
                                    "write this packet. Aborting attempt."
                                    );
                    return;
                }
            }

            if (supportedBleDevices == null || supportedBleDevices.Count == 0)
            {
                Debug.WriteLine("No supported devices in range to which to " +
                                "transmit this packet. Aborting."
                                );
                return;
            }

            //
            // Step 2
            // Compress the packet
            //
            //byte[] compressedPacket = null;
            //int compressedHeaderLength, payloadLength;
            //try
            //{
            //    compressedPacket = headerCompression.CompressHeaderIphc(packet,
            //                                                            out compressedHeaderLength,
            //                                                            out payloadLength
            //                                                            );
            //}
            //catch (Exception e)
            //{
            //    Debug.WriteLine("Exception occurred during header compression. " +
            //                    "Message: " + e.Message
            //                    );
            //    return;
            //}

            //if (compressedPacket == null)
            //{
            //    Debug.WriteLine("Error while compressing header. Compressed " +
            //                    "packet was null. Aborting."
            //                    );
            //    return;
            //}

            //
            // Step 3
            // Check if the packet is for a device immediately in range of
            // this device (optimization to avoid unnecessary future
            // broadcasts if target is a neighbor of this device)
            //
            bool targetIsNeighbor = false;
            bool packetTransmittedSuccessfully = false;

            foreach (IPAddress address in supportedBleDevices.Keys)
            {
                if (IPAddress.Equals(address, destinationAddress))
                {
                    targetIsNeighbor = true;

                    try
                    {
                        // Send the packet to the device if it is the target
                        packetTransmittedSuccessfully = await TestingPacketWriter.WritePacketAsync(supportedBleDevices[address],
                                                                                            packet,//compressedPacket,
                                                                                            compressedHeaderLength,
                                                                                            payloadLength
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
                                        "to this address:" +
                                        destinationAddress.ToString()
                                        );
                    }

                    break;
                }
            }

            //
            // Step 4
            // Send the packet to all devices in range if the target is not an
            // immediate neighbor
            //
            if (!targetIsNeighbor)
            {
                foreach (DeviceInformation device in supportedBleDevices.Values)
                {
                    try
                    {
                        packetTransmittedSuccessfully = await TestingPacketWriter.WritePacketAsync(device,
                                                                                            packet,//compressedPacket,
                                                                                            compressedHeaderLength,
                                                                                            payloadLength
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
                                        "to this address:" +
                                        destinationAddress.ToString()
                                        );
                    }

                    // Wait until sending the packet to the next target so as
                    // not to run over other devices trying to write to it
                    Thread.Sleep(1000);
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
            if (listenResult == null)
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
            catch (Exception e)
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
            if (packet != null)
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
            Debug.WriteLine($"Sending listening request {++count}");
            SendListenRequestToDriver();
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

        #region Packet header compression and decompression

        /// <summary>
        /// Compresses the IPv6 header of a full packet received from the
        /// driver.
        /// </summary>
        /// <param name="packet">The uncompressed packet.</param>
        /// <param name="processedHeaderLength">The length, in bytes, of the resultant compressed header.</param>
        /// <param name="payloadLength">The length of the packet's payload.</param>
        /// <returns></returns>
        private byte[] CompressHeader(
            ref byte[] packet,
            out int processedHeaderLength,
            out int payloadLength
         )
        {
            Debug.WriteLine("Original packet size: " + packet.Length);
            Debug.WriteLine("Original packet contents: " + Utilities.BytesToString(packet));
            Debug.WriteLine("Compressing packet...");

            byte[] compressedPacket = headerCompression.CompressHeaderIphc(packet,
                                                                           out processedHeaderLength,
                                                                           out payloadLength
                                                                           );

            Debug.WriteLine("Compression of packet complete.");

            if (compressedPacket == null)
            {
                Debug.WriteLine("Error occurred during packet compression. " +
                                "Unable to compress."
                                );
            }
            else
            {
                Debug.WriteLine("Compressed packet size: " + compressedPacket.Length);
                Debug.WriteLine("Compressed packet contents: " + Utilities.BytesToString(compressedPacket));
            }

            return compressedPacket;
        }

        /// <summary>
        /// Decompresses a compressed packet that was received over BLE back
        /// to its original uncompressed form.
        /// </summary>
        /// <param name="compressedPacket">The compressed packet.</param>
        /// <param name="processedHeaderLength">The length of the compressed header.</param>
        /// <param name="payloadLength">The length of the packet's payload.</param>
        /// <returns></returns>
        private byte[] DecompressHeader(
            ref byte[] compressedPacket,
            int processedHeaderLength,
            int payloadLength
        )
        {
            Debug.WriteLine("Decompressing packet...");

            byte[] uncompressedPacket = headerCompression.UncompressHeaderIphc(compressedPacket,
                                                                               processedHeaderLength,
                                                                               payloadLength
                                                                               );

            if (uncompressedPacket == null)
            {
                Debug.WriteLine("Error occurred during packet decompression.");
            }
            else
            {
                Debug.WriteLine("Uncompressed packet size: " + uncompressedPacket.Length);
                Debug.WriteLine("Uncompressed packet contents: " + Utilities.BytesToString(uncompressedPacket));

                Debug.WriteLine("Decompressed packet size is same as original: " + (packet.Length == uncompressedPacket.Length));
                Debug.WriteLine("Decompressed packet is identical to the original: " + (Utilities.PacketsEqual(packet, uncompressedPacket)));
            }

            return uncompressedPacket;
        }

        #endregion
    }
}
