using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Net;

// Our GATT library
using IPv6ToBleBluetoothGattLibraryForDesktop;
using IPv6ToBleBluetoothGattLibraryForDesktop.Helpers;

// UWP namespaces
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace AdvLibraryForDesktop
{
    /// <summary>
    /// A class to watch for Bluetooth advertisements from publishers who are
    /// advertising that they are ready to receive a packet, so we can write
    /// the packet to them.
    /// 
    /// Note that, for the IPv6 Over Bluetooth Low Energy Mesh project,
    /// this is only called from processes that are inherently always running
    /// in the background (headless). 
    /// 
    /// On a Windows Desktop device (a laptop or desktop), usually the border
    /// router, this is run from an always-running Windows Service. On Windows 
    /// 10 IoT Core, which could either be a router in the mesh or a node
    /// device, this is run from a background IoT UWP app, which are specially
    /// permitted to always run and auto-start compared to normal UWP apps.
    /// 
    /// Therefore, we can simply use the standard BLE Advertisement APIs 
    /// instead of having to go out of our way to create background task
    /// workers and manage their lifetimes.
    /// 
    /// The behavior of the IPv6 Over Bluetooth Low Energy project is such that
    /// a server/recipient of a packet will publish advertisements when it is
    /// ready, and a client/sender will watch for those advertisements when it
    /// has a packet.
    /// 
    /// When a device receives a packet, i.e. a router, BEFORE setting up this
    /// watcher it checks if the packet is destined for that device and only
    /// sets up this watcher if it is not.
    /// </summary>
    public class IPv6ToBleAdvWatcherPacketWrite
    {
        #region Local variables
        //---------------------------------------------------------------------
        // Local variables
        //---------------------------------------------------------------------

        // The watcher itself
        private BluetoothLEAdvertisementWatcher watcher;

        // A boolean to mark whether we successfully transmitted a packet
        // upon receiving an advertisement from a nearby node/server
        private bool transmittedSuccessfully = false;

        // Getter for transmittedSuccessfully so the caller can check this
        public bool TransmittedSuccessfully
        {
            get
            {
                return transmittedSuccessfully;
            }

            private set
            {
                if(transmittedSuccessfully != value)
                {
                    transmittedSuccessfully = value;
                }
            }
        }

        // An IPv6 packet, passed by the caller
        private byte[] packet = new byte[1280];

        // Getter and setter for the packet
        public byte[] Packet
        {
            get
            {
                return packet;
            }

            set
            {
                if(Utilities.PacketsEqual(value, packet))
                {
                    packet = value;
                }
            }
        }

        // The destination address of Packet
        private IPAddress destinationAddress;

        // Setter for the destination address
        public IPAddress DestinationAddress
        {
            private get
            {
                return destinationAddress;
            }

            set
            {
                if(!IPAddress.Equals(destinationAddress, value))
                {
                    destinationAddress = value;
                }
            }
        }

        // The static routing table of the host
        private Dictionary<IPAddress, List<IPAddress>> staticRoutingTable = new Dictionary<IPAddress, List<IPAddress>>();

        // Setter for the static routing table (provided by caller)
        public Dictionary<IPAddress, List<IPAddress>> StaticRoutingTable
        {
            private get
            {
                return staticRoutingTable;
            }
            set
            {
                if(!Equals(staticRoutingTable, value))
                {
                    staticRoutingTable = value;
                }
            }
        }

        #endregion

        #region Init, shut down, and start/stop
        //---------------------------------------------------------------------
        // Init and ShutDown.
        //---------------------------------------------------------------------

        /// <summary>
        /// Constructs the watcher and begins watching for devices to which to
        /// write a packet.
        /// </summary>
        /// <param name="packet">The packet we'd like to write to a remote device.</param>
        /// <param name="destinationAddress">The destination address of the packet.</param>
        /// <param name="staticRoutingTable">The local host's static routing table of devices in the subnet.</param>
        public void Start(
            byte[]                                  packet,
            IPAddress                               destinationAddress,
            Dictionary<IPAddress, List<IPAddress>>  staticRoutingTable
        )
        {
            //
            // Step 1
            // Initialize local variables from the arguments
            //
            Packet = packet;
            DestinationAddress = destinationAddress;
            StaticRoutingTable = staticRoutingTable;

            //
            // Step 2
            // Create the watcher for active scanning
            //
            watcher = new BluetoothLEAdvertisementWatcher
            {
                ScanningMode = BluetoothLEScanningMode.Active
            };

            //
            // Step 3
            // Configure the watcher to look only for advertisements from our
            // IPv6ToBle publisher. This is based on the assumption that all
            // devices in this network run Windows, and we don't want to
            // receive all advertisements around us.
            //
            // ***
            // If you do want to work with non-Windows devices or listen to all
            // advertisements, comment out the rest of Step 3.
            // ***
            //

            // Define the manufacturer data against which we want to match
            BluetoothLEManufacturerData manufacturerData = new BluetoothLEManufacturerData();

            // Set the company ID for the manufacturer data to be the same as
            // we set in the IPv6ToBleAdvertisementPublisher class
            manufacturerData.CompanyId = 0xDEDE;

            // Set the payload to be the same payload string we specified in the
            // publisher
            DataWriter writer = new DataWriter();
            writer.WriteString("IPv6ToBle");

            manufacturerData.Data = writer.DetachBuffer();

            // Add the manufacturer data to the advertisement filter on the watcher
            watcher.AdvertisementFilter.Advertisement.ManufacturerData.Add(manufacturerData);

            // Adjust the signal strength filter. Basing this off the 
            // BluetoothAdvertisement sample from Microsoft: >= -70dBm is
            // "in range," < -75dBm is "out of range," out-of-range timeout
            // is 2 seconds.
            watcher.SignalStrengthFilter.InRangeThresholdInDBm = -70;
            watcher.SignalStrengthFilter.OutOfRangeThresholdInDBm = -75;
            watcher.SignalStrengthFilter.OutOfRangeTimeout = TimeSpan.FromMilliseconds(2000);

            //
            // Step 4
            // Hook up event handlers and start the watcher
            // 
            watcher.Received += OnAdvertisementReceived;
            watcher.Stopped += OnAdvertisementStopped;

            watcher.Start();
        }

        // Destroys the object and stops the watcher. This MUST be called after
        // the client is done looking for nearby devices or the watcher
        // will keep running forever, leak resources, and consume power.
        public void Stop()
        {
            // Stop the watcher
            watcher.Stop();

            // Unhook event handlers to prevent resource leaks
            watcher.Received -= OnAdvertisementReceived;
            watcher.Stopped -= OnAdvertisementStopped;
        }

        #endregion

        #region Event handlers
        //---------------------------------------------------------------------
        // Event handlers for advertisements
        //---------------------------------------------------------------------

        //
        // Specifies behavior for when an advertisement is received by the
        // watcher. Because the IPv6 Over Bluetooth Low Energy project is based
        // on 6LoWPAN principles, routers actively watch for possible
        // recipients when they have a packet to deliver. Receiving an
        // advertisement means that there is a suitable node (or other router)
        // within range that can receive the packet, so we transmit it here.
        //
        private async void OnAdvertisementReceived(
            BluetoothLEAdvertisementWatcher             watcher,
            BluetoothLEAdvertisementReceivedEventArgs   eventArgs
        )
        {
            BluetoothError status = BluetoothError.Success;

            // Variables for the remote device, the IPv6ToBle packet processing
            // service, IPSS, and the device's characteristics
            BluetoothLEDevice device = null;
            GattDeviceService internetProtocolSupportService = null;
            GattDeviceService ipv6ToBlePacketProcessingService = null;
            IReadOnlyList<GattCharacteristic> deviceCharacteristics = null;

            // Variables to hold the characteristics with which we need to
            // interact
            GattCharacteristic ipv6PacketWriteCharacteristic = null;
            GattCharacteristic ipv6AddressCharacteristic = null;
            IPAddress deviceAddress = null;

            //
            // Step 1
            // Verify we have recevied a proper advertisement from the server
            // by checking its manufacturer data. This is kind of redundant
            // because we filter advertisements based on this, but it's not a
            // bad idea.
            //
            IList<BluetoothLEManufacturerData> manufacturerDataList = eventArgs.Advertisement.ManufacturerData;
            if(manufacturerDataList.Count == 0)
            {
                status = BluetoothError.OtherError;
                Debug.WriteLine("No manufacturer data in the advertisement.");
                goto Exit;
            }
            else
            {
                // There should only be one if it's one of our advertisements
                BluetoothLEManufacturerData manufacturerData = manufacturerDataList[0];
                
                // Verify it's the IPv6ToBle manufacturer name
                string manufacturerDataString = manufacturerData.CompanyId.ToString();
                if(manufacturerDataString != "IPv6ToBle")
                {
                    status = BluetoothError.OtherError;
                    Debug.WriteLine("Manufacturer Company ID did not match " +
                                    "IPv6ToBle."
                                    );
                    goto Exit;
                }
            }

            //
            // Step 2
            // Connect to the device
            // 
            try
            {
                // Connect based on the device's Bluetooth address
                device = await BluetoothLEDevice.FromBluetoothAddressAsync(eventArgs.BluetoothAddress);

                if(device == null)
                {
                    status = BluetoothError.DeviceNotConnected;
                    Debug.WriteLine("Error connecting to device.");
                    goto Exit;
                }
            } catch (Exception e) when (e.HResult == Constants.E_DEVICE_NOT_AVAILABLE)
            {
                status = BluetoothError.RadioNotAvailable;
                Debug.WriteLine("Bluetooth radio is not on.");
                goto Exit;
            }

            //
            // Step 3
            // Enumerate the GATT services to get the 
            // IPv6ToBlePacketProcessingService and Internet Protocol Support
            // Service (IPSS)
            //
            if (device != null)
            {
                // Retrieve the list of services from the device (uncached)
                GattDeviceServicesResult servicesResult = await device.GetGattServicesAsync(BluetoothCacheMode.Uncached);

                if (servicesResult.Status == GattCommunicationStatus.Success)
                {
                    var services = servicesResult.Services;
                    Debug.WriteLine($"Found {services.Count} services");

                    // Iterate through the list of services and check if
                    // both services we require are there
                    foreach (GattDeviceService service in services)
                    {
                        Guid uuid = service.Uuid;

                        // Check for IPv6ToBle Packet Write Service
                        if (uuid == Constants.IPv6ToBlePacketProcessingServiceUuid)
                        {
                            ipv6ToBlePacketProcessingService = service;
                            continue;
                        }

                        // Check for IPSS
                        ushort shortId = GattHelpers.ConvertUuidToShortId(uuid);
                        if(shortId == (ushort)GattHelpers.SigAssignedGattNativeUuid.InternetProtocolSupport)
                        {
                            internetProtocolSupportService = service;
                            continue;
                        }
                    }
                }
            }

            // Report error if the device was not running our packet processing
            // service for some reason
            if(ipv6ToBlePacketProcessingService == null ||
                internetProtocolSupportService == null)
            {
                status = BluetoothError.OtherError;
                Debug.WriteLine("Device did not have the " +
                                "IPv6ToBlePacketProcessingService running or" +
                                " available, or did not have the Internet" +
                                " Protocol Support Service running or " +
                                "available."
                                );
                goto Exit;
            }

            //
            // Step 4
            // Enumerate the GATT characteristics
            //
            try
            {
                // Verify we can access the service
                DeviceAccessStatus accessStatus = await ipv6ToBlePacketProcessingService.RequestAccessAsync();
                if (accessStatus == DeviceAccessStatus.Allowed)
                {
                    // Enumerate the characteristics
                    GattCharacteristicsResult characteristicsResult =
                        await ipv6ToBlePacketProcessingService.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);

                    if (characteristicsResult.Status == GattCommunicationStatus.Success)
                    {
                        deviceCharacteristics = characteristicsResult.Characteristics;
                    }
                    else
                    {
                        status = BluetoothError.OtherError;
                        Debug.WriteLine("Could not access the packet " +
                                        "processing service."
                                        );
                        goto Exit;
                    }
                }
                else
                {
                    // Not granted access
                    status = BluetoothError.NotSupported;

                    Debug.WriteLine("Could not access the packet " +
                                    "processing service."
                                    );
                    goto Exit;
                }
            }
            catch (Exception e)
            {
                status = BluetoothError.DeviceNotConnected;
                Debug.WriteLine("Could not read characteristics due to " +
                                "permissions issues. " + e.Message
                                );
                goto Exit;
            }
            
            if(deviceCharacteristics != null)
            {
                // Find the IPv6 Address and packet write characteristics
                foreach (GattCharacteristic characteristic in deviceCharacteristics)
                {
                    if (characteristic.Uuid == Constants.IPv6ToBleIPv6AddressCharacteristicUuid)
                    {
                        ipv6AddressCharacteristic = characteristic;
                    }

                    if (characteristic.Uuid == Constants.IPv6ToBlePacketWriteCharacteristicUuid)
                    {
                        ipv6PacketWriteCharacteristic = characteristic;
                    }
                }
            }            

            if(ipv6PacketWriteCharacteristic == null ||
                ipv6AddressCharacteristic == null)
            {
                status = BluetoothError.OtherError;
                Debug.WriteLine("Could not access the IPv6 address" +
                                " characteristic and the packet write" +
                                " characteristic.");
                goto Exit;
            }

            // Get the device's IPv6 address from the characteristic
            if(ipv6AddressCharacteristic != null)
            {
                GattReadResult readResult = await ipv6AddressCharacteristic.ReadValueAsync();
                if(readResult.Status == GattCommunicationStatus.Success)
                {
                    deviceAddress = new IPAddress(GattHelpers.ConvertBufferToByteArray(readResult.Value));
                }
                else
                {
                    status = BluetoothError.OtherError;
                    Debug.WriteLine("Could not read the device's IPv6 address" +
                                    " from the remote characteristic."
                                    );
                    goto Exit;
                }
            }

            // Check if the device is either the destination itself or in the
            // path to the destination
            if(!IsDeviceTheDestinationOrInPathToDestination(deviceAddress,
                                                            DestinationAddress,
                                                            StaticRoutingTable
                                                            ))
            {
                status = BluetoothError.NotSupported;
                Debug.WriteLine("The device was not the destination, nor was" +
                                " it the in the path to the destination."
                                );
                goto Exit;
            }

            //
            // Step 5
            // Write the packet now that we have verified that the device is
            // supported and is either the destination or in the path to the
            // destination
            //
            GattCommunicationStatus writeStatus = 
                await ipv6PacketWriteCharacteristic.WriteValueAsync(GattHelpers.ConvertByteArrayToBuffer(Packet));
            if(writeStatus != GattCommunicationStatus.Success)
            {
                status = BluetoothError.OtherError;
                Debug.WriteLine("Could not write the IPv6 packet to the" +
                                " remote device");
            }

        Exit:
            
            if(status != BluetoothError.Success)
            {
                TransmittedSuccessfully = false;
            }
            else
            {
                TransmittedSuccessfully = true;
            }
        }

        // Method for when the watcher is stopped or aborted. Really, all we
        // do is write a debug line that this event has occurred.
        private void OnAdvertisementStopped(
            BluetoothLEAdvertisementWatcher                 watcher,
            BluetoothLEAdvertisementWatcherStoppedEventArgs eventArgs
        )
        {
            if(watcher.Status == BluetoothLEAdvertisementWatcherStatus.Aborted)
            {
                Debug.WriteLine("Watcher aborted due to inactivity.");
            }
            if(watcher.Status == BluetoothLEAdvertisementWatcherStatus.Stopped)
            {
                Debug.WriteLine("Watcher has been stopped.");
            }
        }
        #endregion

        #region Private helpers
        //---------------------------------------------------------------------
        // Helpers for finding the shortest path to a destination
        //---------------------------------------------------------------------

        /// <summary>
        /// Method to determine if a device's IPv6 address is part of a path
        /// to a given destination IPv6 address. This is used on router devices,
        /// including the border router, 
        ///
        /// For now, the static routing table is simply a dictionary that maps
        /// a given address and the complete path to get there from the border
        /// router.
        /// </summary>
        /// <returns>Returns true if the destination is either a neighbor or is
        /// in the path to the destination. False otherwise.</returns>
        private bool IsDeviceTheDestinationOrInPathToDestination(
            IPAddress                               deviceAddress,
            IPAddress                               destinationAddress,
            Dictionary<IPAddress, List<IPAddress>>  staticRoutingTable
        )
        {
            //
            // Step 1
            // Return true if this device is the destination
            //
            if(IPAddress.Equals(deviceAddress, destinationAddress))
            {
                return true;
            }

            //
            // Step 2
            // Otherwise, check if the device is in the path to the destination
            //            

            // Check for malformed input - the destination address must be in
            // the routing table

            if(!staticRoutingTable.ContainsKey(destinationAddress))
            {
                Debug.WriteLine("Malformed input; destination address does" +
                                "not exist in the static routing table."
                                );
                return false;
            }

            // Check if the device is in the path to the destination
            bool isInPath = false;
            foreach (IPAddress address in staticRoutingTable[destinationAddress])
            {
                if(IPAddress.Equals(address, deviceAddress))
                {
                    isInPath = true;
                    break;
                }
            }

            return isInPath;
        }
        #endregion
    }
}
