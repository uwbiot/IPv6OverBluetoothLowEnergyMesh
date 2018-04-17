using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

// UWP namespaces
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace IPv6ToBleBluetoothGattLibraryForDesktop.Helpers
{
    /// <summary>
    /// Helper class for working with GATT aspects like characteristics and
    /// UUIDs.oft.
    /// </summary>
    public static class GattHelpers
    {
        //---------------------------------------------------------------------
        // Generic parameter wrappers for characteristics
        //---------------------------------------------------------------------

        // GATT local characteristics parameter for Write parameters
        public static readonly GattLocalCharacteristicParameters packetWriteParameters = new GattLocalCharacteristicParameters
        {
            CharacteristicProperties = GattCharacteristicProperties.Write | GattCharacteristicProperties.WriteWithoutResponse,
            WriteProtectionLevel = GattProtectionLevel.Plain,
            UserDescription = "IPv6 packet writing characteristic"
        };

        //---------------------------------------------------------------------
        // Miscellaneous GATT helper functions. 
        //
        // These are based off the GattServicesHelper class in the 
        // BluetoothLEExplorer sample from Microsoft.
        //---------------------------------------------------------------------

        /// <summary>
        /// Gets a characteristic from the characteristics result object
        /// </summary>
        /// <param name="result">A GATT characteristics result</param>
        /// <param name="characteristic">The GATT characteristic to get</param>
        public static void GetCharacteristicFromResult(
            GattLocalCharacteristicResult result,
            ref GattLocalCharacteristic characteristic
        )
        {
            if (result.Error == BluetoothError.Success)
            {
                characteristic = result.Characteristic;
            }
            else
            {
                Debug.WriteLine(result.Error.ToString());
            }
        }

        /// <summary>
        /// Converts an IPv6 packet (byte array) to a buffer for Bluetooth
        /// operations.
        /// </summary>
        /// <param name="packet">The IPv6 packet in byte array form.</param>
        /// <returns></returns>
        public static IBuffer ConvertPacketToBuffer(byte[] packet)
        {
            DataWriter writer = new DataWriter();
            writer.WriteBytes(packet);
            return writer.DetachBuffer();
        }

        /// <summary>
        /// Converts a Bluetooth buffer into a byte array (IPv6 packet).
        /// </summary>
        /// <param name="buffer">The buffer received via Bluetooth.</param>
        /// <returns></returns>
        public static byte[] ConvertBufferToPacket(IBuffer buffer)
        {
            uint bufferLength = buffer.Length;
            byte[] packet = new byte[bufferLength];

            DataReader reader = DataReader.FromBuffer(buffer);
            reader.ReadBytes(packet);

            return packet;
        }

        //---------------------------------------------------------------------
        // Helpers for working with UUIDs.
        //
        // These methods are based on the methods in the GattUuidsService class
        // in the BluetoothLEExplorer sample from Microsoft.
        //---------------------------------------------------------------------

        // Bluetooth SIG-assigned Attribute ID value enumeration. We only
        // define the IPSP one because that's the only one we care about.
        public enum SigAssignedGattNativeUuid : ushort
        {
            InternetProtocolSupport = 0x1820
        }

        /// <summary>
        /// Converts a UUID to a string name
        /// </summary>
        /// <param name="uuid"></param>
        /// <returns></returns>
        public static string ConvertUuidToName(Guid uuid)
        {
            if (Enum.TryParse(ConvertUuidToShortId(uuid).ToString(), out SigAssignedGattNativeUuid name) == true)
            {
                return name.ToString();
            }
            else
            {
                return uuid.ToString();
            }
        }

        /// <summary>
        /// Converts a UUID into its short form. Used when querying services
        /// based on their UUID to search for Bluetooth SIG-defined services.
        /// </summary>
        /// <param name="uuid"></param>
        /// <returns></returns>
        public static ushort ConvertUuidToShortId(Guid uuid)
        {
            byte[] byteForm = uuid.ToByteArray();
            ushort shortUuid = (ushort)(byteForm[0] | (byteForm[1] << 8));
            return shortUuid;
        }
    }
}
