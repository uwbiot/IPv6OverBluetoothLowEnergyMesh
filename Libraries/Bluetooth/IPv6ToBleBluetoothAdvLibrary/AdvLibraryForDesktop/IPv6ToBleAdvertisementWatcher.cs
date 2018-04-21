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
    /// A class to watch for Bluetooth advertisements.
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
    /// workers.
    /// </summary>
    public class IPv6ToBleAdvertisementWatcher
    {
        //---------------------------------------------------------------------
        // Local variables
        //---------------------------------------------------------------------

        private BluetoothLEAdvertisementWatcher watcher;

        //---------------------------------------------------------------------
        // Constructor and destructor
        //---------------------------------------------------------------------

        // Constructs the watcher and begins watching
        public IPv6ToBleAdvertisementWatcher()
        {
            //
            // Step 1
            // Create the watcher
            //
            watcher = new BluetoothLEAdvertisementWatcher();

            //
            // Step 2
            // Configure the watcher to look only for advertisements from our
            // IPv6ToBle publisher. This is based on the assumption that all
            // devices in this network run Windows, and we don't want to
            // receive all advertisements around us.
            //
            // If you do want to work with non-Windows devices or listen to all
            // advertisements, comment out the rest of this step.
            //

            // Define the manufacturer data against we want to match
            BluetoothLEManufacturerData manufacturerData = new BluetoothLEManufacturerData();

            // Set the company ID for the manufacturer data to be the same as
            // we set in the IPv6ToBleAdvertisementPublisher class
            manufacturerData.CompanyId = 0xDEDE;

            // Set the payload to be the same payload string we specified in the
            // publisher
            DataWriter writer = new DataWriter();
            writer.WriteString("IPv6ToBle");

            manufacturerData.Data = writer.DetachBuffer();

            // Add the manufacturer data to the advertisement filter on the watcher
            watcher.AdvertisementFilter.Advertisement.ManufacturerData.Add(manufacturerData);

            // Adjust the signal strength filter. Basing this off the 
            // BluetotohAdvertisement sample from Microsoft: >= -70dBm is
            // "in range," < -75dBm is "out of range," out-of-range timeout
            // is 2 seconds.
            watcher.SignalStrengthFilter.InRangeThresholdInDBm = -70;
            watcher.SignalStrengthFilter.OutOfRangeThresholdInDBm = -75;
            watcher.SignalStrengthFilter.OutOfRangeTimeout = TimeSpan.FromMilliseconds(2000);

            //
            // Step 3
            // Hook up event handlers and start the watcher
            // 
            watcher.Received += OnAdvertisementReceived;
            watcher.Stopped += OnAdvertisementStopped;

            watcher.Start();
        }

        // Destroys the object and stops the watcher
        ~IPv6ToBleAdvertisementWatcher()
        {
            // Stop the watcher
            watcher.Stop();

            // Unhook event handlers to prevent resource leaks
            watcher.Received -= OnAdvertisementReceived;
            watcher.Stopped -= OnAdvertisementStoped;
        }

        //---------------------------------------------------------------------
        // Event handlers for advertisements
        //---------------------------------------------------------------------
    }
}
