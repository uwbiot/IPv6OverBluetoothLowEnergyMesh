using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;

using IPv6ToBleBluetoothGattLibraryForUWP;
using IPv6ToBleBluetoothGattLibraryForUWP.Helpers;
using Windows.Storage.Streams;

namespace IPv6ToBleBluetoothGattLibraryForUWP.Client
{
    /// <summary>
    /// This class writes a packet to a desired remote device.
    /// </summary>
    public static class PacketWriter
    {
        /// <summary>
        /// Writes a packet to a given device.
        /// </summary>
        /// <param name="targetDevice">The target device.</param>
        /// <param name="packet">The packet, compressed or not.</param>
        /// <param name="compressedHeaderLength">Optional. The length of the compressed header if the caller is sending a compressed packet.</param>
        /// <param name="payloadLength">Optional. The paylaod length of the packet. Only needed if the caller is sending a compressed packet.</param>
        /// <returns></returns>
        public static async Task<bool> WritePacketAsync(
            DeviceInformation targetDevice,
            byte[] packet,
            int compressedHeaderLength,
            int payloadLength
        )
        {
            BluetoothError status = BluetoothError.Success;

            // Variables for the remote device, the IPv6ToBle packet processing
            // service, IPSS, and the device's characteristics
            BluetoothLEDevice device = null;
            GattDeviceService ipv6ToBlePacketProcessingService = null;
            IReadOnlyList<GattCharacteristic> deviceCharacteristics = null;

            // Variables for the remote packet write characteristic
            GattCharacteristic ipv6PacketWriteCharacteristic = null;
            GattCharacteristic compressedHeaderLengthCharacteristic = null;
            GattCharacteristic payloadLengthCharacteristic = null;

            //
            // Step 1
            // Connect to the device
            // 
            try
            {
                // Connect based on the device's Bluetooth address
                device = await BluetoothLEDevice.FromIdAsync(targetDevice.Id);

                if (device == null)
                {
                    status = BluetoothError.DeviceNotConnected;
                    Debug.WriteLine("Error connecting to device.");
                    goto Exit;
                }
            }
            catch (Exception e) when (e.HResult == Constants.E_DEVICE_NOT_AVAILABLE)
            {
                status = BluetoothError.RadioNotAvailable;
                Debug.WriteLine("Bluetooth radio is not on.");
                goto Exit;
            }

            //
            // Step 2
            // Enumerate the GATT services to get the 
            // IPv6ToBlePacketProcessingService
            //
            if (device != null)
            {
                // Retrieve the list of services from the device (uncached)
                GattDeviceServicesResult servicesResult = await device.GetGattServicesAsync(BluetoothCacheMode.Cached);

                if (servicesResult.Status == GattCommunicationStatus.Success)
                {
                    var services = servicesResult.Services;
                    Debug.WriteLine($"Found {services.Count} services when " +
                                    "querying services for packet writing."
                                    );

                    // Iterate through the list of services and check if
                    // both services we require are there
                    foreach (GattDeviceService service in services)
                    {
                        Guid uuid = service.Uuid;

                        // Check for IPv6ToBle Packet Write Service
                        if (uuid == Constants.IPv6ToBlePacketProcessingServiceUuid)
                        {
                            ipv6ToBlePacketProcessingService = service;
                            break;
                        }
                    }
                }
            }

            // Report error if the device was not running our packet processing
            // service for some reason
            if (ipv6ToBlePacketProcessingService == null)
            {
                status = BluetoothError.OtherError;
                Debug.WriteLine("Device did not have the " +
                                "IPv6ToBlePacketProcessingService running or" +
                                " available."
                                );
                goto Exit;
            }

            //
            // Step 3
            // Enumerate the GATT characteristics
            //
            try
            {
                // Verify we can access the service
                DeviceAccessStatus accessStatus = await ipv6ToBlePacketProcessingService.RequestAccessAsync();
                if (accessStatus == DeviceAccessStatus.Allowed)
                {
                    // Enumerate the characteristics
                    GattCharacteristicsResult characteristicsResult =
                        await ipv6ToBlePacketProcessingService.GetCharacteristicsAsync(BluetoothCacheMode.Cached);

                    if (characteristicsResult.Status == GattCommunicationStatus.Success)
                    {
                        deviceCharacteristics = characteristicsResult.Characteristics;
                    }
                    else
                    {
                        status = BluetoothError.OtherError;
                        Debug.WriteLine("Could not access the packet " +
                                        "processing service."
                                        );
                        goto Exit;
                    }
                }
                else
                {
                    // Not granted access
                    status = BluetoothError.NotSupported;

                    Debug.WriteLine("Could not access the packet " +
                                    "processing service."
                                    );
                    goto Exit;
                }
            }
            catch (Exception e)
            {
                status = BluetoothError.DeviceNotConnected;
                Debug.WriteLine("Could not read characteristics due to " +
                                " permissions issues. " + e.Message
                                );
                goto Exit;
            }

            // Find the required characteristics for packet writing
            if (deviceCharacteristics != null)
            {
                foreach (GattCharacteristic characteristic in deviceCharacteristics)
                {
                    if (characteristic.Uuid == Constants.IPv6ToBlePacketWriteCharacteristicUuid)
                    {
                        ipv6PacketWriteCharacteristic = characteristic;
                    }
                    if(characteristic.Uuid == Constants.IPv6ToBleCompressedHeaderLengthCharacteristicUuid)
                    {
                        compressedHeaderLengthCharacteristic = characteristic;
                    }
                    if(characteristic.Uuid == Constants.IPv6ToBlePayloadLengthCharacteristicUuid)
                    {
                        payloadLengthCharacteristic = characteristic;
                    }
                }
            }

            if (ipv6PacketWriteCharacteristic == null ||
                compressedHeaderLengthCharacteristic == null ||
                payloadLengthCharacteristic == null)
            {
                status = BluetoothError.OtherError;
                Debug.WriteLine("Could not access all three characteristics " +
                                "required for packet writing."
                                );
                goto Exit;
            }

            //
            // Step 5
            // Write the packet now that we have verified that the device is
            // supported and is either the destination or in the path to the
            // destination
            //            

            DataWriter writer = new DataWriter();

            // Write the compressed header length
            writer.WriteInt32(compressedHeaderLength);
            GattCommunicationStatus writeStatus = await compressedHeaderLengthCharacteristic.WriteValueAsync(writer.DetachBuffer());
            if (writeStatus != GattCommunicationStatus.Success)
            {
                status = BluetoothError.OtherError;
                Debug.WriteLine("Could not write compressed header length.");
                goto Exit;
            }

            // Write the payload length
            writer = new DataWriter();
            writer.WriteInt32(payloadLength);
            writeStatus = await payloadLengthCharacteristic.WriteValueAsync(writer.DetachBuffer());
            if (writeStatus != GattCommunicationStatus.Success)
            {
                status = BluetoothError.OtherError;
                Debug.WriteLine("Could not write payload length.");
                goto Exit;
            }

            // Write the packet itself last so the receiver knows that it has
            // all needed data when this characteristic is written to
            writeStatus = await ipv6PacketWriteCharacteristic.WriteValueAsync(GattHelpers.ConvertByteArrayToBuffer(packet));
            if (writeStatus != GattCommunicationStatus.Success)
            {
                status = BluetoothError.OtherError;
                Debug.WriteLine("Could not write the IPv6 packet to the" +
                                " remote device");
                goto Exit;
            }

            Exit:

            // Dispose of the service and device that we accessed, then force
            // a garbage collection to destroy the objects and fully disconnect
            // from the remote GATT server and device. This is as a workaround
            // for a current Windows bug that doesn't properly disconnect
            // devices, as well as a workaround for the Broadcomm Bluetooth LE
            // driver on the Raspberry Pi 3 that can't handle multiple connects
            // and reconnects if it thinks it's still occupied.
            //
            // Additionally, at this step, if you had connected any events
            // to the services or characteristics, you'd have to do that first.
            // But we didn't do that here, so no need.

            ipv6ToBlePacketProcessingService?.Dispose();
            device?.Dispose();
            device = null;
            GC.Collect();

            if (status != BluetoothError.Success)
            {
                return false;
            }

            return true;
        }
    }
}
