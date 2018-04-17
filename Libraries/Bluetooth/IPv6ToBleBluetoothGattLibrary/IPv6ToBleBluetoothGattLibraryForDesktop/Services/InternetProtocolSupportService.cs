using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using IPv6ToBleBluetoothGattLibraryForDesktop.Helpers;

namespace IPv6ToBleBluetoothGattLibraryForDesktop.Services
{
    /// <summary>
    /// Implementation of the Internet Protocol Support Service. This is run
    /// on a GATT server and indicates support for IPv6 data transfer. It is
    /// a very simple service with no characteristics.
    /// 
    /// A GATT server caller simply calls Init(), then uses Start() and Stop()
    /// defined in the base class to control publishing the service.
    /// </summary>
    public class InternetProtocolSupportService : GenericGattService
    {
        //---------------------------------------------------------------------
        // Name of the service
        //---------------------------------------------------------------------

        // The name of the service
        public override string Name
        {
            get
            {
                return "Internet Protocol Support Service";
            }
        }

        //---------------------------------------------------------------------
        // Asynchronous initialization
        //---------------------------------------------------------------------

        public override async Task Init()
        {
            //
            // Create the long UUID for the IPSP by combining its short form
            // (0x1820) with the SIG-defined base UUID:
            //
            // 00000000-0000-1000-8000-00805F9B34FB. 
            //
            // The 16-bit form replaces the second four zeroes like this:
            //
            // 0000xxxx-0000-1000-8000-00805F9B34FB, where the x's are replaced
            // by the short ID.
            //
            await CreateServiceProvider(Guid.Parse("00001820-0000-1000-8000-00805F9B34FB"));

            //
            // That's it - this service has no characteristics
            //
        }
    }
}
