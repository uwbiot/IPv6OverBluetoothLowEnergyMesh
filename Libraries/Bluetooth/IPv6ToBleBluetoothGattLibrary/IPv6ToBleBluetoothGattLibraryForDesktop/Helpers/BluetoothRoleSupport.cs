using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// UWP namespaces
using Windows.Devices.Bluetooth;

namespace IPv6ToBleBluetoothGattLibraryForDesktop.Helpers
{
    /// <summary>
    /// Support class to check role capabilities on the local Bluetooth radio.
    /// Modeled after the CheckPeripheralSupportAsync() method in the
    /// BluetoothLE sample from Microsoft.
    /// </summary>
    public class BluetoothRoleSupport
    {
        // GAP central role
        public static async Task<bool> CheckCentralSupportAsync()
        {
            BluetoothAdapter localAdapter = await BluetoothAdapter.GetDefaultAsync();

            if (localAdapter != null)
            {
                return localAdapter.IsCentralRoleSupported;
            }
            else
            {
                return false;
            }
        }

        // GAP peripheral role
        public static async Task<bool> CheckPeripheralSupportAsync()
        {
            BluetoothAdapter localAdapter = await BluetoothAdapter.GetDefaultAsync();

            if (localAdapter != null)
            {
                return localAdapter.IsPeripheralRoleSupported;
            }
            else
            {
                return false;
            }
        }
    }
}
