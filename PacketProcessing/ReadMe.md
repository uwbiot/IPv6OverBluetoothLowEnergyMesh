# PacketProcessing overview

This ReadMe describes the user mode packet processing app's purpose and functionality, as well as a summary of the classes and objects it contains.

## General info and purpose

The packet processing app is a Universal Windows Platform (UWP) app written in C# and XAML. It is a standard foreground UWP app that will run on any Windows 10 SKU, as long as it is compiled for the correct processor architecture.

The purpose of the packet processing app is to act as the second half of the bridge between the standard TCP/IP network stack and the Bluetooth Low Energy (BLE) stack, the first half being the driver, IPv6ToBle.sys. It also acts as a BLE relay if the underlying Bluetooth radio supports both Generic Attribute (GATT) client and server roles.

## Overview of functionality

Packet processing is designed to both receive and transmit IPv6 packets. It can either receive packets from the driver, or it can receive packets over the air via Bluetooth LE. These two operations occur at different times depending on the flow of data through the system, and depending on the role that the device is playing.

## High-level code order of operations

### Initialization

1. If running on a border router device, typically an x86- or x64-based machine, query for the local IPv6 addresses. Else, call into the SixLowPanLibrary if running on a node device to generate a link-local IPv6 address based on the local Bluetooth radio ID.
2. Scan for and enumerate nearby Bluetooth LE devices. Filter them for supported devices if they are running this project's Bluetooth GATT server.
3. Start the GATT server if running on a node device or a device that supports the Bluetooth LE GAP Central role.
4. Initialize a message queue to track messages that have been seen before, to prevent duplicate transmissions.
5. Send an initial listening request to the driver to request a packet.

### Running

1. Receive a packet from the driver
    1. Compress the IPv6 header of the packet by calling into the 6LoWPAN library
    2. Check if the packet is destined for an immediate neighbor; if so, send the packet to just that neighbor
    3. If the packet is not intended for a neighbor, it must be intended for a device more than one hop away; send the packet over BLE to each supported device previously found
    4. Send another listening request to the driver
2. Receive a packet over BLE
    1. Decompress the IPv6 header by calling into the 6LoWPAN library
    2. Examine the packet to see if it is intended for this device
    3. Add the packet to the message queue
    4. Re-transmit the packet over BLE if it is not for this device and has not been seen before

## Classes

The packet processing app code is entirely contained in the MainPage.xaml.cs file. Initialization is triggerred by clicking the "Start" button, and the "Stop" button shuts down Bluetooth resources before exiting.