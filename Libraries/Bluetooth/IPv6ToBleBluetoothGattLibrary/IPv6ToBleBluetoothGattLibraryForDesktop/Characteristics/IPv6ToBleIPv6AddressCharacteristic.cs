using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Net;

// Namespaces in this project
using IPv6ToBleBluetoothGattLibraryForDesktop.Helpers;

// UWP namespaces
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace IPv6ToBleBluetoothGattLibraryForDesktop.Characteristics
{

    /// <summary>
    /// Characteristic for storing the device's generated link-local IPv6
    /// address. This is uncompressed.
    /// </summary>
    public class IPv6ToBleIPv6AddressCharacteristic : GenericGattCharacteristic
    {
        //---------------------------------------------------------------------
        // Local variables
        //---------------------------------------------------------------------

        // The IPv6 address
        private IPAddress ipv6Address;

        // Getter for the address
        public IPAddress Ipv6Address
        {
            get
            {
                return ipv6Address;
            }
            set
            {
                if(!IPAddress.Equals(ipv6Address, value))
                {
                    ipv6Address = value;
                    OnPropertyChanged(new PropertyChangedEventArgs("Ipv6Address"));
                }
            }
        }

        //---------------------------------------------------------------------
        // Constructor
        //---------------------------------------------------------------------

        public IPv6ToBleIPv6AddressCharacteristic(
            GattLocalCharacteristic characteristic,
            GenericGattService      service,
            IPAddress               address
        ) : base(characteristic, service)
        {
            Ipv6Address = address;
            Value = GattHelpers.ConvertByteArrayToBuffer(Ipv6Address.GetAddressBytes());
        }
    }
}
