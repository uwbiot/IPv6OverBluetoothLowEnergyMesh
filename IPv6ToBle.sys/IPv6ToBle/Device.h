/*++

Module Name:

    device.h

Abstract:

    This file contains the device definitions. This driver is a non-PnP driver,
	so it only has one control device and no Device Add callback.

Environment:

    Kernel-mode Driver Framework

--*/

#include "Public.h"
#include "Helpers_NDIS.h"

EXTERN_C_START

//-----------------------------------------------------------------------------
// Function to initialize the device and its callbacks
//-----------------------------------------------------------------------------

_IRQL_requires_(PASSIVE_LEVEL)
_IRQL_requires_same_
_Check_return_
_Success_(return == STATUS_SUCCESS)
NTSTATUS
IPv6ToBleDeviceCreate(
    _In_	WDFDRIVER		Driver
);


EXTERN_C_END
