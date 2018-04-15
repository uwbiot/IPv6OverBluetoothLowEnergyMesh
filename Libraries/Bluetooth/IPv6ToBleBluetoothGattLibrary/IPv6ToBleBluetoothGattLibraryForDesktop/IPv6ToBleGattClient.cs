using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
        public static QueryNearbyDevices(string[] requestedProperties)
        {
            DeviceWatcher deviceWatcher =
            DeviceInformation.CreateWatcher(
                    BluetoothLEDevice.GetDeviceSelectorFromPairingState(false),
                    requestedProperties,
                    DeviceInformationKind.AssociationEndpoint);

            // Register event handlers before starting the watcher.
            // Added, Updated and Removed are required to get all nearby 
            // devices.
            deviceWatcher.Added += DeviceWatcher_Added;
            deviceWatcher.Updated += DeviceWatcher_Updated;
            deviceWatcher.Removed += DeviceWatcher_Removed;

            // EnumerationCompleted and Stopped are optional to implement.
            deviceWatcher.EnumerationCompleted += DeviceWatcher_EnumerationCompleted;
            deviceWatcher.Stopped += DeviceWatcher_Stopped;

            // Start the watcher.
            deviceWatcher.Start();
        }
    }
}
