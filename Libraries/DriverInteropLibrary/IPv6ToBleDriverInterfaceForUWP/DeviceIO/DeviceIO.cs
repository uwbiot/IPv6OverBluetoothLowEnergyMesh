using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Win32.SafeHandles;

using IPv6ToBleDriverInterfaceForUWP.AsyncResultObjects;

namespace IPv6ToBleDriverInterfaceForUWP.DeviceIO
{
    /// <summary>
    /// A wrapper class for the Win32 CreateFile() and DeviceIoControl()
    /// functions.
    /// </summary>
    public static class DeviceIO
    {
        /// <summary>
        /// Opens a handle to the driver.
        /// </summary>
        /// <returns></returns>
        public static SafeFileHandle OpenDevice(
            String deviceName,
            bool useAsync
        )
        {
            // Open the handle to the driver
            SafeFileHandle device = Kernel32Import.CreateFile(deviceName,
                Kernel32Import.GENERIC_READ | Kernel32Import.GENERIC_WRITE,
                Kernel32Import.FILE_SHARE_READ | Kernel32Import.FILE_SHARE_WRITE,
                IntPtr.Zero,
                Kernel32Import.OPEN_EXISTING,
                useAsync ? Kernel32Import.FILE_FLAG_OVERLAPPED : 0,
                IntPtr.Zero
            );

            if(device.IsInvalid)
            {
                throw new Win32Exception();
            }

            // Bind the handle to the thread pool if performing asynchronous
            // operation, for later completion
            if(useAsync)
            {
                ThreadPool.BindHandle(device);
            }

            return device;
        }

        /// <summary>
        /// Method to initiate a SYNCHRONOUS control command to the driver.
        /// 
        /// The device handle MUST have been opened without the async option
        /// set prior to calling this method.
        /// 
        /// This overload does not take any input and is used only in the
        /// list purge IOCTLs that have no input or output.
        /// </summary>
        /// <param name="device"></param>
        /// <param name="controlCode"></param>
        public unsafe static bool SynchronousControl(
            SafeFileHandle  device,
            int             controlCode
        )
        {
            uint bytesReturned = 0;  // don't care about this in synchronous I/O
            bool result = Kernel32Import.DeviceIoControl(device,
                                                         controlCode,
                                                         new byte[0],
                                                         0,
                                                         new byte[0],
                                                         0,
                                                         ref bytesReturned,
                                                         null
                                                         );
            // Close the driver handle
            Kernel32Import.CloseHandle(device);

            return result;
        }

        /// <summary>
        /// Method to initiate a SYNCHRONOUS control command to the driver.
        /// 
        /// The device handle MUST have been opened without the async option
        /// set prior to calling this method.
        /// 
        /// This overload takes a string input and is used for list operations.
        /// </summary>
        /// <param name="device"></param>
        /// <param name="controlCode"></param>
        public unsafe static bool SynchronousControl(
            SafeFileHandle  device,
            int             controlCode,
            String          ipv6Address
        )
        {
            uint bytesReturned = 0;  // don't care about this in synchronous I/O
            bool result = Kernel32Import.DeviceIoControl(device,
                                                         controlCode,
                                                         ipv6Address,
                                                         sizeof(char) * ipv6Address.Length,
                                                         null,
                                                         0,
                                                         ref bytesReturned,
                                                         null
                                                         );

            // Close the driver handle
            Kernel32Import.CloseHandle(device);

            return result;
        }

        /// <summary>
        /// Method to initiate a SYNCHRONOUS control command to the driver.
        /// 
        /// The device handle MUST have been opened without the async option
        /// set prior to calling this method.
        /// 
        /// This overload takes an int input and is used to query the driver 
        /// what the mesh role is for this device.
        /// 
        /// The main return value of this function is whether the
        /// DeviceIoControl operation succeeded or not; therefore, to determine
        /// if this device is a border router or not, the caller must verify
        /// both the result and the isBorderRouter parameter.
        /// </summary>
        /// <param name="device"></param>
        /// <param name="controlCode"></param>
        public unsafe static bool SynchronousControl(
            SafeFileHandle  device,
            int             controlCode,
            out bool        isBorderRouter
        )
        {
            uint meshRole = 0;
            uint bytesReturned = 0;  // don't care about this in synchronous I/O
            bool result = Kernel32Import.DeviceIoControl(device,
                                                         controlCode,
                                                         0,
                                                         sizeof(uint),
                                                         ref meshRole,
                                                         sizeof(uint),
                                                         ref bytesReturned,
                                                         null
                                                         );
            if(result)
            {
                // Record the answer from the driver
                if (meshRole == 1)
                {
                    isBorderRouter = true;
                }
                else
                {          
                    isBorderRouter = false;
                }
            }
            else
            {
                isBorderRouter = false;
            }
            

            // Close the driver handle
            Kernel32Import.CloseHandle(device);

            return result;
        }

