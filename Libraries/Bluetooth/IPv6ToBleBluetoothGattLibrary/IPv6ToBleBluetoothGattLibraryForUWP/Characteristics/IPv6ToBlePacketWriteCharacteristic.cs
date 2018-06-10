using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel;

// Helpers
using IPv6ToBleBluetoothGattLibraryForUWP.Helpers;

// UWP namespaces from .NET Core
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace IPv6ToBleBluetoothGattLibraryForUWP.Characteristics
{
    /// <summary>
    /// A characteristic for writing data in our service. In other words,
    /// this is the characteristic to which packets are written by the remote.
    /// </summary>
    public class IPv6ToBlePacketWriteCharacteristic : GenericGattCharacteristic
    {
        //---------------------------------------------------------------------
        // Constructor
        //---------------------------------------------------------------------

        public IPv6ToBlePacketWriteCharacteristic(
            GattLocalCharacteristic characteristic,
            GenericGattService service
        ) : base(characteristic, service)
        {
        }

        //---------------------------------------------------------------------
        // Write request callback to receive an IPv6 packet
        //---------------------------------------------------------------------

        protected override void Characteristic_WriteRequested(
            GattLocalCharacteristic sender,
            GattWriteRequestedEventArgs args
        )
        {
            // Receive the Write request into this characteristic's Value buffer
            base.Characteristic_WriteRequested(sender, args);

            // Caller does something with the packet after this
        }
    }
}
