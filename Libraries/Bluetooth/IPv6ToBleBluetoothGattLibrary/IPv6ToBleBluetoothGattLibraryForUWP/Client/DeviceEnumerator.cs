using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Net;

//
// Namespaces in this project
//
using IPv6ToBleBluetoothGattLibraryForUWP.Helpers;

//
// Bluetooth and device enumeration namespaces
//
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Foundation;

namespace IPv6ToBleBluetoothGattLibraryForUWP.Client
{
    /// <summary>
    /// This class represents GATT client functionality to search for and
    /// connect to nearby GATT servers. It also filters out supported devices;
    /// that is, devices that are running the IPv6ToBle packet processing
    /// service and the Internet Protocol Support Service (IPSS).
    /// 
    /// Callers use this class upon startup to discover nearby supported
    /// devices, facilitating subsequent connections to discovered devices
    /// later in an on-demand fashion.
    /// </summary>
    public class DeviceEnumerator
    {

        #region Local Variables
        //---------------------------------------------------------------------
        // Variables for device discovery
        //---------------------------------------------------------------------

        // List to store found devices
        private List<DeviceInformation> foundDevices = new List<DeviceInformation>();

        // Bool to track if enumeration is complete for caller
        private bool enumerationComplete = false;

        // Getter for isEnumerationComplete
        private bool EnumerationComplete
        {
            get
            {
                return enumerationComplete;
            }
            set
            {
                if (enumerationComplete != value)
                {
                    enumerationComplete = value;
                    EnumerationCompleteChanged(new PropertyChangedEventArgs("EnumerationComplete"));
                }
            }
        }

        // Callback to invoke public event to signify that enumeration is 
        // complete
        private void EnumerationCompleteChanged(PropertyChangedEventArgs args)
        {
            EnumerationCompleted?.Invoke(this, args);
        }

        // Public event to let a caller know when enumeration is complete
        public event PropertyChangedEventHandler EnumerationCompleted;

        // Counter for number of found devices
        private int count = 0;

        // Device watcher to enumerate nearby devices
        private DeviceWatcher deviceWatcher;

        //---------------------------------------------------------------------
        // Variables for filtering found devices
        //---------------------------------------------------------------------

        // Dictionary of Bluetooth LE device objects and their IP addresses to 
        // match found device information
        private Dictionary<IPAddress, DeviceInformation> supportedBleDevices = new Dictionary<IPAddress, DeviceInformation>();

        // Getter and setter for list of supported BLE devices (available to
        // caller)
        public Dictionary<IPAddress, DeviceInformation> SupportedBleDevices
        {
            get
            {
                return supportedBleDevices;
            }

            private set
            {
                if (supportedBleDevices != value)
                {
                    supportedBleDevices = value;
                }
            }
        }

        #endregion

        #region Device Discovery
        //---------------------------------------------------------------------
        // Methods for device discovery
        //---------------------------------------------------------------------

        // Start watcher to find all nearby Bluetooth devices (paired or
        // unpaired). Attaches event handlers to populate the device
        // collection.

        public void StartSupportedDeviceEnumerator()
        {
            //
            // Step 1
            // Prepare for device watcher creation
            //

            // Properties we'd like about the device
            string[] requestedProperties =
            {
                "System.Devices.Aep.DeviceAddress",
                "System.Devices.Aep.IsConnected",
                "System.Devices.Aep.Bluetooth.Le.IsConnectable"
            };

            // Query string for the type of device to scan for (BLE protocol)
            string aqsAllBluetoothLeDevices = "(System.Devices.Aep.ProtocolId:=\"{bb7bb05e-5972-42b5-94fc-76eaa7084d49}\")";

            //
            // Step 2
            // Create the device watcher
            //
            deviceWatcher = DeviceInformation.CreateWatcher(
                                aqsAllBluetoothLeDevices,
                                requestedProperties,
                                DeviceInformationKind.AssociationEndpoint
                            );
            //
            // Step 3
            // Register the watcher's event callbacks
            //
            deviceWatcher.Added += DeviceWatcher_Added;
            deviceWatcher.Updated += DeviceWatcher_Updated;
            deviceWatcher.Removed += DeviceWatcher_Removed;
            deviceWatcher.EnumerationCompleted += DeviceWatcher_EnumerationCompleted;

            //
            // Step 4
            // Clear the collection of devices from last time
            //
            foundDevices.Clear();

            //
            // Step 5
            // Start the watcher
            //
            deviceWatcher.Start();
        }

