using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;

namespace IPv6ToBlePacketProcessingForDesktop
{
    public partial class IPv6ToBlePacketProcessing : ServiceBase
    {
        public IPv6ToBlePacketProcessing()
        {
            InitializeComponent();
        }

        protected override void OnStart(string[] args)
        {
        }

        protected override void OnStop()
        {
        }

        private void process1_Exited(object sender, EventArgs e)
        {

        }
    }
}
