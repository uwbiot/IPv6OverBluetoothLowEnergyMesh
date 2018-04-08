///*++
//
//Module Name:
//
//    Helpers_IPAddress.c
//
//Abstract:
//
//    This file contains implementations for IPv6 validation and translation
//    functions.
//
//    This file and its header are only used on the gateway device.
//
//Environment:
//
//    Kernel-mode Driver Framework
//
//--*/
//
//#include "Includes.h"
//#include "Helpers_IPAddress.tmh"    // auto-generated tracing file
//
//BOOLEAN
//IPv6ToBleIPAddressV6StringIsValidFormat(
//    _In_    PCWSTR  ipv6AddressString
//)
///*++
//Routine Description:
//
//    Determines if a string may be an IPv6 address by verifying that the string:
//
//    - Is at least 3 characters
//    - has at least 2 colons (':')
//    - is not more than 40 characters
//
//    This function is heavily based on the "HlprIPAddressV6StringIsValidFormat"
//    helper function in the WFPSAMPLER sample driver from Microsoft.
//
//Arguments:
//
//    ipv6AddressString - the string to validate whether it is an IPv6 address
//
//Return Value:
//
//    TRUE if the string is a valid IPv6 address, FALSE if not.
//
//--*/
//{
//    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HELPERS_IP_ADDRESS, "%!FUNC! Entry");
//
//    NT_ASSERT(ipv6AddressString);
//
//    UINT32 status = NO_ERROR;
//    BOOLEAN isIPv6Address = FALSE;
//    size_t addressSize = 0;
//
//    //
//    // Step 1
//    // Validate the string length with a safe string function
//    //
//    status = StringCchLengthW(ipv6AddressString, STRSAFE_MAX_CCH, &addressSize);
//    if (FAILED(status))
//    {
//        goto Exit;
//    }
//
//    //
//    // Step 2
//    // Validate the string with the rules defined in the routine description ^
//    //
//
//    // Min address size is 3 (::1)
//    // 
//    // Max address size is 39 (FFFF:FFFF:FFFF:FFFF:FFFF:FFFF:FFFF:FFFF)
//    if (addressSize > 2 && addressSize < 40)
//    {
//        UINT32 numColons = 0;
//
//        // Count colons in the address
//        for (UINT32 index = 0;
//            index < addressSize;
//            index++)
//        {
//            if (ipv6AddressString[index] == ':')
//            {
//                numColons++;
//            }
//        }
//
//        // Verify the number of colons (min 2, max 7)
//        if (numColons > 1 && numColons < 8)
//        {
//            isIPv6Address = TRUE;
//        }
//    }
//
//Exit:
//
//    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HELPERS_IP_ADDRESS, "%!FUNC! Exit");
//
//    return isIPv6Address;
//}
//
//_Use_decl_annotations_
//UINT32
//IPv6ToBleIPAddressV6StringToValue(
//    _In_                                        PCWSTR  ipv6AddressString,
//    _Inout_updates_all_(IPV6_ADDRESS_LENGTH)    BYTE*   ipv6Address
//)
///*++
//Routine Description:
//
//    Converts a string representing an IPv6 address to its 16-byte value.
//
//    This function is heavily based on the "HlprIPAddressV6StringToValue"
//    helper function in the WFPSAMPLER sample driver from Microsoft.
//
//Arguments:
//
//    ipv6AddressString - the string to validate whether it is an IPv6 address
//
//    ipv6Address - the resultant 16 byte form of the IPv6 address
//
//Return Value:
//
//    Returns an error as defined in Winerror.h. NO_ERROR is success.
//
//--*/
//{
//    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HELPERS_IP_ADDRESS, "%!FUNC! Entry");
//
//    NT_ASSERT(ipv6AddressString);
//    NT_ASSERT(ipv6Address);
//
//    UINT32 status = NO_ERROR;
//
//    //
//    // Step 1
//    // Validate that the string is a valid IPv6 address, then convert it to its
//    // byte form
//    //
//    if (IPv6ToBleIPAddressV6StringIsValidFormat(ipv6AddressString))
//    {
//        // Required variables for conversion routine
//        UINT32 scopeId = 0;
//        UINT16 port = 0;
//        IN6_ADDR v6Addr = { 0 };
//
//        // Convert the string to the address
//        status = RtlIpv6StringToAddressExW(ipv6AddressString,
//                                          &v6Addr,
//                                          (PULONG)&scopeId,
//                                          &port
//                                          );
//        if (status != NO_ERROR)
//        {
//            goto Exit;
//        }
//
//        RtlCopyMemory(ipv6Address, &v6Addr.u.Byte[0], IPV6_ADDRESS_LENGTH);
//    }
//    else
//    {
//        status = ERROR_INVALID_PARAMETER;
//        TraceEvents(TRACE_LEVEL_ERROR, TRACE_HELPERS_IP_ADDRESS, "IPv6 address string to value conversion failed because string was invalid format %!STATUS!", status);
//    }
//
//Exit:
//
//    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_HELPERS_IP_ADDRESS, "%!FUNC! Exit");
//
//    return status;
//}