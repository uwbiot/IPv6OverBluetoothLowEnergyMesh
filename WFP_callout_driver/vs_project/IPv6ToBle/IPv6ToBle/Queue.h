/*++

Module Name:

    queue.h

Abstract:

    This file contains the queue definitions. This includes functions to set
	up the queues, as well as the IOCTL handling function and all the functions
	that handler calls to do its work.

Environment:

    Kernel-mode Driver Framework

--*/

EXTERN_C_START

//-----------------------------------------------------------------------------
// Initialization function for creating queues
//-----------------------------------------------------------------------------

_IRQL_requires_min_(PASSIVE_LEVEL)
_IRQL_requires_max_(DISPATCH_LEVEL)
_IRQL_requires_same_
NTSTATUS
IPv6ToBleQueuesInitialize(
    _In_ WDFDEVICE Device
);

//-----------------------------------------------------------------------------
// I/O event callback - i.e. callback for receiving IOCTLs from apps
//-----------------------------------------------------------------------------

_IRQL_requires_min_(PASSIVE_LEVEL)
_IRQL_requires_max_(DISPATCH_LEVEL)
_IRQL_requires_same_
EVT_WDF_IO_QUEUE_IO_DEVICE_CONTROL IPv6ToBleEvtIoDeviceControl;

//-----------------------------------------------------------------------------
// Function to inject IPv6 packets into the outbound network layer data path,
// as well as helper completion callback
//-----------------------------------------------------------------------------

_IRQL_requires_min_(PASSIVE_LEVEL)
_IRQL_requires_max_(DISPATCH_LEVEL)
_IRQL_requires_same_
_Check_return_
NTSTATUS
IPv6ToBleQueueInjectNetworkInboundV6(
    _In_ WDFREQUEST	Request
);

_IRQL_requires_min_(PASSIVE_LEVEL)
_IRQL_requires_max_(DISPATCH_LEVEL)
_IRQL_requires_same_
_Check_return_
NTSTATUS
IPv6ToBleQueueInjectNetworkOutboundV6(
	_In_ WDFREQUEST	Request
);

_IRQL_requires_min_(PASSIVE_LEVEL)
_IRQL_requires_max_(DISPATCH_LEVEL)
_IRQL_requires_same_
VOID
IPv6ToBleQueueInjectNetworkComplete(
    _In_    void*               context,
    _Inout_ NET_BUFFER_LIST*    netBufferList,
    _In_    BOOLEAN             dispatchLevel
);

EXTERN_C_END