        /// <summary>
        /// Method to initiate an ASYNCHRONOUS device I/O control operation to
        /// get a packet from the driver, whenever it may arrive.
        /// 
        /// The device handle MUST have been opened with the async option set
        /// to TRUE prior to calling this method.
        /// </summary>
        /// <returns></returns>
        public static IAsyncResult BeginGetPacketFromDriverAsync<TResult>(
            SafeFileHandle  device,
            int             controlCode,
            Int32           maxElements,
            AsyncCallback   asyncCallback,
            object          state
        )
        {
            // Error checking
            if(device == null)
            {
                throw new InvalidEnumArgumentException("Device handle was null");
            }

            if (maxElements > 1280)
            {
                return null;
            }
            // Construct the output buffer; that is, the packet. Shouldn't be
            // more than 1280 bytes or the driver will reject the request.            
            byte[] packet = new byte[maxElements];
            DeviceAsyncResult<byte[]> asyncResult = AsyncControl(device,
                                                                 controlCode,
                                                                 packet,
                                                                 asyncCallback,
                                                                 state
                                                                 );
            return asyncResult;
        }

        /// <summary>
        /// Ends asynchronous device I/O control operation to get a packet from
        /// the driver. 
        /// 
        /// The reciprocal of BeginGetPacketFromDriverAsync().
        /// </summary>
        /// <param name="result"></param>
        public static byte[] EndGetPacketFromDriverAsync<TResult>(
            IAsyncResult result
        )
        {
            DeviceAsyncResult<byte[]> asyncResult = (DeviceAsyncResult<byte[]>)result;
            byte[] packet = asyncResult.EndInvoke();
            return packet;
        }        

        /// <summary>
        /// Helper method to construct an asyncResult object and use it to
        /// call NativeControl(). Called by BeginAsyncControl().
        /// </summary>
        private static DeviceAsyncResult<T> AsyncControl<T>(
            SafeFileHandle  device,
            int             controlCode,
            T               outBuffer,
            AsyncCallback   asyncCallback,
            object          state
        )
        {

            SafePinnedObject outDeviceBuffer = null;
            if(outBuffer != null)
            {
                outDeviceBuffer = new SafePinnedObject(outBuffer);
            }

            // Create the async result object
            DeviceAsyncResult<T> asyncResult = new DeviceAsyncResult<T>(outDeviceBuffer,
                                                                        asyncCallback,
                                                                        state
                                                                        );
            unsafe
            {
                uint bytesReturned = 0;
                NativeAsyncControl(device,
                                   controlCode,
                                   outDeviceBuffer,
                                   ref bytesReturned,
                                   asyncResult.GetNativeOverlapped()
                                   );
            }

            return asyncResult;
        }

        /// <summary>
        /// Helper method that actually sends the given IOCTL to the driver by
        /// calling the native DeviceIOControl() function. Called by AsyncControl().
        /// </summary>
        private static unsafe void NativeAsyncControl(
            SafeFileHandle device,
            int controlCode,
            SafePinnedObject outBuffer,
            ref uint bytesReturned,
            NativeOverlapped* nativeOverlapped
        )
        {
            bool succeeded = Kernel32Import.DeviceIoControl(device,
                                                            controlCode,
                                                            null,
                                                            0,
                                                            outBuffer,
                                                            outBuffer.Size,
                                                            ref bytesReturned,
                                                            nativeOverlapped
                                                            );

            // If DeviceIoControl returns TRUE, the operation completed
            // synchronously
            if (succeeded)
            {
                throw new InvalidOperationException($"Async call to DeviceIoControl completed synchronously.");
            }

            // DeviceIoControl is operating asynchronously; test the returned
            // error code to see if it is pending or not
            Int32 error = Marshal.GetLastWin32Error();
            const Int32 cErrorIOPending = 997;  // system-defined code for pending I/O
            if (error == cErrorIOPending)
            {
                return;
            }

            // Throw an exception if DeviceIoControl fails altogether
            throw new InvalidOperationException($"Control failed with error {error}");
        }
    }
}
