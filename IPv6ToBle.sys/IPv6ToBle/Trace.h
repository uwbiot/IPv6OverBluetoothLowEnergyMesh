/*++

Module Name:

    Trace.h

Abstract:

    Header file for the debug tracing related function defintions and macros.

Environment:

    Kernel mode

--*/

//
// Define the tracing flags.
//
// Tracing GUID - 79839457-6152-49e9-bf8e-775e3470d92f
//
// You can define up to 32 flags here for different tracing purposes in your
// driver. Flags are defined for each major object (driver, device, and queue),
// as well as for major callout operations, for the timer callback, and for the
// helper headers.
//

#define WPP_CONTROL_GUIDS                                              \
    WPP_DEFINE_CONTROL_GUID(                                           \
        IPv6ToBleTraceGuid, (79839457,6152,49e9,bf8e,775e3470d92f),    \
                                                                       \
        WPP_DEFINE_BIT(MYDRIVER_ALL_INFO)                              \
        WPP_DEFINE_BIT(TRACE_DRIVER)                                   \
        WPP_DEFINE_BIT(TRACE_DEVICE)                                   \
        WPP_DEFINE_BIT(TRACE_QUEUE)                                    \
        WPP_DEFINE_BIT(TRACE_CALLOUT_REGISTRATION)                     \
        WPP_DEFINE_BIT(TRACE_CLASSIFY_INBOUND_IP_PACKET_V6)            \
        WPP_DEFINE_BIT(TRACE_CLASSIFY_OUTBOUND_IP_PACKET_V6)           \
        WPP_DEFINE_BIT(TRACE_NOTIFY)                                   \
        WPP_DEFINE_BIT(TRACE_INJECT_NETWORK_INBOUND)                   \
        WPP_DEFINE_BIT(TRACE_INJECT_NETWORK_OUTBOUND)                  \
        WPP_DEFINE_BIT(TRACE_INJECT_NETWORK_COMPLETE)                  \
        WPP_DEFINE_BIT(TRACE_HELPERS_IP_ADDRESS)                       \
        WPP_DEFINE_BIT(TRACE_HELPERS_NDIS)                             \
        WPP_DEFINE_BIT(TRACE_HELPERS_NET_BUFFER)                       \
        WPP_DEFINE_BIT(TRACE_HELPERS_REGISTRY)                         \
        WPP_DEFINE_BIT(TRACE_RUNTIME_LIST)                             \
        WPP_DEFINE_BIT(TRACE_TIMER)                                    \
        )                             

#define WPP_FLAG_LEVEL_LOGGER(flag, level)                                  \
    WPP_LEVEL_LOGGER(flag)

#define WPP_FLAG_LEVEL_ENABLED(flag, level)                                 \
    (WPP_LEVEL_ENABLED(flag) &&                                             \
     WPP_CONTROL(WPP_BIT_ ## flag).Level >= level)

#define WPP_LEVEL_FLAGS_LOGGER(lvl,flags) \
           WPP_LEVEL_LOGGER(flags)
               
#define WPP_LEVEL_FLAGS_ENABLED(lvl, flags) \
           (WPP_LEVEL_ENABLED(flags) && WPP_CONTROL(WPP_BIT_ ## flags).Level >= lvl)

//           
// WPP orders static parameters before dynamic parameters. To support the Trace function
// defined below which sets FLAGS=MYDRIVER_ALL_INFO, a custom macro must be defined to
// reorder the arguments to what the .tpl configuration file expects.
//
#define WPP_RECORDER_FLAGS_LEVEL_ARGS(flags, lvl) WPP_RECORDER_LEVEL_FLAGS_ARGS(lvl, flags)
#define WPP_RECORDER_FLAGS_LEVEL_FILTER(flags, lvl) WPP_RECORDER_LEVEL_FLAGS_FILTER(lvl, flags)

//
// This comment block is scanned by the trace preprocessor to define our
// Trace function.
//
// begin_wpp config
// FUNC Trace{FLAGS=MYDRIVER_ALL_INFO}(LEVEL, MSG, ...);
// FUNC TraceEvents(LEVEL, FLAGS, MSG, ...);
// end_wpp
//
