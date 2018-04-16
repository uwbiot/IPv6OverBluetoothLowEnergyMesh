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
    /// This class represents generic GATT client functionality for our custom
    /// service for transferring byte arrays (packets) over Bluetooth LE.
    /// 
    /// See https://docs.microsoft.com/windows/uwp/devices-sensors/gatt-client
    /// for more details.
    /// </summary>
    public class IPv6ToBleGattClient
    {
        //---------------------------------------------------------------------
        // Local variables
        //---------------------------------------------------------------------

        // List to store found devices
        private List<DeviceInformation> FoundDevices = new List<DeviceInformation>();

        // Getter for count of found devices
        public int NumFoundDevices
        {
            get
            {
                return FoundDevices.Count;
            }
        }

        // Bool to track if enumeration is complete
        private bool isEnumerationComplete = false;

        // Getter for isEnumerationComplete
        public bool IsEnumerationComplete
        {
            get
            {
                return isEnumerationComplete;
            }
        }

        // Counter for number of found devices
        private int count = 0;

        // Device watcher to enumerate nearby devices
        private DeviceWatcher deviceWatcher;

        //---------------------------------------------------------------------
        // Constructor (empty)
        //---------------------------------------------------------------------

        public IPv6ToBleGattClient() { }

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

            // Query string for the type of device to scan for
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
            // Clear the collection of devices
            //
            FoundDevices.Clear();

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
                if(!FoundDevices.Contains(deviceInfo))
                {
                    FoundDevices[count] = deviceInfo;
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
                foreach (DeviceInformation device in FoundDevices)
                {
                    if (newCount < count)
                    {
                        if (FoundDevices[newCount].Id == deviceInfoUpdate.Id)
                        {
                            // Update the device
                            FoundDevices[newCount].Update(deviceInfoUpdate);
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
                foreach (DeviceInformation device in FoundDevices)
                {
                    if (newCount < count)
                    {
                        if (FoundDevices[newCount].Id == deviceInfoUpdate.Id)
                        {
                            // Remove the device
                            FoundDevices.RemoveAt(newCount);
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

        //---------------------------------------------------------------------
        // Methods for device connection after discovery
        //---------------------------------------------------------------------


    }
}