        // Stop watching for nearby Bluetooth LE devices
        public void StopSupportedDeviceEnumerator()
        {
            if (deviceWatcher != null)
            {
                //
                // Step 1
                // Unregister the event callbacks
                //
                deviceWatcher.Added -= DeviceWatcher_Added;
                deviceWatcher.Updated -= DeviceWatcher_Updated;
                deviceWatcher.Removed -= DeviceWatcher_Removed;
                deviceWatcher.EnumerationCompleted -= DeviceWatcher_EnumerationCompleted;

                //
                // Step 2
                // Stop the watcher
                //
                deviceWatcher.Stop();
                deviceWatcher = null;
            }
        }

        // Device watcher added callback
        private void DeviceWatcher_Added(
            DeviceWatcher sender,
            DeviceInformation deviceInfo
        )
        {
            Debug.WriteLine(String.Format("Added {0}{1}",
                                          deviceInfo.Id,
                                          deviceInfo.Name
                                          ));

            // Protect against the race condition if the task runs after the
            // caller stopped the deviceWatcher
            if (sender == deviceWatcher)
            {
                // Make sure the device isn't already in the list
                if (!foundDevices.Contains(deviceInfo))
                {
                    foundDevices.Add(deviceInfo);
                    count++;
                }
            }
        }

        // Device watcher updated callback
        private void DeviceWatcher_Updated(
            DeviceWatcher sender,
            DeviceInformationUpdate deviceInfoUpdate
        )
        {
            // Protect against the race condition if the task runs after the
            // caller stopped the deviceWatcher
            if (sender == deviceWatcher)
            {
                int newCount = 0;

                // Find the device and update it if it was the one that got updated
                foreach (DeviceInformation device in foundDevices)
                {
                    if (newCount < count)
                    {
                        if (foundDevices[newCount].Id == deviceInfoUpdate.Id)
                        {
                            // Update the device
                            foundDevices[newCount].Update(deviceInfoUpdate);
                        }
                    }

                    newCount++;
                }

                Debug.WriteLine("Enumeration updated.");
            }
        }

        // Device watcher removed callback
        private void DeviceWatcher_Removed(
            DeviceWatcher sender,
            DeviceInformationUpdate deviceInfoUpdate
        )
        {
            // Protect against the race condition if the task runs after the
            // caller stopped the deviceWatcher
            if (sender == deviceWatcher)
            {
                int newCount = 0;

                // Find the device and remove it
                foreach (DeviceInformation device in foundDevices)
                {
                    if (newCount < count)
                    {
                        if (foundDevices[newCount].Id == deviceInfoUpdate.Id)
                        {
                            // Remove the device
                            foundDevices.RemoveAt(newCount);
                            break;
                        }
                    }
                    newCount++;
                }

                count--;
            }
        }

        // Device watcher enumeration completed callback
        private void DeviceWatcher_EnumerationCompleted(
            DeviceWatcher sender,
            object args
        )
        {
            Debug.WriteLine("Enumeration complete.");

            EnumerationComplete = true;

            // Reset count for next time
            count = 0;
        }
        #endregion

        #region Filter Discovered Devices
        //---------------------------------------------------------------------
        // Methods for device connection after discovery. Enumerate services
        // on each discovered device and add supported devices to a dictionary
        // that maps devices to their link-local IPv6 addresses.
        //---------------------------------------------------------------------

