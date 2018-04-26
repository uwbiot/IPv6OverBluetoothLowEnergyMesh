using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.ComponentModel;

//
// Namespaces for driver interaction
//
using IPv6ToBleDriverInterfaceForDesktop;   // Driver interop library
using Microsoft.Win32.SafeHandles;          // Safe file handles
using System.Threading;                 // Asynchronous I/O and thread pool
using System.Runtime.InteropServices;   // Marhsalling interop calls

//
// Namespaces for Bluetooth
//
using IPv6ToBleBluetoothGattLibraryForDesktop;
using IPv6ToBleAdvLibraryForDesktop;
using System.IO;

namespace IPv6ToBlePacketProcessingForDesktop
{
    /// <summary>
    /// The thread worker that handles the main logic for packet processing.
    /// This class is instantiated and stopped by the service's OnStart() and
    /// OnStop() methods respectively.
    /// </summary>
    public class ThreadWorker
    {
        #region Local variables
        //---------------------------------------------------------------------
        // Local variables
        //---------------------------------------------------------------------

        // The parent service for this worker thread
        private IPv6ToBlePacketProcessing parentService;

        // Getter and Setter for the parent service
        private IPv6ToBlePacketProcessing ParentService
        {
            get
            {
                return parentService;
            }

            set
            {
                if(parentService != value)
                {
                    parentService = value;
                }
            }
        }

        // A boolean to signal the worker thread to stop, if we're shutting
        // down or otherwise stopping the service
        private volatile bool shouldStop;

        #endregion

        #region Constructor
        //---------------------------------------------------------------------
        // Constructor
        //---------------------------------------------------------------------

        public ThreadWorker(
            IPv6ToBlePacketProcessing parentService
        )
        {
            ParentService = parentService;
            shouldStop = false;
        }
        #endregion

        #region Main business logic
        //---------------------------------------------------------------------
        // Main business logic for doing work and stopping
        //---------------------------------------------------------------------


        // Method that is called when the thread is started. Prepares for 
        // Driver and Bluetooth operations, then spins if it has nothing to do
        // (i.e. we're waiting for an incoming packet).
        public void DoWork()
        {
            //
            // Step 1
            // Verify we can open a handle to the driver. If we can't, we fail
            // starting the service.
            //
            bool isDriverReachable = TryOpenHandleToDriver();
            if (!isDriverReachable)
            {
                Debug.WriteLine("Could not open a handle to the driver.");
                ParentService.ExitCode = -1;
                ParentService.Stop();
                throw new FileNotFoundException();
            }

            //
            // Step 2
            // Spin up the GATT server service to listen for later replies
            // over Bluetooth LE
            //
            IPv6ToBleGattServer gattServer = new IPv6ToBleGattServer();
            bool gattServerStarted = Task.Run(gattServer.StartAsync).Result;
            if (!gattServerStarted)
            {
                Debug.WriteLine("Could not start the GATT server.");
                ParentService.ExitCode = -1;
                ParentService.Stop();
                throw new Exception();
            }

            //
            // Step 3
            // Send an initial batch of packet listening requests to the driver
            //
            for (int i = 0; i < 10; i++)
            {
                SendListenRequestToDriver();

                // Wait 1 millisecond to give the driver a chance to pend the
                // request before sending it another one
                Thread.Sleep(1);
            }

            //
            // Step 4
            // Load the static routing table
            //


            //
            // Step 5
            // Set up a Bluetooth advertiser to let neighbors know we can
            // receive a packet if need be
            //

            
        }

        // Method to signal the thread to stop
        public void RequestStop()
        {
            shouldStop = true;
        }
        #endregion

        #region Driver interaction

        /// <summary>
        /// Verifies that the driver is present. Does not do anything else.
        /// </summary>
        /// <returns></returns>
        private bool TryOpenHandleToDriver()
        {
            bool isDriverReachable = true;

            SafeFileHandle driverHandle = IPv6ToBleDriverInterface.CreateFile(
                "\\\\.\\IPv6ToBle",
                IPv6ToBleDriverInterface.GENERIC_READ | IPv6ToBleDriverInterface.GENERIC_WRITE,
                IPv6ToBleDriverInterface.FILE_SHARE_READ | IPv6ToBleDriverInterface.FILE_SHARE_WRITE,
                IntPtr.Zero,
                IPv6ToBleDriverInterface.OPEN_EXISTING,
                0,
                IntPtr.Zero
            );

            if (driverHandle.IsInvalid)
            {
                int code = Marshal.GetLastWin32Error();

                Debug.WriteLine("Could not open a handle to the driver, " +
                                    "error code: " + code.ToString()
                                    );
                isDriverReachable = false;
            }

            return isDriverReachable;
        }

