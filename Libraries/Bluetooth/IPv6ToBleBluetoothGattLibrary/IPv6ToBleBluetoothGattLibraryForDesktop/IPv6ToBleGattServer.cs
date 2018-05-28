using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

// Namespaces in this project
using IPv6ToBleBluetoothGattLibraryForDesktop.Services;
using IPv6ToBleBluetoothGattLibraryForDesktop.Helpers;

// UWP namespaces
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace IPv6ToBleBluetoothGattLibraryForDesktop
{
    /// <summary>
    /// Class for setting up and running GATT services on any device acting as
    /// a GATT server, which is all devices in the IPv6 Over Bluetooth Low
    /// Energy Mesh project. This is because all devices can write IPv6 packets
    /// to each other, so they must all be GATT servers to receive a packet
    /// at any time.
    /// 
    /// This class spins up and runs the Internet Protocol Support Service
    /// (IPSS) and the IPv6ToBlePacketProcessingService.
    /// </summary>
    public class IPv6ToBleGattServer
    {
        #region Local variables
        //---------------------------------------------------------------------
        // Local variables
        //---------------------------------------------------------------------

        // Boolean trackers to verify the GATT server device's radio supports
        // the GAP peripheral and central roles
        private bool isPeripheralRoleSupported;

        // Our two services
        public InternetProtocolSupportService InternetProtocolSupportService = new InternetProtocolSupportService();
        public IPv6ToBlePacketProcessingService PacketProcessingService = new IPv6ToBlePacketProcessingService();

        #endregion

        #region Start and stop
        //---------------------------------------------------------------------
        // Methods to start and stop the GATT server
        //---------------------------------------------------------------------

        // Starts the GATT server and starts the services
        public async Task<bool> StartAsync()
        {
            //
            // Step 1
            // Verify the local Bluetooth radio supports the peripheral role
            // 
            isPeripheralRoleSupported = await BluetoothRoleSupport.CheckPeripheralSupportAsync();

            // Only return false if neither are supported; this way, if at
            // least one is supported the device can act as a GATT server
            // and its functionality then depends on whether it is connectable
            // or not.
            if (!isPeripheralRoleSupported)
            {
                return false;
            }

            //
            // Step 2
            // Initialize the IPSS
            //
            await InternetProtocolSupportService.InitAsync();
            if(InternetProtocolSupportService == null)
            {
                Debug.WriteLine("Error creating the Internet Protocol Support" +
                                " Service."
                                );
                return false;
            }

            //
            // Step 3
            // Initialize the packet processing service
            //
            await PacketProcessingService.InitAsync();
            if(PacketProcessingService == null)
            {
                Debug.WriteLine("Error creating the IPv6ToBle packet " +
                                "processing service."
                                );
                return false;
            }

            //
            // Step 4
            // Start advertising the services
            //
            InternetProtocolSupportService.Start(isPeripheralRoleSupported);
            PacketProcessingService.Start(isPeripheralRoleSupported);

            return true;
        }

        // Stops the GATT server and stops the services
        public void Stop()
        {
            // Stop the services
            InternetProtocolSupportService.Stop();
            PacketProcessingService.Stop();
        }

        #endregion

    }
}
