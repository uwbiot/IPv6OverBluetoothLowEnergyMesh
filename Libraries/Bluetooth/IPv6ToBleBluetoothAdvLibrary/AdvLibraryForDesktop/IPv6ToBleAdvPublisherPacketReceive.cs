using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// UWP namespaces
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Storage.Streams;

namespace AdvLibraryForDesktop
{
    /// <summary>
    /// A class to register and publish Bluetooth advertisements.
    /// 
    /// Note that, for the IPv6 Over Bluetooth Low Energy Mesh project,
    /// this is only called from processes that are inherently always running
    /// in the background (headless). 
    /// 
    /// On a Windows Desktop device (a laptop or desktop), usually the border
    /// router, this is run from an always-running Windows Service. On Windows 
    /// 10 IoT Core, which could either be a router in the mesh or a node
    /// device, this is run from a background IoT UWP app, which are specially
    /// permitted to always run and auto-start compared to normal UWP apps.
    /// 
    /// Therefore, we can simply use the standard BLE Advertisement APIs 
    /// instead of having to go out of our way to create background task
    /// workers. In a normal foreground app, to run a publisher in the
    /// background would require registering a background task.
    /// 
    /// The behavior of the IPv6 Over Bluetooth Low Energy project is such that
    /// a server/recipient of a packet will publish advertisements that it is
    /// ready, and a client/sender will watch for those advertisements when it
    /// has a packet.
    /// </summary>
    public class IPv6ToBleAdvPublisherPacketReceive
    {
        //---------------------------------------------------------------------
        // Local variables
        //---------------------------------------------------------------------

        // The advertisement publisher itself
        private BluetoothLEAdvertisementPublisher publisher;

        //---------------------------------------------------------------------
        // Init and ShutDown
        //---------------------------------------------------------------------

        // Initializes the publisher and begins publishing advertisements
        public void Start()
        {
            //
            // Step 1
            // Create the publisher
            //
            publisher = new BluetoothLEAdvertisementPublisher();

            //
            // Step 2
            // Add a payload to the advertisement. It must be less than 20 
            // bytes or an exception will occur.
            //
            BluetoothLEManufacturerData manufacturerData = new BluetoothLEManufacturerData();

            // Add a manufacturer ID
            manufacturerData.CompanyId = 0xDEDE;    // Another reference to King DeDeDe

            // Add a string
            DataWriter writer = new DataWriter();
            writer.WriteString("IPv6ToBle");

            manufacturerData.Data = writer.DetachBuffer();

            // Add the manufacturer data to the publisher
            publisher.Advertisement.ManufacturerData.Add(manufacturerData);

            // Register a status changed handler in case something happens to
            // the publisher
            publisher.StatusChanged += OnPublisherStatusChanged;

            // Start the publisher
            publisher.Start();
        }

        // Stops publishing advertisements
        public void Stop()
        {
            // Stop the publisher
            publisher.Stop();

            // Unregister the event handler to prevent resource leaks
            publisher.StatusChanged -= OnPublisherStatusChanged;
        }

        //---------------------------------------------------------------------
        // Method to handle if/when the publisher's status changes
        //---------------------------------------------------------------------

        // Event callback for when the status of the publisher changes. This
        // helps keep the publisher going if it is aborted by the system.
        private void OnPublisherStatusChanged(
            BluetoothLEAdvertisementPublisher                       publisher,
            BluetoothLEAdvertisementPublisherStatusChangedEventArgs eventArgs
        )
        {
            if(eventArgs.Status == BluetoothLEAdvertisementPublisherStatus.Aborted)
            {
                publisher.Start();
            }
        }
    }
}
