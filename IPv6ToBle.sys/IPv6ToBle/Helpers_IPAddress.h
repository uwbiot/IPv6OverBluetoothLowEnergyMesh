/*++

Module Name:

    Helpers_IPAddress.h

Abstract:

    This file contains definitions for functions to validate and translate
    IPv6 addresses.

    This header is only used on the gateway device.

Environment:

    Kernel-mode Driver Framework

--*/

#ifndef _HELPERS_IPADDRESS_H_
#define _HELPERS_IPADDRESS_H

//-----------------------------------------------------------------------------
// Function to validate if a string is a valid IPv6 address
//-----------------------------------------------------------------------------

BOOLEAN
IPv6ToBleIPAddressV6StringIsValidFormat(
    _In_    PCWSTR  ipv6AddressString
);

//-----------------------------------------------------------------------------
// Function to translate an IPv6 string to its 16-byte value
//-----------------------------------------------------------------------------

_Success_(return == NO_ERROR)
UINT32
IPv6ToBleIPAddressV6StringToValue(
    _In_                                        PCWSTR  ipv6AddressString,
    _Inout_updates_all_(IPV6_ADDRESS_LENGTH)    BYTE*   ipv6Address
);

#endif  // _HELPERS_IPADDRESS_H_