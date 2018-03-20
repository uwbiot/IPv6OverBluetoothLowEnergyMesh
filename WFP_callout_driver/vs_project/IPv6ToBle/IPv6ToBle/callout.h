/*++

Module Name:

	callout.h

Abstract:

	This file contains definitions and function declarations for the WFP
	callout driver's classify and notify callbacks, as well as functions to
    register and unregister the callbacks.

	This callout driver is very simple, even for a WFP callout driver. On the
    border router device, it has one callout at the outbound IPv6 network
    layer (the IP_PACKET layer) and one callout at the inbound IPv6 network
    layer (also the IP_PACKET layer). On the node devices, it has only one
    callout at the outbound IPv6 network layer.

	This driver uses the Windows 8 and later version of the WFP APIs, e.g.
	the structures and functions that end with a "2," if possible.

Environment:

	Kernel-mode Driver Framework

--*/

#ifndef _CALLOUT_H_
#define _CALLOUT_H_

EXTERN_C_START

//-----------------------------------------------------------------------------
// Prototypes for the callout driver's classify and notify callbacks
//-----------------------------------------------------------------------------

_IRQL_requires_min_(PASSIVE_LEVEL)
_IRQL_requires_max_(DISPATCH_LEVEL)
_IRQL_requires_same_
VOID
IPv6ToBleCalloutClassifyInboundIpPacketV6(
    _In_		const FWPS_INCOMING_VALUES0*			inFixedValues,
    _In_		const FWPS_INCOMING_METADATA_VALUES0*	inMetaValues,
    _Inout_opt_	void*									layerData,
    _In_opt_	const void*								classifyContext,
    _In_		const FWPS_FILTER2*						filter,
    _In_		UINT64									flowContext,
    _Inout_		FWPS_CLASSIFY_OUT0*						classifyOut
);

_IRQL_requires_min_(PASSIVE_LEVEL)
_IRQL_requires_max_(DISPATCH_LEVEL)
_IRQL_requires_same_
VOID
IPv6ToBleCalloutClassifyOutboundIpPacketV6(
    _In_		const FWPS_INCOMING_VALUES0*			inFixedValues,
    _In_		const FWPS_INCOMING_METADATA_VALUES0*	inMetaValues,
    _Inout_opt_	void*									layerData,
    _In_opt_	const void*								classifyContext,
    _In_		const FWPS_FILTER2*						filter,
    _In_		UINT64									flowContext,
    _Inout_		FWPS_CLASSIFY_OUT0*						classifyOut
);

_IRQL_requires_min_(PASSIVE_LEVEL)
_IRQL_requires_max_(DISPATCH_LEVEL)
_IRQL_requires_same_
NTSTATUS
IPv6ToBleCalloutNotifyIpPacket(
    _In_	FWPS_CALLOUT_NOTIFY_TYPE	notifyType,
    _In_	const GUID*					filterKey,
    _Inout_	const FWPS_FILTER2*			filter
);

//-----------------------------------------------------------------------------
// Functions to register and unregister the callout and add the filter
//-----------------------------------------------------------------------------
_IRQL_requires_(PASSIVE_LEVEL)
_IRQL_requires_same_
_Success_(return == STATUS_SUCCESS)
NTSTATUS
IPv6ToBleCalloutsRegister();

_IRQL_requires_(PASSIVE_LEVEL)
_IRQL_requires_same_
_Success_(return == STATUS_SUCCESS)
NTSTATUS
IPv6ToBleCalloutRegisterInboundIpPacketV6Callout(
    _In_    const GUID* layerKey,
    _In_    const GUID* calloutKey,
    _Out_   UINT32*     calloutId
);

_IRQL_requires_(PASSIVE_LEVEL)
_IRQL_requires_same_
_Success_(return == STATUS_SUCCESS)
NTSTATUS
IPv6ToBleCalloutRegisterOutboundIpPacketV6Callout(
    _In_    const GUID* layerKey,
    _In_    const GUID* calloutKey,
    _Out_   UINT32*     calloutId
);

_IRQL_requires_(PASSIVE_LEVEL)
_IRQL_requires_same_
_Success_(return == STATUS_SUCCESS)
NTSTATUS
IPv6ToBleCalloutFilterAdd(
    _In_				wchar_t*	    filterName,
    _In_				wchar_t*	    filterDesc,
    _In_reads_opt_(16)	const UINT8*	remoteAddress,
    _In_				const GUID*		layerKey,
    _In_				const GUID*		calloutKey
);

_IRQL_requires_(PASSIVE_LEVEL)
_IRQL_requires_same_
VOID
IPv6ToBleCalloutsUnregister();

//-----------------------------------------------------------------------------
// Callout and sublayer GUIDs
//-----------------------------------------------------------------------------

// {D0BE33C5-DDE0-4B7C-85A1-653A94816D43}
DEFINE_GUID(
    IPV6_TO_BLE_INBOUND_IP_PACKET_V6,
    0xd0be33c5,
    0xdde0,
    0x4b7c,
    0x85, 0xa1, 0x65, 0x3a, 0x94, 0x81, 0x6d, 0x43
);

// {A12028B8-3578-49C2-9084-6515412B6F80}
DEFINE_GUID(
    IPV6_TO_BLE_OUTBOUND_IP_PACKET_V6,
    0xa12028b8,
    0x3578,
    0x49c2,
    0x90, 0x84, 0x65, 0x15, 0x41, 0x2b, 0x6f, 0x80
);

// {0C364802-3E3B-4997-B104-F3CAFCD996CA}
DEFINE_GUID(
    IPV6_TO_BLE_SUBLAYER,
    0xc364802,
    0x3e3b,
    0x4997,
    0xb1, 0x4, 0xf3, 0xca, 0xfc, 0xd9, 0x96, 0xca
);

EXTERN_C_END

#endif	// _CALLOUT_H_