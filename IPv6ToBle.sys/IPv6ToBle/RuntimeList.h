/*++

Module Name:

    RuntimeList.h

Abstract:

    This file contains definitions for functions to manipulate runtime lists,
    including assigning and removing entries and destroying the lists.

Environment:

    Kernel-mode Driver Framework

--*/

#ifndef _RUNTIMELIST_H_
#define _RUNTIMELIST_H_

//-----------------------------------------------------------------------------
// Functions to add entries to the lists
//-----------------------------------------------------------------------------

_IRQL_requires_min_(PASSIVE_LEVEL)
_IRQL_requires_max_(DISPATCH_LEVEL)
_IRQL_requires_same_
NTSTATUS
IPv6ToBleRuntimeListAssignNewListEntry(
    _In_    WDFREQUEST  Request,
	_In_	ULONG		TargetList
);

//-----------------------------------------------------------------------------
// Functions to remove entries from the lists
//-----------------------------------------------------------------------------

_IRQL_requires_min_(PASSIVE_LEVEL)
_IRQL_requires_max_(DISPATCH_LEVEL)
_IRQL_requires_same_
NTSTATUS
IPv6ToBleRuntimeListRemoveListEntry(
    _In_    WDFREQUEST  Request,
	_In_	ULONG		TargetList
);

//-----------------------------------------------------------------------------
// Functions to clean up the runtime lists in the device context
//-----------------------------------------------------------------------------

_IRQL_requires_min_(PASSIVE_LEVEL)
_IRQL_requires_max_(DISPATCH_LEVEL)
_IRQL_requires_same_
VOID
IPv6ToBleRuntimeListPurgeRuntimeList(
	_In_ ULONG TargetList
);

#endif  // _RUNTIMELIST_H_