        // Connects to each found device and enumerates available GATT
        // services, then only adds devices to the list that support both the
        // IPSSS and our IPv6ToBle packet writing service. This method is 
        // called after the initial device discovery phase.
        public async Task PopulateSupportedDevices()
        {
            //
            // Step 1
            // Check for empty list in case we couldn't find anything
            //
            if (foundDevices.Count == 0)
            {
                return;
            }

            //
            // Step 2
            // Connect to each previously found device and enumerate its
            // services. If it supports both IPSS and IPv6ToBle Packet Writing
            // Service, add it to the list.
            //
            foreach (DeviceInformation deviceInfo in foundDevices)
            {

                BluetoothLEDevice currentDevice = null;
                GattDeviceService ipv6ToBlePacketProcessingService = null;
                GattCharacteristic ipv6AddressCharacteristic = null;
                IPAddress ipv6Address = null;

                bool hasInternetProtocolSupportService = false;
                bool hasIPv6ToBlePacketWriteService = false;

                try
                {
                    // Connect. This is recommended to do on a UI thread
                    // normally because it may prompt for consent, but for our
                    // purposes in this application it will auto-accept and we
                    // don't have to use a UI thread.
                    currentDevice = await BluetoothLEDevice.FromIdAsync(deviceInfo.Id);

                    if (currentDevice == null)
                    {
                        Debug.WriteLine($"Failed to connect to device {deviceInfo.Id}");
                    }
                }
                catch (Exception e) when (e.HResult == Constants.E_DEVICE_NOT_AVAILABLE)
                {
                    Debug.WriteLine("Bluetooth radio is not on.");
                }

                // Enumerate the GATT services with GetGattServicesAsync
                if (currentDevice != null)
                {
                    // Retrieve the list of services from the device (uncached)
                    GattDeviceServicesResult servicesResult = await currentDevice.GetGattServicesAsync(BluetoothCacheMode.Uncached);

                    if (servicesResult.Status == GattCommunicationStatus.Success)
                    {
                        var services = servicesResult.Services;
                        Debug.WriteLine($"Found {services.Count} services for" +
                                        $" device {deviceInfo.Id}"
                                        );

                        // Iterate through the list of services and check if
                        // both services we require are there
                        foreach (GattDeviceService service in services)
                        {
                            Guid uuid = service.Uuid;

                            // Check for IPSS
                            ushort shortId = GattHelpers.ConvertUuidToShortId(uuid);
                            if (shortId == (ushort)GattHelpers.SigAssignedGattNativeUuid.InternetProtocolSupport)
                            {
                                hasInternetProtocolSupportService = true;
                                continue;
                            }

                            // Check for IPv6ToBle Packet Write Service
                            if (uuid == Constants.IPv6ToBlePacketProcessingServiceUuid)
                            {
                                hasIPv6ToBlePacketWriteService = true;
                                ipv6ToBlePacketProcessingService = service;
                                continue;
                            }
                        }
                    }
                }

                // Query the device's IP address - enumerate characteristics
                // for the IPv6ToBlePacketProcessingService and read from the
                // IPv6 address characteristic to map devices to their
                // addresses
                if (hasInternetProtocolSupportService &&
                    hasIPv6ToBlePacketWriteService &&
                    ipv6ToBlePacketProcessingService != null)
                {
                    IReadOnlyList<GattCharacteristic> characteristics = null;

                    try
                    {
                        // Verify we can access the device's packet processing service
                        DeviceAccessStatus accessStatus = await ipv6ToBlePacketProcessingService.RequestAccessAsync();
                        if (accessStatus == DeviceAccessStatus.Allowed)
                        {
                            // Enumerate the characteristics
                            GattCharacteristicsResult characteristicsResult =
                                await ipv6ToBlePacketProcessingService.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);

                            if (characteristicsResult.Status == GattCommunicationStatus.Success)
                            {
                                characteristics = characteristicsResult.Characteristics;
                            }
                            else
                            {
                                Debug.WriteLine("Could not access the packet" +
                                                " processing service."
                                                );
                                // On error, act as if there were no characteristics
                                characteristics = new List<GattCharacteristic>();
                            }
                        }
                        else
                        {
                            // Not granted access
                            Debug.WriteLine("Could not access the packet" +
                                            "processing service."
                                            );
                            // On error, act as if there were no characteristics
                            characteristics = new List<GattCharacteristic>();
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine("Could not read characteristics due to" +
                                        " permissions issues. " + e.Message
                                        );
                        // On error, act as if there were no characteristics
                        characteristics = new List<GattCharacteristic>();
                    }

                    // Find the IPv6 address characteristic
                    foreach (GattCharacteristic characteristic in characteristics)
                    {
                        if (characteristic.Uuid == Constants.IPv6ToBleIPv6AddressCharacteristicUuid)
                        {
                            ipv6AddressCharacteristic = characteristic;
                            break;
                        }
                    }

                    // Get the IPv6 address from the characteristic
                    if (ipv6AddressCharacteristic != null)
                    {
                        GattReadResult readResult = await ipv6AddressCharacteristic.ReadValueAsync();
                        if (readResult.Status == GattCommunicationStatus.Success)
                        {
                            ipv6Address = new IPAddress(GattHelpers.ConvertBufferToByteArray(readResult.Value));
                        }
                    }

                    // Finally, add the deviceInfo/IP address pair to the
                    // dictionary
                    if (ipv6Address != null)
                    {
                        supportedBleDevices.Add(ipv6Address, deviceInfo);
                    }
                }

                // Dispose of the service and device that we accessed, then force
                // a garbage collection to destroy the objects and fully disconnect
                // from the remote GATT server and device. This is as a workaround
                // for a current Windows bug that doesn't properly disconnect
                // devices, as well as a workaround for the Broadcomm Bluetooth LE
                // driver on the Raspberry Pi 3 that can't handle multiple connects
                // and reconnects if it thinks it's still occupied.
                //
                // Additionally, at this step, if you had connected any events
                // to the services or characteristics, you'd have to do that first.
                // But we didn't do that here, so no need.

                ipv6ToBlePacketProcessingService?.Dispose();
                currentDevice?.Dispose();
                currentDevice = null;
                GC.Collect();
            }
        }
        #endregion
    }
}