using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Http;
using System.Net;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Threading;
using System.ComponentModel;

using Microsoft.Win32.SafeHandles;

//
// UWP namespaces
//
using Windows.ApplicationModel.Background;
using Windows.Devices.Enumeration;

//
// Namespaces for this project
//
using IPv6ToBleBluetoothGattLibraryForUWP.Server;
using IPv6ToBleBluetoothGattLibraryForUWP.Client;
using IPv6ToBleBluetoothGattLibraryForUWP.Characteristics;
using IPv6ToBleBluetoothGattLibraryForUWP.Helpers;
using IPv6ToBleSixLowPanLibraryForUWP;
using IPv6ToBleDriverInterfaceForUWP;
using IPv6ToBleDriverInterfaceForUWP.DeviceIO;


// The Background Application template is documented at http://go.microsoft.com/fwlink/?LinkID=533884&clcid=0x409

namespace IPv6ToBlePacketProcessingForIoTCore
{
    public sealed class StartupTask : IBackgroundTask
    {
        #region Local variables
        //---------------------------------------------------------------------
        // General variables
        //---------------------------------------------------------------------

        // A list of this device's link-local IPv6 addresses (in case it has
        // more than one adapter with an IPv6 address)
        //private List<IPAddress> localIPv6AddressesForDesktop = null;

        // This device's generated link-local IPv6 address
        private IPAddress generatedLocalIPv6AddressForNode = null;

        // A deferral for this task to run forever
        private BackgroundTaskDeferral mainDeferral = null;

        // Count of listening requests sent to driver, for debugging
        private int count = 0;

        // A device enumerator to find nearby devices on startup
        private DeviceEnumerator enumerator;

        // Dictionary of Bluetooth LE device objects and their IP addresses to 
        // match found device information
        private Dictionary<IPAddress, DeviceInformation> supportedBleDevices = new Dictionary<IPAddress, DeviceInformation>();

        // Tracker to know when device enumeration is complete
        private bool enumerationCompleted = false;

        //---------------------------------------------------------------------
        // Bluetooth variables
        //---------------------------------------------------------------------

        // The GATT server to receive packets and provide information to
        // remote devices
        private GattServer gattServer = null;

        // The local packet write characteristic, part of the packet processing
        // service in the GATT server
        static IPv6ToBlePacketWriteCharacteristic localPacketWriteCharacteristic;

        // A Bluetooth Advertisement watcher to write packets received from the
        // driver over Bluetooth LE
        //static IPv6ToBleAdvWatcherPacketWrite packetWriter;

        // A FIFO queue of recent messages/packets to use with managed flooding
        private Queue<byte[]> messageCache = null;

        #endregion

        #region Main, initialization, and stop

        //---------------------------------------------------------------------
        // Run(), the entry point
        //---------------------------------------------------------------------

        /// <summary>
        /// The main method entry point of the IoT application.
        /// </summary>
        /// <param name="taskInstance"></param>
        public async void Run(IBackgroundTaskInstance taskInstance)
        {
            mainDeferral = taskInstance.GetDeferral();
            taskInstance.Canceled += TaskInstance_Canceled;

            await Init();
        }

        //---------------------------------------------------------------------
        // Initialization and task cancellation
        //---------------------------------------------------------------------

        /// <summary>
        /// Initializes local variables, like a constructor.
        /// </summary>
        private async Task Init()
        {
            //
            // Step 1
            // Acquire this device's IPv6 address from the local Bluetooth radio
            //
            generatedLocalIPv6AddressForNode = await StatelessAddressConfiguration.GenerateLinkLocalAddressFromBlThRadioIdAsync(2);
            if (generatedLocalIPv6AddressForNode == null)
            {
                Debug.WriteLine("Could not generate the local IPv6 address.");
                throw new Exception();
            }

            //
            // Step 3
            // Spin up the GATT server service to listen for later replies
            // over Bluetooth LE
            //
            gattServer = new GattServer();

            bool gattServerStarted = false;
            try
            {
                gattServerStarted = await gattServer.StartAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error starting GATT server with this" +
                                 "message: " + ex.Message);
            }

            if (!gattServerStarted)
            {
                Debug.WriteLine("Could not start the GATT server.");
                throw new Exception();
            }
            else
            {
                Debug.WriteLine("GATT server started.");
            }

            // Get a handle to the local packet write characteristic
            localPacketWriteCharacteristic = gattServer.PacketProcessingService.PacketWriteCharacteristic;

