using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IPv6ToBleBluetoothGattLibraryForDesktop.Helpers
{
    public class Utilities
    {
        /// <summary>
        /// Helper function to convert a byte array to its string form
        /// </summary>
        public static string BytesToString(byte[] array)
        {
            StringBuilder builder = new StringBuilder();

            for(int i = 0; i < array.Length; i++)
            {
                builder.Append($"{array[i]}");
                if(i < array.Length - 1)
                {
                    builder.Append(" ");
                }
            }

            return builder.ToString();
        }
    }
}
