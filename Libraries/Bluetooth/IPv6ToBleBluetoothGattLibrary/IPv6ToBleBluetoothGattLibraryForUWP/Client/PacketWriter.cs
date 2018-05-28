using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;

using IPv6ToBleBluetoothGattLibraryForUWP.Helpers;

namespace IPv6ToBleBluetoothGattLibraryForUWP.Client
{
    /// <summary>
    /// This class writes a packet to a desired remote device.
    /// </summary>
    public static class PacketWriter
    {
        /// <summary>
        /// Write a packet to a given device.
        /// </summary>
        /// <returns></returns>
        public static async Task<bool> WritePacketAsync(
            DeviceInformation   targetDevice,
            byte[]              packet
        )
        {
            BluetoothError status = BluetoothError.Success;

            // Variables for the remote device, the IPv6ToBle packet processing
            // service, IPSS, and the device's characteristics
            BluetoothLEDevice device = null;
            GattDeviceService ipv6ToBlePacketProcessingService = null;
            IReadOnlyList<GattCharacteristic> deviceCharacteristics = null;

            // Variable for the remote packet write characteristic
            GattCharacteristic ipv6PacketWriteCharacteristic = null;

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
                GattDeviceServicesResult servicesResult = await device.GetGattServicesAsync(BluetoothCacheMode.Uncached);

                if (servicesResult.Status == GattCommunicationStatus.Success)
                {
                    var services = servicesResult.Services;
                    Debug.WriteLine($"Found {services.Count} services");

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
                        await ipv6ToBlePacketProcessingService.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);

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
                                "permissions issues. " + e.Message
                                );
                goto Exit;
            }

            if (deviceCharacteristics != null)
            {
                // Find the packet write characteristic
                foreach (GattCharacteristic characteristic in deviceCharacteristics)
                {
                    if (characteristic.Uuid == Constants.IPv6ToBlePacketWriteCharacteristicUuid)
                    {
                        ipv6PacketWriteCharacteristic = characteristic;
                        break;
                    }
                }
            }

            if (ipv6PacketWriteCharacteristic == null)
            {
                status = BluetoothError.OtherError;
                Debug.WriteLine("Could not access the IPv6 address" +
                                " characteristic and the packet write" +
                                " characteristic.");
                goto Exit;
            }

            //
            // Step 5
            // Write the packet now that we have verified that the device is
            // supported and is either the destination or in the path to the
            // destination
            //
            GattCommunicationStatus writeStatus =
                await ipv6PacketWriteCharacteristic.WriteValueAsync(GattHelpers.ConvertByteArrayToBuffer(packet));
            if (writeStatus != GattCommunicationStatus.Success)
            {
                status = BluetoothError.OtherError;
                Debug.WriteLine("Could not write the IPv6 packet to the" +
                                " remote device");
            }

            Exit:

            // Dispose of the device so Windows doesn't maintain a
            // connection; the caller will reconnect later if needed
            if (device != null)
            {
                device.Dispose();
            }

            if (status != BluetoothError.Success)
            {
                return false;
            }

            return true;
        }
    }
}
