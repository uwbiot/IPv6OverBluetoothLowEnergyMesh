# IPv6ToBleBluetoothAdvLibrary overview

The IPv6ToBleBluetoothAdvLibraryForUWP DLL contains code for Bluetooth Low Energy (BLE) Advertisement functionality.

## General info and purpose

The original purpose of this library was to advertise the presence of a device running this project's GATT server to other nearby devices. However, it was determined that the Bluetooth radio did not support simultaneously starting a GATT server and running an Advertisement publisher, because the GATT services occupied the radio with their advertisements. Therefore, this code was not used in the final running product. However, it is left intact for completeness and in the event that someone extending this project would like to use Advertisements for any reason.

For more info about Bluetooth Advertisements on Windows, see [Bluetooth LE Advertisements](https://docs.microsoft.com/windows/uwp/devices-sensors/ble-beacon).

## Classes

- IPv6ToBleAdvPublisherPacketReceive.cs
    - Publishes advertisements with the "IPv6ToBle" manufacturer name to identify the device as being compatible with this project. Advertises that this device can receive a packet because it is running the GATT server.
- IPv6ToBleAdvWatcherPacketWrite.cs
    - Watches for advertisements from nearby compatible devices in order to write a packet to them.