        /// <summary>
        /// Sends the IOCTL_IPV6_TO_BLE_LISTEN_NETWORK_V6 control code to the
        /// driver to ask the driver to listen for a packet.
        /// </summary>
        private unsafe void SendListenRequestToDriver()
        {
            //
            // Step 1
            // Open the handle to the driver with Overlapped async I/O flagged
            //
            SafeFileHandle driverHandle = IPv6ToBleDriverInterface.CreateFile(
                "\\\\.\\IPv6ToBle",
                IPv6ToBleDriverInterface.GENERIC_READ | IPv6ToBleDriverInterface.GENERIC_WRITE,
                IPv6ToBleDriverInterface.FILE_SHARE_READ | IPv6ToBleDriverInterface.FILE_SHARE_WRITE,
                IntPtr.Zero,
                IPv6ToBleDriverInterface.OPEN_EXISTING,
                IPv6ToBleDriverInterface.FILE_FLAG_OVERLAPPED,  // async
                IntPtr.Zero
            );

            if (driverHandle.IsInvalid)
            {
                int code = Marshal.GetLastWin32Error();

                Debug.WriteLine("Could not open a handle to the driver, " +
                                    "error code: " + code.ToString()
                                    );
                return;
            }

            //
            // Step 2
            // Prepare for asynchronous I/O
            //

            // Bind the driver handle to the Windows Thread Pool. Really, we're
            // binding the handle to an I/O completion port owned by the thread
            // pool.
            bool handleBound = ThreadPool.BindHandle(driverHandle);
            if (!handleBound)
            {
                Debug.WriteLine("Could not bind driver handle to a thread " +
                    "pool I/O completion port.\n");
            }

            // Set up the byte array to hold a packet (always use 1280 bytes
            // to make sure we can receive the maximum Bluetooth packet size)
            byte[] packet = new byte[1280];
            int bytesReceived = 0;

            // Create a NativeOverlapped pointer to pass to DeviceIoControl().
            // The Pack() method packs the current managed Overlapped structure
            // into a native one, specifies a delegate callback for when the 
            // asynchronous operation completes, and specifies a managed object
            // (the packet) that serves as a the buffer.
            Overlapped overlapped = new Overlapped();
            NativeOverlapped* nativeOverlapped = overlapped.Pack(IPv6ToBleListenCompletionCallback,
                                                                 packet
                                                                 );

            //
            // Step 3
            // Send the IOCTL to request the driver to listen for a packet and
            // give us the packet through the request's output buffer
            //
            // Note about asynchronous DeviceIoControl: when using overlapped
            // I/O, the call immediately returns with a boolean result. If
            // the result is true, the operation completed synchronously. If
            // false, it may be executing asynchronously. The error code is
            // ERROR_IO_PENDING for async I/O; otherwise it's a failure code.
            //
            // If DeviceIoControl() executes synchronously for some reason or
            // fails to execute asynchronously, the NativeOverlapped pointer
            // must still be freed.
            //            

            // System-defined ERROR_IO_PENDING == 997
            const int ErrorIoPending = 997;

            // Send the IOCTL
            bool listenResult = IPv6ToBleDriverInterface.DeviceIoControl(
                                    driverHandle,
                                    IPv6ToBleDriverInterface.IOCTL_IPV6_TO_BLE_LISTEN_NETWORK_V6,
                                    null,
                                    0,
                                    packet,
                                    (sizeof(byte) * 1280),
                                    out bytesReceived,
                                    nativeOverlapped
                                    );
            if (listenResult)
            {
                // Operation completed synchronously for some reason
                Debug.WriteLine("DeviceIoControl executed synchronously " +
                    "despite overlapped I/O flag.\n");
                Overlapped.Unpack(nativeOverlapped);
                Overlapped.Free(nativeOverlapped);
            }
            else
            {
                int error = Marshal.GetLastWin32Error();

                if (error != ErrorIoPending)
                {
                    // Failed to execute DeviceIoControl with overlapped I/O
                    Debug.WriteLine("Failed to execute " +
                        "DeviceIoControl asynchronously with error code" +
                        error + "\n");
                    Overlapped.Unpack(nativeOverlapped);
                    Overlapped.Free(nativeOverlapped);
                }
            }

            // Close the driver handle
            IPv6ToBleDriverInterface.CloseHandle(driverHandle);
        }

        /// <summary>
        /// Delegate callback function for handling asynchronous I/O
        /// completion events.
        /// 
        /// The main job of this function is to get the packet that we have
        /// now received and sent it out over Bluetooth Low Energy.
        /// 
        /// This function also cleans up the NativeOverlapped structure by 
        /// unpacking and freeing it.
        /// 
        /// For more info on this function, see
        /// https://msdn.microsoft.com/library/system.threading.iocompletioncallback
        /// </summary>
        private unsafe void IPv6ToBleListenCompletionCallback(
            uint errorCode,
            uint numBytes,
            NativeOverlapped* nativeOverlapped
        )
        {
            //
            // Step 1
            // Verify the operation succeeded, catch an exception if it did
            // not, and finally free the NativeOverlapped structure
            //
            try
            {
                if(errorCode != 0 || numBytes == 0)
                {
                    
                    throw new Win32Exception((int)errorCode);
                }
                else
                {
                    Overlapped.Unpack(nativeOverlapped);
                }

                // Now that we have the packet in hand, let's send it out
                // over Bluetooth

            } catch (Exception e)
            {
                Debug.WriteLine("Receiving a packet from the driver" +
                                " failed with this error: " + e.Message
                                );
            }
            finally
            {
                Overlapped.Free(nativeOverlapped);
            }

            //
            // Step 2
            // Send another request to the driver to replace this one
            //
            SendListenRequestToDriver();
        }

        #endregion

        #region Bluetooth interaction



        #endregion
    }
}
