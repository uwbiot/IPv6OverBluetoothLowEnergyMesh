# IPv6ToBleBluetoothGattLibrary overview

The IPv6ToBleBluetoothGattLibrary DLL contains code for Bluetooth Low Energy (BLE) Generic Attribute (GATT) client and server functionality. This is the primary library that the packet processing app uses to talk to the Windows BLE stack.

## General info and purpose

Communication over Bluetooth low energy occurs in two ways: with the Generic Attribute (GATT) profile, or with Advertisements. Advertisements are small, ~20 byte payload messages that are broadcasted by a publisher and recived by a watcher. This form of communicating is best suited for beaconing out small payloads, such as sensor readings on a low-power node device. For all other forms of BLE communication, GATT is the foundation.

This project transmits IPv6 packets over the air as blobs of bytes; because most packets, even when compressed, are larger than 20 bytes, Advertisements are not suitable for this need. With GATT, one can define services and characteristics that can provide or receive data in custom ways. Depending on whether a device is the one sending or receiving the data, its role is either that of a server or a client. This library provides code for both of these roles and provides for future expandability in defining new GATT services or characteristics.

For more info about GATT client on Windows, see [Bluetooth GATT Client](https://docs.microsoft.com/windows/uwp/devices-sensors/gatt-client).

For more info about GATT server on Windows, see [Bluetooth GATT Server](https://docs.microsoft.com/windows/uwp/devices-sensors/gatt-server).

## Classes

- Characteristics
    - GenericGattCharacteristic.cs
        - Represents a base GATT characteristic that can be extended for custom functionality. It contains the base implementations of basic operations such as responding to read requests, write requests, and notify requests. This class is based off the class of the same name in the Microsoft BluetoothLE sample.
    - IPv6ToBleIPv6AddressCharacteristic.cs
        - Extends the GenericGattCharacteristic class to represent a read-only characteristic that stores the device's link-local IPv6 address. This is so remote devices can query and know each device's IPv6 address.
    - IPv6ToBlePacketWriteCharacteristic
        - Extends the GenericGattCharacteristic class to represent a write-only characteristic that stores a byte array; in other words, an IPv6 packet transmitted to this device.
- Client
    - DeviceEnumerator.cs
        - Uses a DeviceWatcher class internally to scan for nearby Bluetooth LE devices. After scanning for found devices, provides a method to filter those devices by querying them to see if they support this project's GATT services.
    - PacketWriter.cs
        - Writes a packet to a previously discovered and filtered device. Queries GATT services and characteristics on the remote device, then writes to the IPv6ToBlePacketWriteCharacteristic of the remote device.
- Helpers
    - BluetoothRoleSupport.cs
        - Queries the local Bluetooth radio to determine if it support the Bluetooth GAP central role (client/master) or GAP peripheral role (server/slave).
    - Constants.cs
        - Contains definitions for constants used throughout the library, such as UUID (GUID) values and error codes.
    - CreateServiceException.cs
        - Custom Exception definition for when creating a GATT service fails.
    - GattHelpers.cs
        - Contains helpers specific to GATT, such as characteristic parameters, byte array <-> IBuffer converters, and UUID helper functions.
    - Utilities.cs
        - Generic helpers for working with byte arrays, such as for printing a byte array or comparing two byte arrays.
- Server
    - GattServer.cs
        - Represents a GATT server object that sets up and advertises this project's GATT services.
- Services
    - GenericGattService.cs
        - Represents a base GATT service that can be extended for custom functionality. It contains the base implementations for initializing, starting, and stopping the service. This class is based off the class of the same name in the Microsoft BluetoothLE sample.
    - InternetProtocolSupportService.cs
        - Extends the GenericGattService. A GATT service that conforms to the Bluetooth SIG-defined Internet Protocol Support Service. The presence of this service indicates that a device supports transferring IPv6 packets over BLE. This service has no characteristics.
    - IPv6ToBlePacketProcessingService.cs
        - A custom service for this project that extends the generic GATT service and provides two characteristics: the IPv6ToBleIPv6AddressCharacteristic and the IPv6ToBlePacketWriteCharacteristic.