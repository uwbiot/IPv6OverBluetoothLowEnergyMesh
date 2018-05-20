using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Http;
using System.Net;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Threading;
using System.ComponentModel;

//
// UWP namespaces
//
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


// The Background Application template is documented at http://go.microsoft.com/fwlink/?LinkID=533884&clcid=0x409

namespace IPv6ToBlePacketProcessingForIoTCore
{
    public sealed class StartupTask : IBackgroundTask
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

        // A deferral for this task to run forever
        private BackgroundTaskDeferral mainDeferral = null;

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

        #endregion

        #region Main, initialization, and stop

        //---------------------------------------------------------------------
        // Run(), the entry point
        //---------------------------------------------------------------------

        /// <summary>
        /// The main method entry point of the IoT application.
        /// </summary>
        /// <param name="taskInstance"></param>
        public void Run(IBackgroundTaskInstance taskInstance)
        {
            mainDeferral = taskInstance.GetDeferral();
            taskInstance.Canceled += TaskInstance_Canceled;

            Init();
        }

        //---------------------------------------------------------------------
        // Initialization and task cancellation
        //---------------------------------------------------------------------

        /// <summary>
        /// Initializes local variables, like a constructor.
        /// </summary>
        private void Init()
        {
            //
            // Step 1
            // Acquire this device's IPv6 address from the local Bluetooth radio
            //
            generatedLocalIPv6AddressForNode = Task.Run(() => IPv6AddressFromBluetoothAddress.GenerateAsync(2)).Result;
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
            gattServer = new IPv6ToBleGattServer();
            bool gattServerStarted = false;
            try
            {
                gattServerStarted = Task.Run(gattServer.StartAsync).Result;
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

            //
            // Step 4
            // Set up the Bluetooth advertiser to let neighbors know we can
            // receive a packet if need be. For testing, this is always on.
            //
            packetReceiver = new IPv6ToBleAdvPublisherPacketReceive();
            packetReceiver.Start();

            //
            // Step 6
            // Initialize the message cache for 10 messages
            //
            messageCache = new Queue<byte[]>(10);

            //
            // Step 7
            // Send 10 initial listening requests to the driver
            //
            for (int i = 0; i < 10; i++)
            {
                SendListenRequestToDriver();
                Thread.Sleep(100); // Wait 1/10 second to start another one
            }
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
            if(packetReceiver != null)
            {
                packetReceiver.Stop();
            }

            //
            // Step 2
            // Complete the deferral
            //
            mainDeferral.Complete();
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

                    // DISPLAY THE PACKET!!
                    Debug.WriteLine("Received this packet over " +
                                            "Bluetooth: " + Utilities.BytesToString(packet));

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
        internal void SendPacketOverBluetoothLE(
            byte[] packet,
            IPAddress destinationAddress
        )
        {
            Debug.WriteLine("Starting to send packet over BLE.");

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
                Debug.WriteLine("Could not transmit this packet: " +
                                            Utilities.BytesToString(packet) +
                                            " to this address: " +
                                            destinationAddress.ToString());
            }
            else
            {
                // We successfully transmitted the packet! Cue fireworks.
                Debug.WriteLine("Successfully transmitted this " +
                               "packet:" + Utilities.BytesToString(packet) +
                                "to this address:" +
                                destinationAddress.ToString());
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
            Debug.WriteLine("Sending listening request to driver.");

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
                packet = DeviceIO.EndGetPacketFromDriverAsync<byte[]>(result);
            }
            catch (Exception e)
            {
                Debug.WriteLine("Exception occurred.\n" +
                                "Source: " + e.Source + "\n" + 
                                "Message: " + e.Message + "\n" +
                                "Stack trace: " + e.StackTrace + "\n"
                                );
                return;
            }


            //
            // Step 2
            // Send the packet over Bluetooth provided it's not null
            //
            if (packet != null)
            {
                Debug.WriteLine("Got a packet! Contents: " + Utilities.BytesToString(packet));
                IPAddress destinationAddress = GetDestinationAddressFromPacket(packet);
                SendPacketOverBluetoothLE(packet,
                                          destinationAddress
                                          );
            }
            else
            {
                Debug.WriteLine("Packet was null. Some error must have" +
                                        "occurred. Nothing to send over BLE.");
                return;
            }

            //
            // Step 3
            // Send another listening request to the driver to replace this one
            //
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
        #endregion
    }
}
