using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// Helpers
using IPv6ToBleBluetoothGattLibraryForDesktop.Helpers;

// UWP namespaces from .NET Core
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Foundation;
using Windows.Storage.Streams;
using Windows.Security.Cryptography;

namespace IPv6ToBleBluetoothGattLibraryForDesktop.Characteristics
{
    /// <summary>
    /// Generic characteristic off of which our other GATT characteristics are
    /// derived.
    /// 
    /// This class is based off the GenericGattCharacteristic class in the
    /// GattServicesLibrary sample from Microsoft.
    /// </summary>
    public abstract class GenericGattCharacteristic : INotifyPropertyChanged
    {
        //---------------------------------------------------------------------
        // Requirements for property changed events
        //---------------------------------------------------------------------

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// The callback for invoking the property changed event handler.
        /// </summary>
        /// <param name="args">The property that changed.</param>
        protected void OnPropertyChanged(PropertyChangedEventArgs args)
        {
            PropertyChanged?.Invoke(this, args);
        }

        //---------------------------------------------------------------------
        // Objects and methods for the underlying GATT characteristic and
        // its parent service
        //---------------------------------------------------------------------

        // The GATT characteristic for which this class is a wrapper
        private GattLocalCharacteristic characteristic;

        // Getter and setter for the characteristic
        public GattLocalCharacteristic Characteristic
        {
            get
            {
                return characteristic;
            }

            set
            {
                if (characteristic != value)
                {
                    characteristic = value;
                    OnPropertyChanged(new PropertyChangedEventArgs("Characteristic"));
                }
            }
        }

        // Getter for this characteristic's parent service
        public GenericGattService ParentService
        {
            get;
            private set;
        }

        // The characteristic's buffer to hold a payload
        private IBuffer value;

        // Getter and setter for the characteristic's value
        public IBuffer Value
        {
            get
            {
                return value;
            }

            set
            {
                if(this.value != value)
                {
                    this.value = value;
                    OnPropertyChanged(new PropertyChangedEventArgs("Value"));
                }
            }
        }

        //---------------------------------------------------------------------
        // Constructor
        //---------------------------------------------------------------------

        public GenericGattCharacteristic(
            GattLocalCharacteristic     characteristic, 
            GenericGattService service
        )
        {
            Characteristic = characteristic;
            ParentService = service;

            if(Characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Read))
            {
                Characteristic.ReadRequested += Characteristic_ReadRequested;
            }

            if(Characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Write) ||
               Characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse))
            {
                Characteristic.WriteRequested += Characteristic_WriteRequested;
            }

            Characteristic.SubscribedClientsChanged += Characteristic_SubscribedClientsChanged;
        }

        //---------------------------------------------------------------------
        // Event callback for when number of subscribers to this characteristic
        // change
        //---------------------------------------------------------------------

        protected virtual void Characteristic_SubscribedClientsChanged(
            GattLocalCharacteristic sender,
            object args
        )
        {
            Debug.WriteLine("Subscribers: {0}", sender.SubscribedClients.Count());
        }

        //---------------------------------------------------------------------
        // Event callback to Notify or Indicate to clients
        //---------------------------------------------------------------------

        public virtual async void NotifyValue()
        {
            // Do nothing if parent service is not publishing
            if(!ParentService.IsPublishing)
            {
                return;
            }

            bool notify = Characteristic.CharacteristicProperties.HasFlag(
                            GattCharacteristicProperties.Notify
                          );
            bool indicate = Characteristic.CharacteristicProperties.HasFlag(
                                GattCharacteristicProperties.Indicate
                            );

            if(notify || indicate)
            {
                Debug.WriteLine($"NotifyValue executing: Notify = {notify}," +
                                $" Indicate = {indicate}"
                                );
                await Characteristic.NotifyValueAsync(Value);
            }
            else
            {
                Debug.WriteLine("Characteristic does not include Notify or" +
                                "Indicate; nothing to notify."
                                );
            }
        }

        //---------------------------------------------------------------------
        // Event callback for Read
        //---------------------------------------------------------------------

        protected virtual void Characteristic_ReadRequested(
            GattLocalCharacteristic sender,
            GattReadRequestedEventArgs args
        )
        {
            // Get an event deferral for async operations
            var deferral = args.GetDeferral();
            Characteristic_ReadRequested(sender, args, deferral);
        }

        // Helper function for async Read callback
        protected virtual async void Characteristic_ReadRequested(
            GattLocalCharacteristic sender,
            GattReadRequestedEventArgs args,
            Deferral deferral = null
        )
        {
            Debug.WriteLine($"base.Characteristic_ReadRequested Entry");

            // Get an event deferral for async operations if we don't have one
            if (deferral == null)
            {
                deferral = args.GetDeferral();
            }            

            //
            // Normally, GetRequestAsync() is recommended to run on a UI thread
            // because, even with paired devices, the device may prompt the
            // user for consent. But, we're running in a background service so
            // we won't run this on a UI thread. According to one of the devs
            // on the core Bluetooth team, because this is for a "test
            // application," consent prompts are not currently part of MS's
            // policy and it will be auto-accepted. 
            //
            var request = await args.GetRequestAsync();
            request.RespondWithValue(Value);
            Debug.WriteLine($"Characteristic ReadRequested- Length: " +
                            $"{request.Length}, State: {request.State}, " +
                            $"Offset: {request.Offset}"
                            );

            Debug.WriteLine("base.Characteristic_ReadRequested Exit");
        }

        //---------------------------------------------------------------------
        // Event callback for Write
        //---------------------------------------------------------------------

        protected virtual void Characteristic_WriteRequested(
            GattLocalCharacteristic sender,
            GattWriteRequestedEventArgs args
        )
        {
            Characteristic_WriteRequested(sender, args, args.GetDeferral());
        }

        // Helper function for async Write callback
        protected virtual async void Characteristic_WriteRequested(
            GattLocalCharacteristic sender,
            GattWriteRequestedEventArgs args,
            Deferral deferral
        )
        {
            Debug.WriteLine("base.Characteristic_WriteRequested Entry");

            GattWriteRequest request = null;

            // Get an event deferral for async operations if we don't have one
            if(deferral == null)
            {
                deferral = args.GetDeferral();
            }

            //
            // Normally, GetRequestAsync() is recommended to run on a UI thread
            // because, even with paired devices, the device may prompt the
            // user for consent. But, we're running in a background service so
            // we won't run this on a UI thread. According to one of the devs
            // on the core Bluetooth team, because this is for a "test
            // application," consent prompts are not currently part of MS's
            // policy and it will be auto-accepted. 
            //
            request = await args.GetRequestAsync();

            // Set the value
            Value = request.Value;

            // Respond with completion notification if requested
            if(request.Option == GattWriteOption.WriteWithResponse)
            {
                Debug.WriteLine("Completing write request with response.");
                request.Respond();
            }
            else
            {
                Debug.WriteLine("Completing write request without response.");
            }

            // Debugging print!
            byte[] data;
            CryptographicBuffer.CopyToByteArray(Value, out data);

            if(data == null)
            {
                Debug.WriteLine("Value after write completion was NULL. :(");
            }
            else
            {
                Debug.WriteLine($"Write completed; value is now {Utilities.BytesToString(data)}");
            }

            Debug.WriteLine("base.Characteristic_WriteRequested Exit");
        }
    }
}
