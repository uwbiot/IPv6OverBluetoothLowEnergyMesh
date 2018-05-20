using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Win32.SafeHandles;

namespace IPv6ToBleDriverInterfaceForUWP.DeviceIO
{
    /// <summary>
    /// A wrapper class that pins a managed object in memory for the duration
    /// of asynchronous operations, so it doesn't get moved or otherwise
    /// affected by the garbage collector.
    /// 
    /// This class is based on the example provided by Jeffrey Richter in his
    /// June 2007 article, "Concurrent Affairs," in MSDN Magazine.
    /// </summary>
    internal sealed class SafePinnedObject : SafeHandleZeroOrMinusOneIsInvalid
    {
        // The handle of the pinned object, or 0
        private GCHandle managedGarbageCollectorHandle;

        // Constructor
        public SafePinnedObject(object obj) : base(true)
        {
            // Check for null object and do nothing if it is null
            if (obj == null)
            {
                return;
            }

            // Pin the buffer and save its memory address
            managedGarbageCollectorHandle = GCHandle.Alloc(obj, GCHandleType.Pinned);
            SetHandle(managedGarbageCollectorHandle.AddrOfPinnedObject());
        }

        /// <summary>
        /// Release the handle
        /// </summary>
        /// <returns></returns>
        protected override bool ReleaseHandle()
        {
            // Set the address to null for safety
            SetHandle(IntPtr.Zero);
            managedGarbageCollectorHandle.Free();
            return true;
        }

        /// <summary>
        /// Returns the object of a pinned buffer, or null if not specified
        /// </summary>
        public object Target
        {
            get
            {
                return IsInvalid ? null : managedGarbageCollectorHandle.Target;
            }
        }

        /// <summary>
        /// Gets the number of bytes in a pinned object, or 0 if not specified
        /// </summary>
        public Int32 Size
        {
            get
            {
                object target = Target;

                if (target == null)
                {
                    return 0;
                }

                // Return size of object if it is not an array
                if (!target.GetType().IsArray)
                {
                    return Marshal.SizeOf(target);
                }

                // Return the total size of the object if it is an array
                Array array = (Array)target;
                return array.Length * Marshal.SizeOf(array.GetType().GetElementType());
            }
        }
    }
}
