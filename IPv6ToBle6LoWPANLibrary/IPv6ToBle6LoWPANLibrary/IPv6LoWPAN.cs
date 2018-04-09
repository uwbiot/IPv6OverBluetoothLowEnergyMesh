using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IPv6ToBle6LoWPANLibrary
{
    using Windows.Devices.Bluetooth;

    /// <summary>
    /// This class contains static methods for 6LoWPAN operations (IPv6 over
    /// Low-power Personal Area Networks).
    /// </summary>
    public class IPv6LoWPAN
    {
        /// <summary>
        /// This method takes the 128-bit UUID of a device, as specified by
        /// 3.10.3 of the Bluetooth Mesh v1.0 specification, and returns an
        /// IPv6 address derived from it.
        /// </summary>
        /// <param name="UUID"></param>
        /// <returns></returns>
        public static String CreateIPv6AddressFromUuid(
            Guid UUID
        )
        {

        }
    }
}
