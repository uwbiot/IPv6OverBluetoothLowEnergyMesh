using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.ComponentModel;

using System.Runtime.InteropServices;

using Microsoft.Win32.SafeHandles;

using IPv6ToBleInteropLibrary;


namespace PacketProcessingTestUWP
{
    public class DriverThreadWorker
    {
        //---------------------------------------------------------------------
        // Local variables
        //---------------------------------------------------------------------

        // The parent app page that spawned this thread
        private MainPage parentPage;

        // Boolean to track whether the thread should stop
        private bool shouldStop;

        // String to record errors and report them to the parent page (which
        // listens for an event signaling that this has changed)
        private string driverThreadError;

        public event PropertyChangedEventHandler DriverThreadErrorChanged;

        public void OnDriverThreadErrorChanged(PropertyChangedEventArgs args)
        {
            DriverThreadErrorChanged?.Invoke(this, args);
        }

        public string DriverThreadError
        {
            get
            {
                return driverThreadError;
            }
            private set
            {
                lock(this)
                {
                    if(driverThreadError != value)
                    {
                        driverThreadError = value;
                    }
                    OnDriverThreadErrorChanged(new PropertyChangedEventArgs("DriverThreadError"));
                }
            }
        }

        //---------------------------------------------------------------------
        // Constructor
        //---------------------------------------------------------------------

        public DriverThreadWorker(MainPage page)
        {
            parentPage = page;
            shouldStop = false;
        }

        //---------------------------------------------------------------------
        // Main logic and stop function
        //---------------------------------------------------------------------

        /// <summary>
        /// Main loop for this thread to run.
        /// </summary>
        public void DoWork()
        {
            // Indefinitely run in a cycle of sending listening requests to the
            // driver, waiting for a packet, receiving a packet from the driver,
            //sending a received packet over Bluetooth LE, etc.
            while (!shouldStop)
            {
                SendListenRequestToDriverAndSendReceivedPacketAsync();
            }
        }

        /// <summary>
        /// Function to request this thread to stop
        /// </summary>
        public void RequestStop()
        {
            shouldStop = true;
        }

        //---------------------------------------------------------------------
        // Driver operations
        //---------------------------------------------------------------------

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

                //Debug.WriteLine("Could not open a handle to the driver, " +
                //                "error code: " + code.ToString());
                DriverThreadError = "Could not open a handle to the driver, " +
                                    "error code: " + code.ToString();
                isDriverReachable = false;
            }

            return isDriverReachable;
        }

        /// <summary>
        /// Sends the IOCTL_IPV6_TO_BLE_LISTEN_NETWORK_V6 control code to the
        /// driver to ask the driver to listen for a packet.
        /// </summary>
        private unsafe void SendListenRequestToDriverAndSendReceivedPacketAsync()
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

                //Debug.WriteLine("Could not open a handle to the driver, " +
                //                "error code: " + code.ToString());
                DriverThreadError = "Could not open a handle to the driver, " +
                                    "error code: " + code.ToString();
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
                //Debug.WriteLine("Could not bind driver handle to a thread " +
                //                "pool I/O completion port.");
                DriverThreadError = "Could not bind driver handle to a thread " +
                                    "pool I/O completion port.";
                goto Exit;
            }

            // Set up the byte array to hold a packet (always use 1280 bytes
            // to make sure we can receive the maximum Bluetooth packet size)
            byte[] packet = new byte[1280];
            int bytesReceived = 0;

            // Create a NativeOverlapped pointer to pass to DeviceIoControl().
            // The Pack() method packs the current managed Overlapped structure
            // into a native one, specifies a delegate callback for when the 
            // asynchronous operation completes, and specifies a managed object
            // (the packet) that serves as the buffer.
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
                //Debug.WriteLine("DeviceIoControl executed synchronously " +
                //                "despite overlapped I/O flag.");
                DriverThreadError = "DeviceIoControl executed synchronously " +
                                    "despire overlapped I/O flag.";
                Overlapped.Unpack(nativeOverlapped);
                Overlapped.Free(nativeOverlapped);
                goto Exit;
            }
            else
            {
                int error = Marshal.GetLastWin32Error();

                if (error != ErrorIoPending)
                {
                    // Failed to execute DeviceIoControl with overlapped I/O
                    //Debug.WriteLine("Failed to execute " +
                    //    "DeviceIoControl asynchronously with error code" +
                    //    error + "\n");
                    DriverThreadError = "Failed to execute DeviceIoControl " +
                                        "asynchronously with error code: " +
                                        error.ToString();
                    Overlapped.Unpack(nativeOverlapped);
                    Overlapped.Free(nativeOverlapped);
                    goto Exit;
                }
                else
                {
                    // Wait for a packet to arrive. We do this by trying to get
                    // the overlapped result for 10 seconds, then aborting this
                    // attempt if it fails. This is so we can return control
                    // to the while loop in the DoWork() function, giving
                    // the thread a chance to see if it has been requested to
                    // stop.

                    listenResult = IPv6ToBleDriverInterface.GetOverlappedResultEx(
                                       driverHandle,
                                       nativeOverlapped,
                                       out bytesReceived,
                                       10000,  // 10 seconds
                                       false   // don't block forever
                                   );

                    // We have a packet. Send it out over Bluetooth. The very
                    // act of receiving a packet from the driver MUST mean that
                    // the packet was not meant for this device.
                    if (listenResult)
                    {
                        IPAddress destinationAddress = parentPage.GetDestinationAddressFromPacket(packet);
                        parentPage.SendPacketOverBluetoothLE(packet, destinationAddress);
                    }
                    else
                    {
                        // We waited 10 seconds for a packet from the driver
                        // and got nothing, so log the error, free the 
                        // NativeOverlapped structure, and continue
                        error = Marshal.GetLastWin32Error();
                        //Debug.WriteLine("Did not receive a packet in the time" +
                        //                " interval. Trying again. Error code: " +
                        //                error.ToString());
                        DriverThreadError = "Did not receive a packet in the " +
                                            "time interval, error code: " +
                                            error.ToString();
                        //Overlapped.Unpack(nativeOverlapped);
                        //Overlapped.Free(nativeOverlapped);
                    }
                }
            }

            Exit:
            // Close the driver handle, implicitly canceling outstanding I/O
            IPv6ToBleDriverInterface.CloseHandle(driverHandle);
        }

        /// <summary>
        /// Delegate callback function for handling asynchronous I/O
        /// completion events.
        /// 
        /// This function cleans up the NativeOverlapped structure by 
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
            // Verify the operation succeeded, catch an exception if it did
            // not, and finally free the NativeOverlapped structure
            try
            {
                if (errorCode != 0)
                {

                    throw new Win32Exception((int)errorCode);
                }
                else
                {
                    Overlapped.Unpack(nativeOverlapped);
                }
            }
            catch (Exception e)
            {
                //Debug.WriteLine("Asynchronous Overlapped I/O failed with this" +
                //                " error: " + e.Message);
                DriverThreadError = "Asynchronous overlapped I/O failed with " +
                                    "this error code: " + e.Message;
            }
            finally
            {
                Overlapped.Free(nativeOverlapped);
            }
        }
    }
}
