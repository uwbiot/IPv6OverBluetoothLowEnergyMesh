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
    /// </summary>
    public class IPv6ToBleAdvertisementPublisher
    {
        //---------------------------------------------------------------------
        // Local variables
        //---------------------------------------------------------------------

        // The advertisement publisher itself
        private BluetoothLEAdvertisementPublisher publisher;

        //---------------------------------------------------------------------
        // Constructor and destructor
        //---------------------------------------------------------------------

        // Constructs and begins publishing advertisements
        public IPv6ToBleAdvertisementPublisher()
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

        // Destroys the object and stops publishing advertisements
        ~IPv6ToBleAdvertisementPublisher()
        {
            // Stop the publisher
            publisher.Stop();

            // Unregister the event handler to prevent resource leaks
            publisher.StatusChanged -= OnPublisherStatusChanged;
        }

        //---------------------------------------------------------------------
        // Methods to handle if/when the publisher's status changes
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

        //---------------------------------------------------------------------
        // Stop and start routines, just in case a caller needs this? Of note
        // is that you normally also add and remove the event handler during
        // normal operations with Bluetooth Advertisements, such as when the
        // app is suspending or resuming. But, since we always run these from
        // a continuously-running, automatically-starting background service/
        // IoT app, we only set up and tear down the handlers when we create
        // or destroy the class instance. But we still provide these helpers
        // to start or stop the publisher for completeness, if the caller needs
        // them.
        //---------------------------------------------------------------------

        public void Start()
        {
            publisher.Start();
        }

        public void Stop()
        {
            publisher.Stop();
        }
    }
}
