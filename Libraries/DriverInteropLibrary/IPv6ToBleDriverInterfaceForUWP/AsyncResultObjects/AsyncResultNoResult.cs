using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IPv6ToBleDriverInterfaceForUWP.AsyncResultObjects
{
    /// <summary>
    /// This class represents the result of an asynchronous operation from
    /// the driver. The core premise of doing our own implementation of the
    /// Asynchronous Programming Model (APM) is that we have speical IOCTLs to
    /// send to the driver and we have the need to wait indefinitely for a
    /// packet to arrive.
    /// 
    /// The IAsyncResult interface looks like this:
    /// 
    /// public interface IAsyncResult {
    /// WaitHandle AsyncWaitHandle { get; } // For Wait-Until-Done technique
    /// Boolean IsCompleted { get; } // For Polling technique
    /// Object AsyncState { get; } // For Callback technique
    /// Boolean CompletedSynchronously { get; } // Almost never used
    /// }
    /// 
    /// The code in this class is based on two MSDN magazine articles by 
    /// Jeffrey Richter in March 2007 and June 2007.
    /// 
    /// </summary>
    internal class AsyncResultNoResult : IAsyncResult
    {
        #region Local variables

        // Fields set at construction that do not change when the operation is
        // pending
        private readonly AsyncCallback mAsyncCallback;

        // Fields set at construction that do change after the operation completes
        private const Int32 cStatePending = 0;
        private const Int32 cStateCompletedSynchronously = 1;
        private const Int32 cStateCompletedAsynchronously = 2;
        private Int32 mCompletedState = cStatePending;

        // Field that may or may not get set depending on usage
        private ManualResetEvent mAsyncWaitHandle;

        // Field set when operation completes

        private Exception mException;

        #endregion

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="asyncCallback"></param>
        /// <param name="state"></param>
        public AsyncResultNoResult(
            AsyncCallback   asyncCallback,
            object          state
        )
        {
            mAsyncCallback = asyncCallback;
            AsyncState = state;
        }

        #region Methods to handle completion

        public void SetAsCompleted(
            Exception exception,
            bool completedSynchronously
        )
        {
            // Set the exception to the passed-in one. If null, this means that
            // no error has occurred.
            mException = exception;

            // Set the completed state
            Int32 prevState = Interlocked.Exchange(ref mCompletedState,
                                                   completedSynchronously ? cStateCompletedSynchronously : cStateCompletedAsynchronously
                                                   );
            if(prevState != cStatePending)
            {
                throw new InvalidOperationException("You can only set a result once.");
            }

            // Set the event if it exists
            if(mAsyncWaitHandle != null)
            {
                mAsyncWaitHandle.Set();
            }

            // Call the callback method if it were set
            mAsyncCallback?.Invoke(this);
        }

        /// <summary>
        /// Ends invocation
        /// </summary>
        public void EndInvoke()
        {
            // If the operation isn't done, wait for it
            if(!IsCompleted)
            {
                mAsyncWaitHandle.WaitOne();
                mAsyncWaitHandle.Close();
                mAsyncWaitHandle = null;
            }

            // The operation is done, so throw an exception if it occurred
            if(mException != null)
            {
                throw mException;
            }
        }

        #endregion

        #region IAsyncResult implementation

        public object AsyncState { get; }

        public bool CompletedSynchronously
        {
            get
            {
                return Thread.VolatileRead(ref mCompletedState) == cStateCompletedSynchronously;
            }
        }

        public WaitHandle AsyncWaitHandle
        {
            get
            {
                if(mAsyncWaitHandle == null)
                {
                    bool done = IsCompleted;
                    ManualResetEvent manualResetEvent = new ManualResetEvent(done);
                    if(Interlocked.CompareExchange(ref mAsyncWaitHandle,
                                                   manualResetEvent,
                                                   null
                                                   ) != null)
                    {
                        // Another thread created this object's event; dispose
                        // of the event we just created
                        manualResetEvent.Close();
                    }
                    else
                    {
                        if(!done && IsCompleted)
                        {
                            // If the operation wasn't done when we created the
                            // event but it is now done, set the event
                            mAsyncWaitHandle.Set();
                        }
                    }
                }

                return mAsyncWaitHandle;
            }
        }

        public bool IsCompleted
        {
            get
            {
                return Thread.VolatileRead(ref mCompletedState) != cStatePending;
            }
        }

        #endregion
    }
}
