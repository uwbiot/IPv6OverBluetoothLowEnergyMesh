using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// Namespaces for our project
using IPv6ToBleBluetoothGattLibraryForDesktop.Characteristics;
using IPv6ToBleBluetoothGattLibraryForDesktop.Helpers;

// UWP namespaces from .NET Core
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace IPv6ToBleBluetoothGattLibraryForDesktop.Services
{
    /// <summary>
    /// The GATT service for supporting IPv6 packet transfer.
    /// 
    /// This service only supports write request operations.
    /// 
    /// A server runs this service to *receive* a packet. A client sends a
    /// packet by connecting to the server, verifying this service is on the
    /// server, selecting the packet write characteristic, and writing to it.
    /// </summary>
    public class IPv6ToBlePacketWriteService : GenericGattService
    {
        //---------------------------------------------------------------------
        // Name of the service and identifying GUIDs/UUIDs
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

        //---------------------------------------------------------------------
        // Asynchronous initialization
        //---------------------------------------------------------------------

        public override async Task Init()
        {
            await CreateServiceProvider(Constants.IPv6ToBlePacketWriteServiceUuid);

            GattLocalCharacteristicResult characteristicResult = null;

            //
            // Step 1
            // Prepare the packet write characteristic
            //
            GattLocalCharacteristicParameters writeParams = Constants.packetWriteParameters;
            writeParams.UserDescription = "IPv6 to BLE packet write characteristic";

            //
            // Step 2
            // Create the characteristic
            //
            GattLocalCharacteristic createdPacketWriteCharacteristic = null;
            characteristicResult = await ServiceProvider.Service.CreateCharacteristicAsync(
                                            Constants.IPv6ToBlePacketWriteCharacteristicUuid,
                                            writeParams
                                         );

            //
            // Step 3
            // Assign the created characteristic to this service's internal one
            //
            Utilities.GetCharacteristicFromResult(characteristicResult,
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
        }
    }
}
