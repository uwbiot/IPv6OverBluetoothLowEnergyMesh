using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;

namespace IPv6ToBlePacketProcessingForDesktop
{
    //
    // Namespaces for driver interaction
    //
    using IPv6ToBleDriverInterfaceForDesktop;   // Driver interop library
    using Microsoft.Win32.SafeHandles;          // Safe file handles
    using System.Threading;                 // Asynchronous I/O and thread pool
    using System.Runtime.InteropServices;   // Marhsalling interop calls

    //
    // Namespaces for Bluetooth
    //
    using Windows.Devices.Bluetooth.GenericAttributeProfile;
    using Windows.Devices.Enumeration;
    using Windows.Devices.Enumeration.Pnp;

    public partial class IPv6ToBlePacketProcessing : ServiceBase
    {
        public IPv6ToBlePacketProcessing()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Defines processing that occurs when the service starts. In other
        /// words, the behavior of the service.
        /// </summary>
        /// <param name="args"></param>
        protected override void OnStart(string[] args)
        {
            //
            // Step 1
            // Verify we can open a handle to the driver. Spin until we can
            // do so.
            //
            bool isDriverReachable = false;
            while(!isDriverReachable)
            {
                isDriverReachable = TryOpenHandleToDriver();
                if(!isDriverReachable)
                {
                    Debug.WriteLine("Could not open handle to the driver," +
                        "waiting 5 seconds before trying again.");
                    Thread.Sleep(5000);
                }
            }

            //
            // Step 2
            // Spin up the GATT server service to listen for later replies
            // over Bluetooth LE
            //

            //
            // Step 3
            // Send an initial batch of packet listening requests to the driver
            //


            // TODO: figure out where I need to code with the overlapped I/O
            // to take the received packet and continue on

            //
            // Step 
            // With a packet in hand, enumerate all nearby Bluetooth LE devices
            // with a device watcher
            //

            //
            // Step 
            // Stop the watcher
            //

            //
            // Step
            // Connect to the devices
            //

            //
            // Step
            // Enumerate supported services. Only proceed if the device has
            // both the IPSP service and our packet write GATT service
            //

            //
            // Step
            // Enumerate supported characteristics for the packet write GATT
            // service
            //
            
            //
            // Step
            // Write the packet to the characteristic on the remote device
            //

        }

        /// <summary>
        /// Specifies the actions to take when the service stops running.
        /// </summary>
        protected override void OnStop()
        {
            // Release resources allocated during OnStart()
        }

        /// <summary>
        /// Tries to open a handle to the driver. Does not actually do anything
        /// with the driver handle, just reports that the driver is there.
        /// </summary>
        /// <returns>True if successful, false otherwise.</returns>
        private bool TryOpenHandleToDriver()
        {
            bool driverOpened = true;

            SafeFileHandle driverHandle = IPv6ToBleDriverInterface.CreateFile(
                 "\\\\.\\IPv6ToBle",
                IPv6ToBleDriverInterface.GENERIC_READ | IPv6ToBleDriverInterface.GENERIC_WRITE,
                IPv6ToBleDriverInterface.FILE_SHARE_READ | IPv6ToBleDriverInterface.FILE_SHARE_WRITE,
                IntPtr.Zero,
                IPv6ToBleDriverInterface.OPEN_EXISTING,
                0,
                IntPtr.Zero
            );

            if(driverHandle.IsInvalid)
            {
                driverOpened = false;
            }
            else
            {
                IPv6ToBleDriverInterface.CloseHandle(driverHandle);
            }

            return driverOpened;
        }
    }
}
