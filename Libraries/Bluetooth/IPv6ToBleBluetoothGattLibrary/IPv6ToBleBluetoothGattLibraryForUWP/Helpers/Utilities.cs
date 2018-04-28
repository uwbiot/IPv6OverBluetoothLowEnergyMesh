using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IPv6ToBleBluetoothGattLibraryForUWP.Helpers
{
    /// <summary>
    /// Utility functions for generic byte array operations, etc.
    /// </summary>
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
                builder.Append($"{array[i]:X2}");
                if(i < array.Length - 1)
                {
                    builder.Append(" ");
                }
            }

            return builder.ToString();
        }        

        /// <summary>
        /// Compares two byte arrays, i.e. packets, for equality
        /// </summary>
        /// <param name="packet1">The first packet</param>
        /// <param name="packet2">The second packet</param>
        /// <returns></returns>
        public static bool PacketsEqual(byte[] packet1, byte[] packet2)
        {
            int length = packet1.Length;

            if(length != packet2.Length)
            {
                return false;
            }

            for(int i = 0; i < length; i++)
            {
                if(packet1[i] != packet2[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
