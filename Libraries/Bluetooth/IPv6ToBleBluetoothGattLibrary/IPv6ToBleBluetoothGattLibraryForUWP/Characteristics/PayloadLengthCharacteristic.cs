using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace IPv6ToBleBluetoothGattLibraryForUWP.Characteristics
{
    public class PayloadLengthCharacteristic : GenericGattCharacteristic
    {
        //---------------------------------------------------------------------
        // Constructor
        //---------------------------------------------------------------------

        public PayloadLengthCharacteristic(
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
