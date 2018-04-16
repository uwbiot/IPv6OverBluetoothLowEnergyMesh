using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Diagnostics;

// Helpers
using IPv6ToBleBluetoothGattLibraryForDesktop.Helpers;

// UWP namespaces from .NET Core
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace IPv6ToBleBluetoothGattLibraryForDesktop
{
    /// <summary>
    /// Generic service off of which our other GATT services are derived.
    /// 
    /// This class is based off the GenericGattService class in the
    /// GattServicesLibrary sample from Microsoft.
    /// </summary>
    public abstract class GenericGattService : INotifyPropertyChanged
    {
        //---------------------------------------------------------------------
        // Requirements for property changed events
        //---------------------------------------------------------------------

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(PropertyChangedEventArgs args)
        {
            PropertyChanged?.Invoke(this, args);
        }        

        //---------------------------------------------------------------------
        // Name and publishing status variables
        //---------------------------------------------------------------------

        // Getter for name
        public abstract string Name
        {
            get;
        }

        // Boolean value for whether this service is publishing or not
        private bool isPublishing = false;

        // Getter and setter for isPublishing
        public bool IsPublishing
        {
            get
            {
                return isPublishing;
            }

            private set
            {
                if(value != isPublishing)
                {
                    isPublishing = value;
                    OnPropertyChanged(new PropertyChangedEventArgs("IsPublishing"));
                }
            }
        }

        //---------------------------------------------------------------------
        // Internal GATT service provider around which this class wraps
        //---------------------------------------------------------------------

        private GattServiceProvider serviceProvider;

        // Getter and setter for the GATT service provider
        public GattServiceProvider ServiceProvider
        {
            get
            {
                return serviceProvider;
            }

            protected set
            {
                if(serviceProvider != value)
                {
                    serviceProvider = value;
                    OnPropertyChanged(new PropertyChangedEventArgs("ServiceProvider"));
                }
            }
        }

        //---------------------------------------------------------------------
        // Method to initialize this class
        //---------------------------------------------------------------------

        public abstract Task Init();

        //---------------------------------------------------------------------
        // Method to create the GATT service provider
        //---------------------------------------------------------------------

        // <param name="uuid"> The UUID of the service to create </param>
        protected async Task CreateServiceProvider(Guid uuid)
        {
            GattServiceProviderResult creationResult = await GattServiceProvider.CreateAsync(uuid);

            if(creationResult.Error != BluetoothError.Success)
            {
                throw new CreateServiceException(creationResult);
            }

            ServiceProvider = creationResult.ServiceProvider;
        }

        //---------------------------------------------------------------------
        // Methods to start and stop the service
        //---------------------------------------------------------------------

        // Starts the service.
        // <param name="connectable"> 
        //      Starts the service as Connectable if set to true, starts the
        //      service as only Discoverable if set to false.
        // </param>
        public virtual void Start(bool connectable)
        {
            //
            // Setting IsDiscoverable to true ensures that remote devices can
            // query support for this service from the local device.
            // IsConnectable is for the Peripheral role and advertises whether
            // this service is connectable, and best-effort populates the 
            // advertising packet with the service ID.
            //
            GattServiceProviderAdvertisingParameters advertisingParams = new GattServiceProviderAdvertisingParameters
            {
                IsDiscoverable = true,
                IsConnectable = connectable
            };

            // Start advertising
            try
            {
                ServiceProvider.StartAdvertising(advertisingParams);
                IsPublishing = true;
                OnPropertyChanged(new PropertyChangedEventArgs("IsPublishing"));
            }
            catch (Exception)
            {
                Debug.WriteLine($"An error occurred while starting advertising" +
                    $"for service {ServiceProvider.Service.Uuid}");
                IsPublishing = false;
                throw;
            }
        }

        // Stops the service if it is already running
        public virtual void Stop()
        {
            try
            {
                ServiceProvider.StopAdvertising();
                IsPublishing = false;
                OnPropertyChanged(new PropertyChangedEventArgs("IsPublishing"));
            }
            catch (Exception)
            {
                Debug.WriteLine($"An error occurred while stopping" +
                    $"advertising for service {ServiceProvider.Service.Uuid}");
                IsPublishing = true;
                throw;
            }
        }
    }
}
