using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel;

// Helpers
using IPv6ToBleBluetoothGattLibraryForDesktop.Helpers;

// UWP namespaces from .NET Core
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace IPv6ToBleBluetoothGattLibraryForDesktop.Characteristics
{
    /// <summary>
    /// A characteristic for writing data in our service. In other words,
    /// this is the characteristic to which packets are written by the remote.
    /// </summary>
    public class IPv6ToBlePacketWriteCharacteristic : GenericGattCharacteristic
    {
        //---------------------------------------------------------------------
        // Local variables
        //---------------------------------------------------------------------

        // An IPv6 packet. Max size is 1280 bytes, the MTU for Bluetooth in
        // general.
        private byte[] packet = new byte[1280];

        // Getter and setter for the packet
        public byte[] Packet
        {
            get
            {
                return packet;
            }

            private set
            {
                if(!Utilities.PacketsEqual(value, packet))
                {
                    packet = value;
                    OnPropertyChanged(new PropertyChangedEventArgs("Packet"));
                }
            }
        }

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

            // Set the packet byte array to the received value for others to
            // read or retrieve
            Packet = Utilities.ConvertBufferToPacket(Value);
        }
    }
}
