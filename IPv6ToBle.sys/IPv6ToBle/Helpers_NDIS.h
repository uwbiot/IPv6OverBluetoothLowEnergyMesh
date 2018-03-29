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

// This structure holds handles to the memory pools used to create
// NET_BUFFER_LIST and NET_BUFFER structures. NDIS uses special pools for
// performance reasons and so kernel executive memory is not fragmented.
typedef struct _NDIS_POOL_DATA {
	HANDLE	ndisHandle;		// NDIS_HANDLE
	HANDLE	nblPoolHandle;	// NDIS_HANDLE
	HANDLE	nbPoolHandle;	// NDIS_HANDLE
}NDIS_POOL_DATA, *PNDIS_POOL_DATA;

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
