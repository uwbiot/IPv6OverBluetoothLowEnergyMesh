using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace IPv6ToBleBluetoothGattLibraryForDesktop
{
    /// <summary>
    /// Constant definitions for the IPv6ToBle packet writing service
    /// </summary>
    public static class Constants
    {    
        //---------------------------------------------------------------------
        // UUIDs for custom services and characteristics
        //---------------------------------------------------------------------

        // UUID for the IPv6ToBlePacketProcessingService
        public static readonly Guid IPv6ToBlePacketProcessingServiceUuid = Guid.Parse("93898DDB-AEAB-4434-997E-B0D2617E3033");

        // UUID for the IPv6ToBlePacketProcessingService's packet write characteristic
        public static readonly Guid IPv6ToBlePacketWriteCharacteristicUuid = Guid.Parse("582DC845-8AE1-428D-8743-AAA38556AE3B");

        // UUID for the IPv6ToBlePacketProcessingService's IPv6 address read characteristic
        public static readonly Guid IPv6ToBleIPv6AddressCharacteristicUuid = Guid.Parse("BADF5F54-80E3-4FEE-8DFD-1D827A88A73E");

        //---------------------------------------------------------------------
        // Error codes
        //---------------------------------------------------------------------

        public static readonly int E_BLUETOOTH_ATT_WRITE_NOT_PERMITTED = unchecked((int)0x80650003);
        public static readonly int E_BLUETOOTH_ATT_INVALID_PDU = unchecked((int)0x80650004);
        public static readonly int E_ACCESSDENIED = unchecked((int)0x80070005);
        public static readonly int E_DEVICE_NOT_AVAILABLE = unchecked((int)0x800710df); // HRESULT_FROM_WIN32(ERROR_DEVICE_NOT_AVAILABLE)
    }
}
