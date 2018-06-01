using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Diagnostics;

// UWP namespaces
using Windows.Devices.Bluetooth;

namespace IPv6ToBleSixLowPanLibraryForDesktop
{
    /// <summary>
    /// A class to construct a 128-bit link local IPv6 address from the local
    /// Bluetooth radio.
    /// </summary>
    public static class StatelessAddressConfiguration
    {

        public static async Task<IPAddress> GenerateLinkLocalAddressFromBlThRadioIdAsync(
            int scopeId
        )
        {
            //
            // Step 1
            // Get the 64-bit IID from the local Bluetooth radio address
            //
            byte[] sixtyFourBitAddress = await GenerateIidFromBlthRadioIdAsync();

            if (sixtyFourBitAddress == null)
            {
                return null;
            }

            //
            // Step 2
            // Build the IPv6 address string from the bytes
            //
            StringBuilder builder = new StringBuilder();
            builder.Append("fe80::");
            for (int i = 0; i < 8; i += 2)
            {
                builder.Append($"{sixtyFourBitAddress[i]:X2}");
                builder.Append($"{sixtyFourBitAddress[i + 1]:X2}");
                if (i < 6)
                {
                    builder.Append(":");
                }
            }
            builder.Append("%" + scopeId.ToString());

            //
            // Step 3
            // Generate the IPAddress from the string form
            //
            string generatedAddressString = builder.ToString();

            IPAddress generatedAddress;

            if (IPAddress.TryParse(generatedAddressString, out generatedAddress))
            {
                return generatedAddress;
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Generates a 64-bit EUI Interface Identifier (IID) suffix from the 
        /// 48-bit local Bluetooth radio ID. This will be used later to be
        /// appended to the border router's 64-bit prefix.
        /// 
        /// The basic principle is as follows:
        /// 
        /// 1. Insert 0xFFFE into the middle of the 48-digit address.
        /// 2. Flip the seventh bit from 0 to 1.
        /// 3. Prepend fe80::, the link-local identifier, to make it 128-bit.
        /// 
        /// Details are in RFC 2464, RFC 4944, and here:
        /// http://www.tcpipguide.com/free/t_IPv6InterfaceIdentifiersandPhysicalAddressMapping-2.htm
        /// 
        /// The downside of generating a 64-bit IID this way is that if the
        /// physical hardware changes, so does the IID. This may not
        /// be an issue if a device has an embedded Bluetooth radio, but for
        /// devices that may have USB-based Bluetooth dongles, swapping it out
        /// would mean the address is different and mess with any kind of
        /// static programming that relies on the address.
        /// 
        /// Note: This algorithm is probably way slower than it should be 
        /// because I'm terrible at bitwise operations so I don't use them.
        /// </summary>
        public static async Task<byte[]> GenerateIidFromBlthRadioIdAsync()
        {
            //
            // Step 1
            // Retrieve the local radio object and get its address
            //
            // **NOTE**
            // For some reason, the retrieved Bluetooth address using this
            // method is 40 bytes long, not 48 like it should be? I discovered
            // this through testing. 
            // 
            // So, for this algorithm to work, we convert it to byte form. The
            // method to do this conversion pads with zeroes out to an 8-byte,
            // or 64-bit, length. So, we then form a new "48-bit" address by 
            // taking the 40-bit retrieved address and keep the next byte of 
            // zeroes.
            //

            // Get the 40-bit address
            BluetoothAdapter localRadio = await BluetoothAdapter.GetDefaultAsync();
            ulong localRadioAddress = 0;

            if (localRadio != null)
            {
                localRadioAddress = localRadio.BluetoothAddress;
            }
            else
            {
                Debug.WriteLine("No Bluetooth device installed; could not" +
                                "retrieve local radio address."
                                );
                return null;
            }

            // Convert the address into byte form (returns an 8-byte array with
            // padded zeroes at the end)
            byte[] radioAddressBytes = BitConverter.GetBytes(localRadioAddress);

            //
            // Step 2
            // Insert FFFE into the middle of the address
            //
            byte[] sixtyFourBitIid = new byte[8];
            for (int i = 0; i < 3; i++)
            {
                sixtyFourBitIid[i] = radioAddressBytes[i];
            }
            sixtyFourBitIid[3] = 0xFF;
            sixtyFourBitIid[4] = 0xFE;
            for (int i = 5; i < 8; i++)
            {
                sixtyFourBitIid[i] = radioAddressBytes[i - 2];
            }
            //
            // Step 3
            // Flip the seventh bit from 0 to 1 by XOR-ing the 2 column
            //
            sixtyFourBitIid[0] ^= 0x02;

            return sixtyFourBitIid;
        }
    }
}
