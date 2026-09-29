using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using SubnauticaMP.Shared;

namespace SubnauticaMP.Launcher
{
    // A server running inside the launcher, plus the router port-forward and join codes.
    internal sealed class HostedServer
    {
        NetServer _server;
        public int Port { get; private set; }
        public bool Running => _server != null;
        public string InternetCode { get; private set; }
        public string LanCode { get; private set; }
        public string RouterStatus { get; private set; }
        public bool RouterOk { get; private set; }

        public event Action<string> Log;
        public event Action Changed; // players, codes, router status

        public static string WorldPath(string worldName)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) worldName = worldName.Replace(c, '_');
            return Path.Combine(Settings.Folder, "worlds", worldName.Trim() + ".dat");
        }

        public System.Collections.Generic.List<PlayerInfo> Players =>
            _server?.Players ?? new System.Collections.Generic.List<PlayerInfo>();

        public void Start(string worldName, int port)
        {
            if (Running) return;
            var server = new NetServer(WorldPath(worldName));
            server.Log += m => Log?.Invoke(m);
            server.PlayersChanged += () => Changed?.Invoke();
            server.Start(port); // throws if the port is taken
            _server = server;
            Port = server.Port;

            var lan = LocalIPv4();
            LanCode = lan != null ? JoinCode.Encode(lan, Port) : null;
            InternetCode = null;
            RouterOk = false;
            RouterStatus = "Opening your router so friends on other wifi can join...";
            Changed?.Invoke();

            Task.Run(() =>
            {
                var r = Upnp.OpenPort(Port, "Subnautica Multiplayer");
                var ip = r.Success && !string.IsNullOrEmpty(r.ExternalIp) && !r.BehindCgnat ? r.ExternalIp : Upnp.LookUpPublicIp();
                if (ip != null && IPAddress.TryParse(ip, out var addr)) InternetCode = JoinCode.Encode(addr, Port);

                if (r.Success && !r.BehindCgnat)
                {
                    RouterOk = true;
                    RouterStatus = "Router opened automatically. Friends anywhere can join with the internet code.";
                }
                else if (r.BehindCgnat)
                {
                    RouterStatus = "Your internet provider shares your IP with other people (CGNAT), so hosting from home " +
                                   "won't work over the internet. Fix: everyone installs Radmin VPN or Tailscale and uses your VPN IP, or a friend hosts.";
                }
                else
                {
                    RouterStatus = $"Couldn't open your router automatically ({r.Error}). Either turn on UPnP in your router settings, " +
                                   $"forward TCP port {Port} to this PC by hand, or use Radmin VPN / Tailscale.";
                }
                Log?.Invoke(RouterStatus);
                Changed?.Invoke();
            });
        }

        public void Stop()
        {
            if (!Running) return;
            _server.Stop();
            _server = null;
            var port = Port;
            Task.Run(() => Upnp.ClosePort(port));
            InternetCode = LanCode = null;
            RouterStatus = null;
            Changed?.Invoke();
        }

        static IPAddress LocalIPv4()
        {
            try
            {
                // the address of the network card that has the default route
                using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                s.Connect("8.8.8.8", 53);
                return ((IPEndPoint)s.LocalEndPoint).Address;
            }
            catch
            {
                return NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                    .Select(a => a.Address)
                    .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            }
        }
    }
}
