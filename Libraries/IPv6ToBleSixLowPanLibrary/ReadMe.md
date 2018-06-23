# IPv6ToBleSixLowPanLibrary overview

The IPv6ToBleSixLowPanLibrary DLL contains functions for IPv6 over Low-power Personal Area Networks (6LoWPAN).

## General info and purpose

6LoWPAN has three defined goals per RFC 6282:

1. Header compression
2. Fragmentation and reassembly
3. Stateless auto configuration

This library fulfills the first and third goals; the second goal is automatically performed at the Bluetooth L2CAP layer by the Windows Bluetooth LE APIs.

It is important to observe that this code is implemented as a library, not as an operating system layer or module. In open source operating systems such as Contiki OS, on which the header compression/decompression code was based for this library, 6LoWPAn is implemented as an adaptation layer in the network stack. This is not possible on Windows because it is closed source. Therefore, the *concept* of an adaptation layer is spread across the driver and this module, as shown in the system architecture diagram above. The *implementation* for 6LoWPAn itself lies here, though.

## Classes

- HeaderCompression.cs
    - Contains implementations of IPv6 header compression and decompression.
- StatelessAddressConfiguration.cs
    - Queries the local Bluetooth radio for its Bluetooth ID, then forms a link-local IPv6 address based off of it.