using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using IPv6ToBleDriverInterfaceForDesktop.DeviceIO;

namespace IPv6ToBleDriverInterfaceForDesktop.AsyncResultObjects
{
    internal class DeviceAsyncResult<TResult> : AsyncResult<TResult>
    {
        #region Local variables

        // Input and output buffers for device I/O
        private SafePinnedObject mInDeviceBuffer;
        private SafePinnedObject mOutDeviceBuffer;

        #endregion

        /// <summary>
        /// Constructor
        /// </summary>
        public DeviceAsyncResult(
            SafePinnedObject    inDeviceBuffer,
            SafePinnedObject    outDeviceBuffer,
            AsyncCallback       asyncCallback,
            object              state
        ) : base(asyncCallback, state)
        {
            mInDeviceBuffer = inDeviceBuffer;
            mOutDeviceBuffer = outDeviceBuffer;
        }

        /// <summary>
        /// Creates and returns a NativeOverlapped structure to be passed to
        /// native code (i.e. the driver).
        /// </summary>
        /// <returns></returns>
        public unsafe NativeOverlapped* GetNativeOverlapped()
        {
            // Create a managed Overlapped structure that refers to this
            // IAsyncResult
            Overlapped overlapped = new Overlapped(0, 0, IntPtr.Zero, this);

            // Pack the managed Overlapped structure into a NativeOverlapped
            // structure, which pins the memory until the operation completes
            return overlapped.Pack(CompletionCallback,
                                   new object[] { mInDeviceBuffer.Target, mOutDeviceBuffer.Target });
        }

        /// <summary>
        /// The completion callback for when the operation completes.
        /// </summary>
        private unsafe void CompletionCallback(
            UInt32 errorCode,
            UInt32 numBytes,
            NativeOverlapped* nativeOverlapped
        )
        {
            // First, free the NativeOverlapped structure and let this
            // IAsyncResult be collectable by the garbage collector
            Overlapped.Free(nativeOverlapped);

            try
            {
                if(errorCode != 0)
                {
                    // Check if an error occurred and record the error if so
                    base.SetAsCompleted(new Win32Exception((Int32)errorCode), false);
                }
                else
                {
                    // No error occurred, so the output buffer must contain the
                    // result!
                    TResult result = (TResult)mOutDeviceBuffer.Target;

                    // The result is going to be an array of bytes, so we need
                    // to resize the array to the exact size so that the Length
                    // property is accurate
                    if((result != null) && result.GetType().IsArray)
                    {
                        // Only resize if the number of elements initialized in
                        // the array is less than the size of the array itself
                        Type elementType = result.GetType().GetElementType();
                        Int64 numElements = numBytes / Marshal.SizeOf(elementType);
                        Array originalArray = (Array)(object)result;
                        if(numElements < originalArray.Length)
                        {
                            // Create a new array whose size is the number of
                            // initialized elements
                            Array newArray = Array.CreateInstance(elementType,
                                                                  numElements
                                                                  );
                            // Copy the initialized elements from the original
                            // array to the new one
                            Array.Copy(originalArray, newArray, numElements);
                            result = (TResult)(object)newArray;
                        }
                    }

                    // Record the result and call the AsyncCallback method
                    // passed to the BeginGetArray method
                    base.SetAsCompleted(result, false);
                }
            }
            finally
            {
                // Make sure that the input and output buffers are unpinned
                mInDeviceBuffer.Dispose();
                mOutDeviceBuffer.Dispose();
                mInDeviceBuffer = mOutDeviceBuffer = null;  // Allow early GC
            }
        }
    }
}
