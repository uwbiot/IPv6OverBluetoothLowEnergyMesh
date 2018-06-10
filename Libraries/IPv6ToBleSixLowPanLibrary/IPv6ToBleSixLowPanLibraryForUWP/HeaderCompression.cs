using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace IPv6ToBleSixLowPanLibraryForUWP
{
    /// <summary>
    /// A class to perform 6LoWPAN header compression, as well as
    /// decompression, per RFC 7668. 
    /// 
    /// The code in this file is greatly inspired by and translated from the
    /// sicslowpan module in Contiki OS, though it only looks at the parts 
    /// for header compression/decompression. Many aspects of the Contiki
    /// 6LoWPAN implementation are not needed or used because this file is
    /// intended to be used by user mode clients on Windows 10, which all use
    /// C# and communicate with Bluetooth LE via WinRT APIs. The Windows
    /// Bluetooth APIs handle aspects of 6LoWPAN such as fragmentation and
    /// reassembly, so we only have to implement the header compression part
    /// here. 
    /// 
    /// Special note: All IPv6 addresses in this library are treated as byte
    /// arrays. The .NET libraries do provide an IPAddress class, but it is
    /// assumed that the caller will get the byte form of the address before
    /// calling any function from this 6LoWPAN library. This is because we need
    /// to manipulate individual bytes of the address, so a managed class is
    /// not the best solution.
    /// 
    /// See the IPv6 Address Format Explanation Comments for more info.
    /// </summary>

    #region IPv6 Address Format Explanation Comments

    /// An IPv6 address is 128 bits, or 16 bytes. It can either be divided into
    /// 16 8-bit sections, or UINT8's, like this:
    /// 
    /// FF FF:FF FF:FF FF:FF FF:FF FF:FF FF:FF FF
    /// 
    /// Or, it can be divided into 8 16-bit sections, or UINT16's, like this:
    /// 
    /// FFFF:FFFF:FFFF:FFFF:FFFF:FFFF:FFFF:FFFF
    /// 
    /// Keep in mind each hex character is 4 bits wide, so every pair is 8 bits
    /// (2 ^ 4 = 16, going from binary to hex).
    /// 
    /// In C/C++ operating system code, including on Windows and in open source
    /// systems like Contiki OS, an IPv6 address is therefore represented as a
    /// union of these two, contained in a structure. For example, this is the
    /// structure used in Windows:
    /// 
    /// typedef struct in6_addr {
    ///   union {
    ///     UCHAR Byte[16];
    ///     USHORT Word[8];
    ///   }u;
    /// } IN6_ADDR, * PIN6_ADDR, * LPIN6_ADDR;
    /// 
    /// This is the union used in Contiki OS:
    /// 
    /// typedef union uip_ip6addr_t {
    ///     uint8_t u8[16];              /* Initializer, must come first. */
    ///     uint16_t u16[8];
    /// } uip_ip6addr_t;
    /// 
    /// Because both members of the union refer to the same memory, the two
    /// different ways of breaking the address down are for convenience in
    /// referring to certain batches of bits.
    ///
    /// For this class, we use the BYTE primitive, which is analagous to a
    /// UINT8, so all the notation we use to access individual bytes is based
    /// on the first model shown above (16 bytes).
    /// 
    /// Also, remember, the double colon signifies zeros.

    #endregion

    public class HeaderCompression
    {
        #region P/Invoke imports for C runtime

        /// <summary>
        /// Memcmp import from native C++ library using P/Invoke.
        /// </summary>
        /// <returns></returns>
        [DllImport("msvcrt.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int memcmp(byte[] b1, byte[] b2, long count);

        /// <summary>
        /// Memcpy import from native C++ library using P/Invoke. Note that the
        /// P/Invoke marshaler pins the memory for us during this call.
        /// </summary>
        /// <returns></returns>
        [DllImport("msvcrt.dll", EntryPoint = "memcpy", CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
        private static unsafe extern void* memcpy(void* dest, void* src, int size);

        [DllImport("msvcrt.dll", EntryPoint = "memset", CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
        private static unsafe extern void* memset(void* dest, int val, int size);

        #endregion

        #region IPv6 header field extraction helper function



        #endregion

        #region Encoding definitions/constants for compression and uncompression

        /// <summary>
        /// Definitions for LOWPAN_IPHC encoding for IPv6 Header Compression.
        /// 
        /// Verbatim diagram from the RFC:
        /// 
        ///    0                                       1
        ///    0   1   2   3   4   5   6   7   8   9   0   1   2   3   4   5
        ///  +---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+
        ///  | 0 | 1 | 1 |  TF   |NH | HLIM  |CID|SAC|  SAM  | M |DAC|  DAM  |
        ///  +---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+---+
        ///  
        /// Abbreviation descriptions are commented next to each member.
        /// 
        /// For more info, see section 3.1 of RFC 6282:
        /// https://tools.ietf.org/html/rfc6282#section-3.1
        /// </summary>
        private enum IPHC : uint
        {
            //
            // Values of fields within the IPHC encoding first byte. C stands
            // for compressed; I stands for inline.
            //
            FL_C = 0x10,     // Flow label
            TC_C = 0x08,     // Traffic class
            NH_C = 0x04,     // Next header flag
            TTL_1 = 0x01,     // Time to live - compressed, hop limit = 1
            TTL_64 = 0x02,     // Time to live - compressed, hop limit = 64
            TTL_255 = 0x03,     // Time to live - compressed, hop limit = 255
            TTL_I = 0x00,     // Time to live - inline

            //
            // Values of fields within the IPHC encoding second byte
            //
            CID = 0x80,     // Context identifier extension
            SAC = 0x40,     // Source Address Compression
            SAM_00 = 0x00,     // Source Address Mode - 128 bits, in-line
            SAM_01 = 0x10,     // Source Address Mode - 64 bits, elide 1st 64
            SAM_10 = 0x20,     // Source Address Mode - 16 bits, elide 1st 112
            SAM_11 = 0x30,     // Source Address Mode - 0 bits, fully elided
            SAM_BIT = 4,
            M = 0x08,     // Multicast compression
            DAC = 0x04,     // Destination Address Compression
            DAM_00 = 0x00,     // Destination Address Mode - 128 or 48 bits
            DAM_01 = 0x01,     // Destination Address Mode - 64 bits
            DAM_10 = 0x02,     // Destination Address Mode - 16 bits
            DAM_11 = 0x03,     // Destination Address Mode - 0 bits
            DAM_BIT = 0,

            //
            // Link-local context number
            //
            ADDR_CONTEXT_LL = 0,

            //
            // 16-bit multicast addresses compression
            //
            MCAST_RANGE = 0xA0
        }

        /// <summary>
        /// Definitions for LOWPAN_NHC encoding, for IPv6 Next Header
        /// Compression. Also includes UDP header compression.
        /// 
        /// Verbatim diagram from the RFC:
        /// 
        ///   0   1   2   3   4   5   6   7
        /// +---+---+---+---+---+---+---+---+
        /// | 1 | 1 | 1 | 1 | 0 | C |   P   |
        /// +---+---+---+---+---+---+---+---+
        /// 
        /// C = checksum, P = ports.
        /// 
        /// For more info about NHC, see section 4 of RFC 6282:
        /// https://tools.ietf.org/html/rfc6282#section-4
        /// 
        /// For UDP LOWPAN_NHC, see section 4.3.3:
        /// https://tools.ietf.org/html/rfc6282#section-4.3.3
        /// </summary>
        private enum NHC : uint
        {
            //
            // NHC_EXT_HDR (extension header)
            //
            MASK = 0xF0,
            EXT_HDR = 0xE0,

            //
            // UDP LOWPAN_NHC (a.k.a. UDP header compression). Works with IPHC.
            //
            UDP_MASK = 0xF8,
            UDP_ID = 0xF0,
            UDP_CHECKSUM_C = 0x04,
            UDP_CHECKSUM_I = 0x00,

            //
            // UDP port compression, with checksum bit 5 set to 0
            //
            UDP_CS_P_00 = 0xF0, // All inline
            UDP_CS_P_01 = 0xF1, // Source = 16 bit inline, dest = 0xF0 + 8 bit inline
            UDP_CS_P_10 = 0xF2, // Source = 0xF0 + 8 bit inline, dest = 16 bit inline
            UDP_CS_P_11 = 0xF3  // Source and dest = 0xF0B + 4 bit inline
        }

        private int IPV6_HEADER_LENGTH = 1;   // One byte

        // How many address contexts are supported when using IPHC compression
        private static int MAX_ADDRESS_CONTEXTS = 1;

        /// <summary>
        /// An address context for IPHC address compression. Each context can
        /// have up to 8 bytes. Defined as a class so it will be passed by
        /// reference, like a struct* in native code.
        /// </summary>
        class AddressContext
        {
            public byte used;
            public byte number;
            public byte[] prefix;

            public AddressContext()
            {
                used = 0;
                number = 0;
                prefix = new byte[8];
            }
        }

        // Address contexts for IPHC
        private AddressContext[] addressContexts = new AddressContext[MAX_ADDRESS_CONTEXTS];

        // An address context
        private AddressContext context = new AddressContext();

        /// <summary>
        /// Uncompression of link local address.
        /// 
        /// 0 -> full 16 bytes from packet
        /// 1 -> 2 bytes from prefix; zeroes and 8 from packet
        /// 2 -> 2 bytes from prefix; 0000::00ff:fe00:XXXX from packet
        /// 3- -> 2 bytes from prefix; infer 8 bytes from link local address
        /// 
        /// Note: The uncompress function does change 0xf to 0x10.
        /// Note 2: 0x00 => no auto configuration => unspecified.
        /// </summary>
        private byte[] uncompressLinkLocal = { 0x0f, 0x28, 0x22, 0x20 };

        /// <summary>
        /// Uncompression of context-based compression.
        /// 
        /// 0 -> 0 bits from packet [unspecified / reserved]
        /// 1 -> 8 bytes from prefix; zeroes and 8 from packet
        /// 2 -> 8 bytes from prefix; 0000::00ff:fe00:XXXX + 2 from packet
        /// 3 -> 8 bytes from prefix; infer 8 bytes from link local address
        /// </summary>
        private byte[] uncompressContextBased = { 0x00, 0x88, 0x82, 0x80 };

        /// <summary>
        /// Uncompression of non-context based multicast compression.
        /// 
        /// 0 -> 0 bits from packet
        /// 1 -> 2 bytes from prefix; zeroes, 5 from packet
        /// 2 -> 2 bytes from prefix; zeroes + 3 from packet
        /// 3 -> 2 bytes from prefix; infer 1 byte from link local address
        /// </summary>
        private byte[] uncompressNonContextBasedMulticast = { 0x0f, 0x25, 0x23, 0x21 };

        // The link-local prefix (FE80)
        private byte[] linkLocalPrefix = { 0xfe, 0x80 };

        // Time to live uncompression values
        private byte[] timeToLiveValues = { 0, 1, 64, 255 };

        #endregion

        #region Address testing helper functions

        /// <summary>
        /// Checks whether we can compress the IID in the address to 16 bits.
        /// This is used for unicast addresses only and is true if the address
        /// is in this form:
        /// 
        /// [PREFIX] :: 0000:00ff:fe00:XXXX
        /// 
        /// This assumes a 64-bit prefix.
        /// </summary>
        /// <param name="address">The IPv6 address in byte form.</param>
        /// <returns>TRUE if the address is in the format described above.</returns>
        private bool IsIid16BitCompressable(byte[] address)
        {
            return (address[8] == 0 &&
                    address[9] == 0 &&
                    address[10] == 0 &&
                    address[11] == 0xff &&
                    address[12] == 0xfe &&
                    address[13] == 0
                    );
        }

        /// <summary>
        /// Checks whether the 112-bit group ID of the multicast address is
        /// mappable to a 9-bit group ID.
        /// </summary>
        /// <param name="address">The IPv6 address in byte form.</param>
        /// <returns>TRUE if the group is the all nodes or all routers group.</returns>
        private bool IsMulticastAddressCompressable(byte[] address)
        {
            return (address[2] == 0 &&
                    address[3] == 0 &&
                    address[4] == 0 &&
                    address[5] == 0 &&
                    address[6] == 0 &&
                    address[7] == 0 &&
                    address[8] == 0 &&
                    address[9] == 0 &&
                    address[10] == 0 &&
                    address[11] == 0 &&
                    address[12] == 0 &&
                    address[13] == 0 &&
                    address[14] == 0 &&
                    (address[15] == 1 || address[15] == 2)
                    );
        }

        /// <summary>
        /// Same as IsMulticastAddressCompressable, but for this format of
        /// address:
        /// 
        /// FFXX::00XX:XXXX:XXXX
        /// 
        /// Where the X's are the 48 bits for the multicast address. So, byte
        /// 1 is FF, byte 2 is part of the multicast address, bytes 2-10 are
        /// zeros, and bytes 11-15 are the rest of the address.
        /// </summary>
        /// <param name="address"></param>
        /// <returns></returns>
        private bool IsMulticastAddressCompressable48(byte[] address)
        {
            return (address[2] == 0 &&
                    address[3] == 0 &&
                    address[4] == 0 &&
                    address[5] == 0 &&
                    address[6] == 0 &&
                    address[7] == 0 &&
                    address[8] == 0 &&
                    address[9] == 0 &&
                    address[10] == 0
                    );
        }

        /// <summary>
        /// Same as IsMulticastAddressCompressable, but for this format of
        /// address:
        /// 
        /// FFXX::00XX:XXXX
        /// 
        /// Where the X's are the 32 bits for the multicast address. So, byte
        /// 1 is FF, byte 2 is part of the multicast address, bytes 2-12 are
        /// zeros, and bytes 13-15 are the rest of the address.
        /// </summary>
        /// <param name="address"></param>
        /// <returns></returns>
        private bool IsMulticastAddressCompressable32(byte[] address)
        {
            return (address[2] == 0 &&
                    address[3] == 0 &&
                    address[4] == 0 &&
                    address[5] == 0 &&
                    address[6] == 0 &&
                    address[7] == 0 &&
                    address[8] == 0 &&
                    address[9] == 0 &&
                    address[10] == 0 &&
                    address[11] == 0 &&
                    address[12] == 0
                    );
        }

        /// <summary>
        /// Same as IsMulticastAddressCompressable, but for this format of
        /// address:
        /// 
        /// FF02::00XX
        /// 
        /// Where the X's are the 8 bits for the multicast address. So, byte
        /// 1 is FF, byte 2 is 2, bytes 2-14 are zeros, and byte 15 is the end.
        /// </summary>
        /// <param name="address"></param>
        /// <returns></returns>
        private bool IsMulticastAddressCompressable8(byte[] address)
        {
            return (address[1] == 2 &&
                    address[2] == 0 &&
                    address[3] == 0 &&
                    address[4] == 0 &&
                    address[5] == 0 &&
                    address[6] == 0 &&
                    address[7] == 0 &&
                    address[8] == 0 &&
                    address[9] == 0 &&
                    address[10] == 0 &&
                    address[11] == 0 &&
                    address[12] == 0 &&
                    address[13] == 0 &&
                    address[14] == 0
                    );
        }

        /// <summary>
        /// Checks if an IP address was generated from a MAC address/Bluetooth
        /// radio ID.
        /// </summary>
        /// <param name="ipAddress">The full 128-bit IPv6 address in question.</param>
        /// <param name="linkLocalAddress">A 64-bit IID generated from the local Bluetooth radio ID/MAC address.</param>
        /// <returns></returns>
        private bool IsAddressBasedOnMacAddress(
            byte[] ipAddress,
            byte[] linkLocalAddress
        )
        {
            return (ipAddress[8] == (linkLocalAddress[0] ^ 0x02) &&
                   ipAddress[9] == linkLocalAddress[1] &&
                   ipAddress[10] == linkLocalAddress[2] &&
                   ipAddress[11] == linkLocalAddress[3] &&
                   ipAddress[12] == linkLocalAddress[4] &&
                   ipAddress[13] == linkLocalAddress[5] &&
                   ipAddress[14] == linkLocalAddress[6] &&
                   ipAddress[15] == linkLocalAddress[7]
                   );
        }

        #endregion

        #region Address compression/decompression functions

        /// <summary>
        /// Helper function to compare two IP address prefixes.
        /// </summary>
        private bool IpAddressPrefixCompare(
            byte[] address1,
            byte[] address2,
            int length
        )
        {
            return (memcmp(address1, address2, length >> 3) == 0);
        }

        /// <summary>
        /// Finds the context with the given number.
        /// </summary>
        /// <param name="ipAddress">The IP address.</param>
        /// <returns></returns>
        private AddressContext LookupAddressContextByPrefix(byte[] ipAddress)
        {
            for (int i = 0; i < MAX_ADDRESS_CONTEXTS; i++)
            {
                if ((addressContexts[i].used == 1) &&
                    IpAddressPrefixCompare(addressContexts[i].prefix,
                                           ipAddress,
                                           64))
                {
                    return addressContexts[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Compresses a 64-bit IID if possible.
        /// </summary>
        private unsafe byte CompressAddress64(
            byte bitPosition,
            byte[] ipAddress,
            byte[] linkLayerAddress,
            byte* headerCompressionPtr
        )
        {
            // Check if the 64-bit IID is based on the supplied MAC address-
            // generated link local address
            if(IsAddressBasedOnMacAddress(ipAddress, linkLayerAddress))
            {
                return (byte)(3 << bitPosition);    // 0 bits
            }
            else if(IsIid16BitCompressable(ipAddress))
            {
                // Compress the IID to 16 bits: xxxx::0000:00ff:fe00:XXXX
                fixed(byte* source = &ipAddress[14])
                {
                    memcpy(headerCompressionPtr, source, 2);
                }
                headerCompressionPtr += 2;
                return (byte)(2 << bitPosition);    // 16 bits
            }
            else
            {
                // Do not compress the IID. xxxx::IID
                fixed(byte* source = &ipAddress[8])
                {
                    memcpy(headerCompressionPtr, source, 8);
                }
                headerCompressionPtr += 8;
                return (byte)(1 << bitPosition);    // 64 bits
            }
        }

        /// <summary>
        /// Uncompresses an address based on a prefix and a postfix with zeroes
        /// in between. If the postfix is zero in length, it will use the link
        /// address to configure the IP address.
        /// </summary>
        private unsafe void UncompressAddress(
            byte[] ipAddress,
            byte[] prefix,
            byte prefixPostfixCount,
            byte[] linkLayerAddress,
            byte* headerCompressionPtr
        )
        {
            byte prefixCount = (byte)(prefixPostfixCount >> 4);
            byte postfixCount = (byte)(prefixPostfixCount & 0x0f);

            // Full nibble 15 -> 16
            prefixCount = (byte)(prefixCount == 15 ? 16 : prefixCount);
            postfixCount = (byte)(postfixCount == 15 ? 16 : postfixCount);

            if(prefixCount > 0)
            {
                fixed(byte* ipAddrPtr = &ipAddress[0], prefixPtr = &prefix[0])
                {
                    memcpy(ipAddrPtr, prefixPtr, prefixCount);
                }            
            }

            if(prefixCount + postfixCount < 16)
            {
                fixed(byte* ipAddrPtr = &ipAddress[prefixCount])
                {
                    memset(ipAddrPtr, 0, 16 - (prefixCount + postfixCount));
                }
            }

            if(postfixCount > 0)
            {
                fixed(byte* ipAddrPtr = &ipAddress[16 - postfixCount])
                {
                    memcpy(ipAddrPtr, headerCompressionPtr, postfixCount);
                }

                if(postfixCount == 2 && prefixCount < 11)
                {
                    // 16-bit uncompression -> 0000:00ff:fe00:XXXX
                    ipAddress[11] = 0xff;
                    ipAddress[12] = 0xfe;
                }

                headerCompressionPtr += postfixCount;
            } 
            else if(prefixCount > 0)
            {
                // No IID-based configuration if no prefix and no data.
                // Unspecified. Set the last 64 bits of the address to the
                // EUI-64.
                fixed(byte* ipAddrPtr = &ipAddress[8], llAddrPtr = &linkLayerAddress[0])
                {
                    memcpy(ipAddrPtr, llAddrPtr, 8);
                }
                ipAddress[8] ^= 0x02;
            }
        }
         
        #endregion

        #region Header compression

        /// <summary>
        /// Creates and returns a compressed 6LoWPAN packet from a source full 
        /// IPv6 packet. 
        /// 
        /// For LOWPAN_UDP compression, we either compress both ports or none.
        /// The general format with LOWPAN_UDP compression is (verbatim from
        /// RFC 6282):
        /// 
        ///                      1                   2                   3
        ///  0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
        /// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
        /// |0|1|1|TF |N|HLI|C|S|SAM|M|D|DAM| SCI   | DCI   | comp.IPv6 hdr |
        /// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
        /// | compressed IPv6 fields.....                                   |
        /// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
        /// | LOWPAN_UDP    | non compressed UDP fields...                  |
        /// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
        /// | L4 data ...                                                   |
        /// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
        /// 
        /// Quoting from the Contiki OS comments:
        /// 
        /// "The context number 00 is reserved for the link local prefix. For
        /// Unicast addresses, if we cannot compress the prefix, we neither
        /// compress the IID."
        /// </summary>
        /// <param name="linkLayerDestinationAddress">The L2 (link layer) destination address,
        /// needed to compress the IP destination address.</param>
        public unsafe byte[] CompressHeaderIPHC(
            byte[] sourcePacket,
            byte[] linkLayerDestinationAddress
        )
        {    
            // The compressed packet to return. Start out at the same length
            // as the original because we need to manipulate pointers in the
            // new one as we go
            byte[] compressedPacket = new byte[sourcePacket.Length];
            
            fixed(byte* compressedPacketPtr = &compressedPacket[0])
            {
                // Local variables for temp purposes, as well as the two compressed
                // header bytes
                byte temp, iphc0, iphc1;

                // Set the header compression pointer to the beginning of the new
                // compressed packet, + 2 for the 2 compressed bytes
                byte* headerCompressionPtr = compressedPacketPtr + 2;

                iphc0 = 0x60; // 011xxxxx = ... 
                iphc1 = 0;

                // Check if destination context exists, for possibly allocating
                // a third byte
                if(LookupAddressContextByPrefix())

                // Traffic class and flow label. If the flow label is 0, 
                // compress it. If traffic class is 0, compress it. The offset
                // of the traffic class depends on the presence of the version
                // and flow label.
                temp = 

            }

            return compressedPacket;
        }

        #endregion
    }
}
