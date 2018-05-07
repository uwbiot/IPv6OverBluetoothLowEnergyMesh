using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using System.Threading.Tasks;
using System.Net;

using Windows.Networking;
using Windows.Networking.Connectivity;

using IPv6ToBleSixLowPanLibraryForUWP;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace IPv6AddressPrinterForIoTCore
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            this.InitializeComponent();
        }

        private async void generate_button_Click(object sender, RoutedEventArgs e)
        {
            // Generated address
            IPAddress address = await IPv6AddressFromBluetoothAddress.GenerateAsync(2);
            addressTextBox.Text = address.ToString();

            // True local address (only for border router)
            //addressTextBox.Text = LocalIPv6Address();
        }

        private string LocalIPv6Address()
        {
            string hostNameString = Dns.GetHostName();
            IPHostEntry ipEntry = Dns.GetHostEntry(hostNameString);
            IPAddress[] addresses = ipEntry.AddressList;
            if (addresses[0].AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                return addresses[0].ToString();
            }
            else
            {
                return string.Empty;
            }
        }
    }
}
