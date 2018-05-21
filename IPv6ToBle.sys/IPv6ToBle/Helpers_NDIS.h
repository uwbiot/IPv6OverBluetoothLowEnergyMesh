/*++

Module Name:

	Helpers_NDIS.h

Abstract:

	This file contains definitions for helper data structures and functions
	operations that interact with NDIS 6.


Environment:

	Kernel-mode Driver Framework

--*/

#ifndef _HELPERS_NDIS_H_
#define _HELPERS_NDIS_H_

EXTERN_C_START

//-----------------------------------------------------------------------------
// Function to register as an NDIS interface provider, for inbound packet
// injection
//-----------------------------------------------------------------------------

//_IRQL_requires_(PASSIVE_LEVEL)
//_IRQL_requires_same_
//NTSTATUS
//IPv6ToBleNDISRegisterAsIfProvider();

//-----------------------------------------------------------------------------
// Functions to manipulate NDIS memory pools
//-----------------------------------------------------------------------------

_IRQL_requires_min_(PASSIVE_LEVEL)
_IRQL_requires_max_(DISPATCH_LEVEL)
_IRQL_requires_same_
_Check_return_
_Success_(return == STATUS_SUCCESS)
NTSTATUS
IPv6ToBleNDISPoolDataCreate(
	_Out_    NDIS_POOL_DATA*	ndisPoolData,
	_In_opt_ UINT32				memoryTag
);

_IRQL_requires_min_(PASSIVE_LEVEL)
_IRQL_requires_max_(DISPATCH_LEVEL)
_IRQL_requires_same_
_Check_return_
_Success_(return == STATUS_SUCCESS)
NTSTATUS
IPv6ToBleNDISPoolDataPopulate(
	_Inout_	 NDIS_POOL_DATA*	ndisPoolData,
	_In_opt_ UINT32				memoryTag
);

_IRQL_requires_min_(PASSIVE_LEVEL)
_IRQL_requires_max_(DISPATCH_LEVEL)
_IRQL_requires_same_
_Success_(ndisPoolData == 0)
inline VOID
IPv6ToBleNDISPoolDataDestroy(
	_Inout_ NDIS_POOL_DATA*	ndisPoolData
);

_IRQL_requires_min_(PASSIVE_LEVEL)
_IRQL_requires_max_(DISPATCH_LEVEL)
_IRQL_requires_same_
inline VOID
IPv6ToBleNDISPoolDataPurge(
	_Inout_	NDIS_POOL_DATA*	ndisPoolData
);

EXTERN_C_END

#endif	// _HELPERS_NDIS_H
