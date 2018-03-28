/*++

Module Name:

    Includes.h

Abstract:

    This files includes all common include files for this project.

Environment:

    Kernel-mode Driver Framework

--*/

#ifndef _INCLUDES_H_
#define _INCLUDES_H_

// Headers for driver development
#include <ntddk.h>
#include <wdf.h>

EXTERN_C_START

// Networking headers
#define NDIS630 			    // Minimum NDIS version for this driver
#include <ndis.h>				// Windows kernel-mode networking I/O
#include <ws2ipdef.h>			// TCP/IP definitions
#include <in6addr.h>			// structure to hold IPv6 addresses
#include <ip2string.h>			// For address <-> string literal conversions
#include <strsafe.h>            // String functions safer than C STD LIB ones
#include <ntintsafe.h>          // For multiplying size_t values
#include <winerror.h>           // Windows error codes (not NTSTATUS)

// GUID header
#include <initguid.h>

// Windows Filtering Platform (WFP) headers
#pragma warning(push)
#pragma warning(disable:4201)	// unnamed struct/union
#include <fwpsk.h>				// WFP Servicing
#pragma warning(pop)			
#include <fwpmk.h>				// WFP Management

// Windows PreProcessor (WPP) tracing header
#include "trace.h"

// Other headers in this project
#include "Driver.h"             // The driver object definitions, entry, unload
#include "Device.h"				// The device object definitions
#include "Queue.h"				// I/O queue definitions
#include "callout.h"			// Our custom callout driver callbacks
#include "RuntimeList.h"        // Working with runtime white and mesh lists

#include "Helpers_NDIS.h"		// Helpers for kernel mode networking
#include "Helpers_NetBuffer.h"	// Helpers for user <-> kernel translation
#include "Helpers_Registry.h"	// Helpers for working with the registry
#include "Helpers_IPAddress.h"  // Helpers to validate/translate IPv6 addresses

EXTERN_C_END

#endif  // _INCLUDES_H_
