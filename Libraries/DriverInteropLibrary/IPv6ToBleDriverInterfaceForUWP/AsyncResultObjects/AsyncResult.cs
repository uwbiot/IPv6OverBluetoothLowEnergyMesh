using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IPv6ToBleDriverInterfaceForUWP.AsyncResultObjects
{
    /// <summary>
    /// A derivation of the AsyncResultNoResult class that permits one to
    /// return a result of type TResult.
    /// </summary>
    /// <typeparam name="TResult"></typeparam>
    internal class AsyncResult<TResult> : AsyncResultNoResult
    {
        // The result itself
        private TResult mResult = default(TResult);

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="asyncCallback"></param>
        /// <param name="state"></param>
        public AsyncResult(
            AsyncCallback   asyncCallback,
            object          state
        ) : base(asyncCallback, state) { }

        public void SetAsCompleted(
            TResult result,
            bool    completedSynchronously
        )
        {
            // Save the asynchronous operation's result
            mResult = result;

            // Complete the operation with the base's implementation (not
            // setting an exception)
            base.SetAsCompleted(null, completedSynchronously);
        }

        new public TResult EndInvoke()
        {
            // Wait until the operation has completed
            base.EndInvoke();

            // Return the result if the preceding call did not throw
            return mResult;
        }
    }
}
