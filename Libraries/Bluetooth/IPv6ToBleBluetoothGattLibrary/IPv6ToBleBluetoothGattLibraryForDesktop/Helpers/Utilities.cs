using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// UWP namespaces
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace IPv6ToBleBluetoothGattLibraryForDesktop.Helpers
{
    public class Utilities
    {
        /// <summary>
        /// Helper function to convert a byte array to its string form
        /// </summary>
        public static string BytesToString(byte[] array)
        {
            StringBuilder builder = new StringBuilder();

            for(int i = 0; i < array.Length; i++)
            {
                builder.Append($"{array[i]}");
                if(i < array.Length - 1)
                {
                    builder.Append(" ");
                }
            }

            return builder.ToString();
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

        /// <summary>
        /// Compares two byte arrays, i.e. packets, for equality
        /// </summary>
        /// <param name="packet1">The first packet</param>
        /// <param name="packet2">The second packet</param>
        /// <returns></returns>
        public static bool PacketsEqual(byte[] packet1, byte[] packet2)
        {
            int length = packet1.Length;

            if(length != packet2.Length)
            {
                return false;
            }

            for(int i = 0; i < length; i++)
            {
                if(packet1[i] != packet2[i])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Gets a characteristic from the characteristics result object
        /// </summary>
        /// <param name="result">A GATT characteristics result</param>
        /// <param name="characteristic">The GATT characteristic to get</param>
        public static void GetCharacteristicFromResult(
            GattLocalCharacteristicResult   result,
            ref GattLocalCharacteristic     characteristic
        )
        {
            if(result.Error == BluetoothError.Success)
            {
                characteristic = result.Characteristic;
            }
            else
            {
                Debug.WriteLine(result.Error.ToString());
            }
        }
    }
}
