using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace IPv6ToBleBluetoothGattLibraryForDesktop.Helpers
{
    /// <summary>
    /// A custom exception class for failure to create a GATT service.
    /// 
    /// This class is based on the CreateServiceException class in the 
    /// GattServicesLibrary sample from Microsoft.
    /// </summary>
    public class CreateServiceException : Exception
    {
        public GattServiceProviderResult CreateServiceExceptionResult { get; }

        public CreateServiceException(GattServiceProviderResult createServiceExceptionResult) :
            base(string.Format($"An error occurred while creating the GATT" +
                $"service provider with this error code: " +
                $"{createServiceExceptionResult.Error}"))
        {
            CreateServiceExceptionResult = createServiceExceptionResult;
        }
    }
}
