using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace IPv6ToBlePacketProcessingForDesktop
{   
    /// <summary>
    /// The service class. Overrides basic OnStart() and OnStop() functionality.
    /// </summary>
    public partial class IPv6ToBlePacketProcessing : ServiceBase
    {
        public IPv6ToBlePacketProcessing()
        {
            InitializeComponent();
        }

        // A worker thread and its worker object
        private Thread workerThread = null;
        private ThreadWorker worker = null;

        /// <summary>
        /// Defines processing that occurs when the service starts. OnStart()
        /// should return as quickly as possible, so we start a separate thread
        /// to perform our constant tasks here and return.
        /// </summary>
        /// <param name="args"></param>
        protected override void OnStart(string[] args)
        {
            // Spin up the worker thread
            worker = new ThreadWorker(this);
            workerThread = new Thread(worker.DoWork);
        }

        /// <summary>
        /// Specifies the actions to take when the service stops running.
        /// </summary>
        protected override void OnStop()
        {
            // Stop the worker object
            worker.RequestStop();

            // Try to stop the worker thread; if it doesn't stop after 16
            // seconds, abort it. The maximum time the worker thread spends
            // waiting for its own operations is 15 combined seconds.
            if (!workerThread.Join(TimeSpan.FromSeconds(16)))
            {
                workerThread.Abort();
            }
        }
    }
}
