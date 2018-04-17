using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

namespace IPv6ToBleBluetoothGattLibraryForDesktop
{    
    //
    // Bluetooth and device enumeration namespaces
    //
    using Windows.Devices.Bluetooth;
    using Windows.Devices.Bluetooth.GenericAttributeProfile;
    using Windows.Devices.Enumeration;


    /// <summary>
    /// This class represents generic GATT client functionality for the
    /// IPv6 Over Bluetooth Low Energy Mesh system. Multiple components in the
    /// system, the GUI provisioning agent app and the background packet
    /// processing app, utilize this class for GATT client behavior. Business
    /// logic is left to each application, while shared GATT calls are defined
    /// here.
    /// 
    /// This includes functions to:
    /// 
    /// - Find devices
    /// - Enumerate services
    /// - Filter out devices that do not support the Internet Protocol Support 
    ///     Service (IPSS) and our IPv6ToBle Packet Service
    /// - Enumerate characteristics
    /// - Perform read or write operations on a characteristic
    /// </summary>
    public class IPv6ToBleGattClient
    {
        //---------------------------------------------------------------------
        // Constructor (empty)
        //---------------------------------------------------------------------

        public IPv6ToBleGattClient() { }

        #region Device Discovery
        //---------------------------------------------------------------------
        // Local variables for device discovery
        //---------------------------------------------------------------------

        // List to store found devices
        private List<DeviceInformation> foundDevices = new List<DeviceInformation>();

        // Getter for list of found devices for caller
        public List<DeviceInformation> FoundDevices
        {
            get
            {
                return foundDevices;
            }
            private set { }
        }

        // Bool to track if enumeration is complete for caller
        private bool isEnumerationComplete = false;

        // Getter for isEnumerationComplete
        public bool IsEnumerationComplete
        {
            get
            {
                return isEnumerationComplete;
            }
            private set { }
        }

        // Counter for number of found devices
        private int count = 0;

        // Device watcher to enumerate nearby devices
        private DeviceWatcher deviceWatcher;
        

        //---------------------------------------------------------------------
        // Methods for device discovery
        //---------------------------------------------------------------------

        // Start watcher to find all nearby Bluetooth devices (paired or
        // unpaired). Attaches event handlers to populate the device
        // collection.

        public void StartBleDeviceWatcher()
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
        public void StopBleDeviceWatcher()
        {
            if(deviceWatcher != null)
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
            DeviceWatcher       sender,
            DeviceInformation   deviceInfo
        )
        {
            Debug.WriteLine(String.Format("Added {0}{1}",
                                          deviceInfo.Id,
                                          deviceInfo.Name
                                          ));

            // Protect against the race condition if the task runs after the
            // caller stopped the deviceWatcher
            if(sender == deviceWatcher)
            {
                // Make sure the device isn't already in the list
                if(!foundDevices.Contains(deviceInfo))
                {
                    foundDevices[count] = deviceInfo;
                    count++;
                }
            }
        }

        // Device watcher updated callback
        private void DeviceWatcher_Updated(
            DeviceWatcher           sender,
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
                        }
                    }
                    newCount++;
                }

                count--;
            }
        }

        // Device watcher enumeration completed callback
        private void DeviceWatcher_EnumerationCompleted(
            DeviceWatcher   sender,
            object          args
        )
        {
            isEnumerationComplete = true;
            Debug.WriteLine("Enumeration complete.");
        }
        #endregion

        #region Enumerate GATT services and filter out incompatible devices
        //---------------------------------------------------------------------
        // Local variables for enumerating GATT services
        //---------------------------------------------------------------------

        // List of Bluetooth LE device objects to match found device information
        private List<BluetoothLEDevice> bluetoothLEDevices = new List<BluetoothLEDevice>();

        //---------------------------------------------------------------------
        // Methods for device connection after discovery (service and
        // characteristic enumeration)
        //---------------------------------------------------------------------

        // Connects to each found device and enumerates available GATT
        // services, then This method is called after the initial device discovery
        // phase and 
        public async void PopulateIPv6ToBleSupportedDevices()
        {
            // Check for empty list
            if(foundDevices.Count == 0)
            {
                return;
            }


        }
        #endregion
    }
}
