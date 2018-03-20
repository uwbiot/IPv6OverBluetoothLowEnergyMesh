/*++

Module Name:

	Helpers_NetBuffer.h

Abstract:

	This file contains definitions for helper functions for working with
	NET_BUFFER_LIST and NET_BUFFER structures.
	operations that interact with NDIS 6.

	The primary kernel mode data structure for networking I / O is the
	NET_BUFFER_LIST, or NBL.An NBL contains one or more NET_BUFFER structures
	that each represent an actual packet.A NET_BUFFER contains a memory
	descriptor list, or MDL, that describes the actual virtually discontiguous
	memory for the buffer data.

	See this link for more information about kernel mode data structures :
	https://docs.microsoft.com/en-us/windows-hardware/drivers/network/network-data-structures


Environment:

	Kernel-mode Driver Framework

--*/

#ifndef _HELPERS_NETBUFFER_H_
#define _HELPERS_NETBUFFER_H_

//-----------------------------------------------------------------------------
// Helper functions to convert an NBL -> Byte array and vice versa
//-----------------------------------------------------------------------------

EXTERN_C_START

_IRQL_requires_min_(PASSIVE_LEVEL)
_IRQL_requires_max_(DISPATCH_LEVEL)
_IRQL_requires_same_
_Success_(return != 0)
NET_BUFFER_LIST*
IPv6ToBleNBLCreateFromBuffer(
	_In_							NDIS_HANDLE	nblPoolHandle,
	_In_reads_(inputBuffersize)		BYTE*		packetFromUsermode,
	_In_							size_t*		packetFromUsermodeSize
);

_IRQL_requires_min_(PASSIVE_LEVEL)
_IRQL_requires_max_(DISPATCH_LEVEL)
_IRQL_requires_same_
_Success_(return != 0)
BYTE*
IPv6ToBleNBLCopyToBuffer(
	_In_	NET_BUFFER_LIST*	NBL,
	_Out_	UINT32*				size,
	_In_	UINT32				additionalSpace
);

EXTERN_C_END

#endif	// _HELPERS_NETBUFFER_H_
