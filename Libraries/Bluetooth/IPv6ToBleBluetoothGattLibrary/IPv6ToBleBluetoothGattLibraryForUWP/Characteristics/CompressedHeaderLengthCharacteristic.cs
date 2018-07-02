using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace IPv6ToBleBluetoothGattLibraryForUWP.Characteristics
{
    public class CompressedHeaderLengthCharacteristic : GenericGattCharacteristic
    {
        //---------------------------------------------------------------------
        // Constructor
        //---------------------------------------------------------------------

        public CompressedHeaderLengthCharacteristic(
            GattLocalCharacteristic characteristic,
            GenericGattService service
        ) : base(characteristic, service)
        {
        }

        //---------------------------------------------------------------------
        // Write request callback to receive the header length of an IPv6 
        // packet that had its header compressed
        //---------------------------------------------------------------------

        protected override void Characteristic_WriteRequested(
            GattLocalCharacteristic sender,
            GattWriteRequestedEventArgs args
        )
        {
            // Receive the Write request into this characteristic's Value buffer
            base.Characteristic_WriteRequested(sender, args);
        }
    }
}
