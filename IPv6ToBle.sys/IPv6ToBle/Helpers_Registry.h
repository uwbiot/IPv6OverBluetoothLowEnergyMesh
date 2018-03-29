/*++

Module Name:

	Helpers_Registry.h

Abstract:

	This file contains definitions for helper functions to deal with the
	registry. This includes opening/creating the appropriate registry keys,
	loading registry information into runtime lists, and assigning the lists
    to the registry.

    This header is only used on the gateway device.

Environment:

	Kernel-mode Driver Framework

--*/

#ifndef _HELPERS_REGISTRY_H_
#define _HELPERS_REGISTRY_H_

//-----------------------------------------------------------------------------
// Functions to open the white list and mesh list keys
//-----------------------------------------------------------------------------

_IRQL_requires_(PASSIVE_LEVEL)
_IRQL_requires_same_
NTSTATUS
IPv6ToBleRegistryOpenWhiteListKey();

_IRQL_requires_(PASSIVE_LEVEL)
_IRQL_requires_same_
NTSTATUS
IPv6ToBleRegistryOpenMeshListKey();

//-----------------------------------------------------------------------------
// Functions to load white list and mesh list information from the registry and
// populate the runtime lists
//
// These functions are called during driver entry
//-----------------------------------------------------------------------------

_IRQL_requires_(PASSIVE_LEVEL)
_IRQL_requires_same_
NTSTATUS
IPv6ToBleRegistryRetrieveWhiteList();

_IRQL_requires_(PASSIVE_LEVEL)
_IRQL_requires_same_
NTSTATUS
IPv6ToBleRegistryRetrieveMeshList();

//-----------------------------------------------------------------------------
// Functions to store the white list and mesh lists in the registry
//
// These functions are called periodically by the device timer callback, which
// checks if the lists have changed and overwrites the registry key values if
// they have. The device timer is a periodic timer and is thus called at
// IRQL == DISPATCH_LEVEL.
//
// Because working with the registry requires many PASSIVE_LEVEL functions,
// the timer callback schedules a worker thread to perform the task if it
// detects that the runtime list has changed.
//-----------------------------------------------------------------------------

_IRQL_requires_(PASSIVE_LEVEL)
_IRQL_requires_same_
NTSTATUS
IPv6ToBleRegistryAssignWhiteList();

_IRQL_requires_(PASSIVE_LEVEL)
_IRQL_requires_same_
NTSTATUS
IPv6ToBleRegistryAssignMeshList();

_IRQL_requires_(PASSIVE_LEVEL)
_IRQL_requires_same_
IO_WORKITEM_ROUTINE_EX IPv6ToBleRegistryFlushWhiteListWorkItemEx;

_IRQL_requires_(PASSIVE_LEVEL)
_IRQL_requires_same_
IO_WORKITEM_ROUTINE_EX IPv6ToBleRegistryFlushMeshListWorkItemEx;

#endif	// _HELPERS_REGISTRY_H_