            // Subscribe to the characteristic's "packet received" event
            localPacketWriteCharacteristic.PropertyChanged += WatchForPacketReception;

            //
            // Step 4
            // Enumerate nearby supported devices
            //
            //await EnumerateNearbySupportedDevices();            

            //
            // Step 5
            // Initialize the message cache for 10 messages
            //
            messageCache = new Queue<byte[]>(10);

            //
            // Step 6
            // Send 10 initial listening requests to the driver
            //
            //for (int i = 0; i < 10; i++)
            //{
            //    Debug.WriteLine($"Sending listening request {++count}");
            //    SendListenRequestToDriver();
            //    Thread.Sleep(100); // Wait 1/10 second to start another one
            //}
        }

        /// <summary>
        /// Invoked as a cancellation callback when the background task is
        /// cancelled for any reason, for a graceful exit.
        /// 
        /// We don't care about the particular reason (i.e. the app was
        /// aborted, system shutdown, etc.) so we don't need to do different
        /// behavior depending on the reason.
        /// </summary>
        /// <param name="sender">The background task instance.</param>
        /// <param name="reason">The reason for thread cancellation.</param>
        private void TaskInstance_Canceled(
            IBackgroundTaskInstance sender,
            BackgroundTaskCancellationReason reason
        )
        {
            //
            // Step 1
            // Shut down Bluetooth resources upon exit
            //

            // Stop the GATT server
            if (gattServer != null)
            {
                gattServer.Stop();
            }

            // Unsubscribe from the local packet write characteristic's
            // packet received event
            localPacketWriteCharacteristic.PropertyChanged -= WatchForPacketReception;

            //
            // Step 2
            // Stop device enumeration if it is still in progress when this
            // background app is canceled
            //
            if(enumerator != null)
            {
                enumerator.StopBleDeviceWatcher();
                enumerator.EnumerationCompleted -= WatchForEnumerationCompletion;
            }

            //
            // Step 2
            // Complete the deferral
            //
            mainDeferral.Complete();
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
            enumerator.StartBleDeviceWatcher();

            // Spin while enumeration is in progress
            while (!enumerationCompleted) ;

            Debug.WriteLine("Enumeration of nearby supported devices" +
                            " complete."
                            );

            // Filter found devices for supported ones
            Debug.WriteLine("Filtering devices for supported services...");
            await enumerator.PopulateSupportedDevices();
            Debug.WriteLine("Filtering for supported devices complete.");

            // Stop the device watcher            
            enumerator.StopBleDeviceWatcher();

            if (enumerator.SupportedBleDevices != null)
            {
                supportedBleDevices = enumerator.SupportedBleDevices;
                Debug.WriteLine($"Found {supportedBleDevices.Count} devices");
            }
            else
            {
                Debug.WriteLine("No nearby supported devices found.");
            }

            if(enumerator != null)
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
        private async void WatchForPacketReception(
            object                      sender,
            PropertyChangedEventArgs    eventArgs
        )
        {
            if(sender == localPacketWriteCharacteristic)
            {
                if (eventArgs.PropertyName == "Packet")
                {
                    byte[] packet = localPacketWriteCharacteristic.Packet;

                    Debug.WriteLine("Received this packet over " +
                                     "Bluetooth: " + Utilities.BytesToString(packet));

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
                if (IPAddress.Equals(address, destinationAddress))
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
            // Step 3
            // Send the packet to all devices in range if the target is not an
            // immediate neighbor
            //
            if (!targetIsNeighbor)
            {
                foreach (DeviceInformation device in supportedBleDevices.Values)
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
                                        "to this address:" +
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
                                        "Error code: " + e.NativeErrorCode);
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
                                            null
                                        );
            if (listenResult == null)
            {
                Debug.WriteLine("Invalid input for listening for a packet.");
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
                packet = DeviceIO.EndGetPacketFromDriverAsync<byte[]>(result);
            }
            catch (Exception e)
            {
                Debug.WriteLine("Exception occurred.\n" +
                                "Source: " + e.Source + "\n" + 
                                "Message: " + e.Message + "\n"
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
                                        "Error code: " + e.NativeErrorCode);
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

        internal IPAddress GetDestinationAddressFromPacket(byte[] packet)
        {
            // 40 bytes for IPv6 header, 8 bytes for UDP, min 1 byte payload
            if(packet.Length >= 49)
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

            Debug.WriteLine("Packet was not long enough to extract an IPv6 " +
                            "destination address.");
            return null;
        }
        #endregion
    }
}
