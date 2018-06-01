using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IPv6ToBleSixLowPanLibraryForUWP
{
    /// <summary>
    /// A class to perform 6LoWPAN header compression, as well as
    /// decompression, per RFC 7668. 
    /// </summary>

    #region IPv6 Address Format Explanation Comments
    /// The code in this file is greatly inspired by and translated from the
    /// sicslowpan module in Contiki OS, though it only looks at the parts 
    /// for header compression/decompression.
    /// 
    /// An IPv6 address is 128 bits, or 16 bytes. It can either be divided into
    /// 16 8-bit sections, or UINT8's, like this:
    /// 
    /// FF FF:FF FF:FF FF:FF FF:FF FF:FF FF:FF FF
    /// 
    /// Or, it can be divided into 8 16-bit sections, or UINT16's, like this:
    /// 
    /// FFFF:FFFF:FFFF:FFFF:FFFF:FFFF:FFFF:FFFF
    /// 
    /// Keep in mind each hex character is 2 bits wide, so every pair is 4 bits.
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

    public static class HeaderCompression
    {
        #region Encoding definitions

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
            FL_C    = 0x10,     // Flow label
            TC_C    = 0x08,     // Traffic class
            NH_C    = 0x04,     // Next header flag
            TTL_1   = 0x01,     // Time to live - compressed, hop limit = 1
            TTL_64  = 0x02,     // Time to live - compressed, hop limit = 64
            TTL_255 = 0x03,     // Time to live - compressed, hop limit = 255
            TTL_I   = 0x00,     // Time to live - inline

            //
            // Values of fields within the IPHC encoding second byte
            //
            CID     = 0x80,     // Context identifier extension
            SAC     = 0x40,     // Source Address Compression
            SAM_00  = 0x00,     // Source Address Mode - 128 bits, in-line
            SAM_01  = 0x10,     // Source Address Mode - 64 bits, elide 1st 64
            SAM_10  = 0x20,     // Source Address Mode - 16 bits, elide 1st 112
            SAM_11  = 0x30,     // Source Address Mode - 0 bits, fully elided
            SAM_BIT = 4,
            M       = 0x08,     // Multicast compression
            DAC     = 0x04,     // Destination Address Compression
            DAM_00  = 0x00,     // Destination Address Mode - 128 or 48 bits
            DAM_01  = 0x01,     // Destination Address Mode - 64 bits
            DAM_10  = 0x02,     // Destination Address Mode - 16 bits
            DAM_11  = 0x03,     // Destination Address Mode - 0 bits
            DAM_BIT = 0,

            //
            // Link-local context number
            //
            ADDR_CONTEXT_LL = 0,

            //
            // 16-bit multicast addresses compression
            //
            MCAST_RANGE     = 0xA0
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
            MASK    = 0xF0,
            EXT_HDR = 0xE0,

            //
            // UDP LOWPAN_NHC (a.k.a. UDP header compression). Works with IPHC.
            //
            UDP_MASK        = 0xF8,
            UDP_ID          = 0xF0,
            UDP_CHECKSUM_C  = 0x04,
            UDP_CHECKSUM_I  = 0x00,

            //
            // UDP port compression, with checksum bit 5 set to 0
            //
            UDP_CS_P_00     = 0xF0, // All inline
            UDP_CS_P_01     = 0xF1, // Source = 16 bit inline, dest = 0xF0 + 8 bit inline
            UDP_CS_P_10     = 0xF2, // Source = 0xF0 + 8 bit inline, dest = 16 bit inline
            UDP_CS_P_11     = 0xF3  // Source and dest = 0xF0B + 4 bit inline
        }

        #endregion

        #region Address compressibility test helper functions

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
        private static bool IsIid16BitCompressable(byte[] address)
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
        private static bool IsMulticastAddressCompressable(byte[] address)
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
        private static bool IsMulticastAddressCompressable48(byte[] address)
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
        private static bool IsMulticastAddressCompressable32(byte[] address)
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
        /// Where the X's are the 32 bits for the multicast address. So, byte
        /// 1 is FF, byte 2 is 2, bytes 2-14 are zeros, and byte 15 is the end.
        /// </summary>
        /// <param name="address"></param>
        /// <returns></returns>
        private static bool IsMulticastAddressCompressable8(byte[] address)
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

        #endregion
    }
}
