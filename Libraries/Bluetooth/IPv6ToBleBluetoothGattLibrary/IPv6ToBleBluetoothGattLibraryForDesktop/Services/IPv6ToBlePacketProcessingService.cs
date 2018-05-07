using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Diagnostics;

// Namespaces for our project
using IPv6ToBleSixLowPanLibraryForDesktop;
using IPv6ToBleBluetoothGattLibraryForDesktop.Characteristics;
using IPv6ToBleBluetoothGattLibraryForDesktop.Helpers;

// UWP namespaces from .NET Core
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace IPv6ToBleBluetoothGattLibraryForDesktop.Services
{
    /// <summary>
    /// The GATT service for supporting IPv6 packet transfer.
    /// 
    /// This service contains unique business logic information required to
    /// send IPv6 packets over BLE, including these characteristics:
    /// 
    /// - Packet write characteristic
    /// - IPv6 address read characteristic
    /// 
    /// A server runs this service to *receive* a packet. A client sends a
    /// packet by connecting to the server, verifying this service is on the
    /// server, selecting the packet write characteristic, and writing to it.
    /// </summary>
    public class IPv6ToBlePacketProcessingService : GenericGattService
    {
        //---------------------------------------------------------------------
        // Name of the service
        //---------------------------------------------------------------------

        // The name of the service
        public override string Name
        {
            get
            {
                return "IPv6 to Bluetooth Low Energy Packet Service";
            }
        }

        //---------------------------------------------------------------------
        // Characteristics
        //---------------------------------------------------------------------

        // Characteristic to receive a write of an IPv6 packet byte array
        private IPv6ToBlePacketWriteCharacteristic packetWriteCharacteristic;

        public IPv6ToBlePacketWriteCharacteristic PacketWriteCharacteristic
        {
            get
            {
                return packetWriteCharacteristic;
            }
            set
            {
                if(packetWriteCharacteristic != value)
                {
                    packetWriteCharacteristic = value;
                    OnPropertyChanged(new PropertyChangedEventArgs("PacketWriteCharacteristic"));
                }
            }
        }

        private IPv6ToBleIPv6AddressCharacteristic iPv6AddressCharacteristic;

        public IPv6ToBleIPv6AddressCharacteristic IPv6AddressCharacteristic
        {
            get
            {
                return iPv6AddressCharacteristic;
            }
            set
            {
                if(iPv6AddressCharacteristic != value)
                {
                    iPv6AddressCharacteristic = value;
                    OnPropertyChanged(new PropertyChangedEventArgs("IPv6AddressCharacteristic"));
                }
            }
        }

        //---------------------------------------------------------------------
        // Asynchronous initialization
        //---------------------------------------------------------------------

        public override async Task InitAsync()
        {
            await CreateServiceProvider(Constants.IPv6ToBlePacketProcessingServiceUuid);

            GattLocalCharacteristicResult characteristicResult = null;

            //
            // Step 1
            // Create the packet write characteristic
            //
            GattLocalCharacteristic createdPacketWriteCharacteristic = null;
            characteristicResult = await ServiceProvider.Service.CreateCharacteristicAsync(
                                            Constants.IPv6ToBlePacketWriteCharacteristicUuid,
                                            GattHelpers.packetWriteParameters
                                            );

            //
            // Step 2
            // Assign the created packet write characteristic to this service's internal one
            //
            GattHelpers.GetCharacteristicFromResult(characteristicResult,
                                                    ref createdPacketWriteCharacteristic
                                                    );
            if(createdPacketWriteCharacteristic != null)
            {
                PacketWriteCharacteristic = new IPv6ToBlePacketWriteCharacteristic(
                                                createdPacketWriteCharacteristic,
                                                this
                                            );
            }

            characteristicResult = null;

            //
            // Step 3
            // Create the IPv6 address read characteristic
            //

            // Generate the device's link-local IPv6 address
            IPAddress address = await IPv6AddressFromBluetoothAddress.GenerateAsync(2);
            if(address == null)
            {
                Debug.WriteLine("Could not generate a link-local IPv6 address" +
                                " from the Bluetooth address."
                                );
                return;
            }

            // Create the characteristic
            GattLocalCharacteristic createdIPv6AddressReadCharacteristic = null;
            characteristicResult = await ServiceProvider.Service.CreateCharacteristicAsync(
                                            Constants.IPv6ToBleIPv6AddressCharacteristicUuid,
                                            GattHelpers.ipv6AddressReadParameters
                                            );

            //
            // Step 4
            // Assign the created IPv6 address read characteristic to this service's internal one
            //
            GattHelpers.GetCharacteristicFromResult(characteristicResult,
                                                    ref createdIPv6AddressReadCharacteristic
                                                    );
            if(createdIPv6AddressReadCharacteristic != null)
            {
                IPv6AddressCharacteristic = new IPv6ToBleIPv6AddressCharacteristic(
                                                createdIPv6AddressReadCharacteristic,
                                                this,
                                                address
                                            );
            }
        }
    }
}
