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
        public string ReachStatus { get; private set; }
        public bool? Reachable { get; private set; }

        public event Action<string> Log;
        public event Action Changed; // players, codes, router status

        public static string WorldPath(string worldName)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) worldName = worldName.Replace(c, '_');
            return Path.Combine(Settings.Folder, "worlds", worldName.Trim() + ".dat");
        }

        public System.Collections.Generic.List<PlayerInfo> Players =>
            _server?.Players ?? new System.Collections.Generic.List<PlayerInfo>();
        public int HostId => _server?.HostId ?? 0;
        public WorldState World => _server?.SnapshotWorld();

        // The saved world with this name, or null if it's new.
        public static WorldState TryLoadWorld(string worldName)
        {
            try
            {
                var path = WorldPath(worldName);
                return File.Exists(path) ? WorldState.LoadFromFile(path) : null;
            }
            catch { return null; }
        }

        public bool Kick(int playerId, bool ban) => _server != null && _server.Kick(playerId, ban);

        public void Start(string worldName, int port, string gameMode, string password = "")
        {
            if (Running) return;
            var server = new NetServer(WorldPath(worldName), gameMode) { Password = password ?? "" };
            StartLogFile();
            server.Log += m => { Log?.Invoke(m); WriteLogFile(m); };
            server.PlayersChanged += () => Changed?.Invoke();
            server.Start(port); // throws if the port is taken
            _server = server;
            SetPriority(true);
            Log?.Invoke("Server log is also saved to " + LogFilePath);
            Port = server.Port;

            var lan = LocalIPv4();
            LanCode = lan != null ? JoinCode.Encode(lan, Port) : null;
            InternetCode = null;
            RouterOk = false;
            RouterStatus = "Opening your router so friends on other wifi can join...";
            Changed?.Invoke();

            Reachable = null;
            ReachStatus = null;
            Task.Run(() =>
            {
                var r = Upnp.OpenPort(Port, "Subnautica Multiplayer");
                _routerOpened = r.Success && !r.BehindCgnat && !r.DoubleNat;
                var ip = r.PublicIp ?? (r.Success ? r.ExternalIp : null);
                if (ip != null && IPAddress.TryParse(ip, out var addr)) InternetCode = JoinCode.Encode(addr, Port);

                RouterOk = r.Success && !r.BehindCgnat && !r.DoubleNat;
                if (RouterOk)
                    RouterStatus = "Router opened automatically.";
                else if (r.BehindCgnat)
                    RouterStatus = "Your internet provider shares one IP between customers (CGNAT), so nobody can connect in from outside. " +
                                   "Fix: everyone installs Radmin VPN or Tailscale and joins with your VPN IP, or a friend hosts.";
                else if (r.DoubleNat)
                    RouterStatus = $"Your router opened the port, but there's a second box in front of it (your internet provider's modem, outside IP {r.ExternalIp}). " +
                                   $"Also forward TCP {Port} on that modem (or put it in bridge mode), or use Radmin VPN / Tailscale.";
                else
                    RouterStatus = $"Couldn't open your router automatically ({r.Error}). Forward TCP port {Port} to this PC by hand, or use Radmin VPN / Tailscale.";
                Log?.Invoke(RouterStatus);
                Log?.Invoke("Router details: " + r.Report());
                Changed?.Invoke();
                CheckReachable();
            });
        }

        bool _routerOpened;

        // Asks an outside website to connect to us: the real answer to "can friends join?"
        public void CheckReachable()
        {
            if (!Running) return;
            var port = Port;
            ReachStatus = "Checking if friends can reach you...";
            Reachable = null;
            Changed?.Invoke();
            Task.Run(() =>
            {
                var ok = Upnp.CheckReachable(port);
                if (ok != true) { System.Threading.Thread.Sleep(5000); ok = Upnp.CheckReachable(port) ?? ok; } // first try can be too early
                Reachable = ok;
                ReachStatus = ok == true ? "✔ Friends on other wifi CAN reach you. Send them the internet code!"
                    : ok == false && _routerOpened
                        ? "✖ Your router opened the port, but the internet still can't get in. Hit 'Allow through firewall' (it also removes old hidden " +
                          "block rules), check your antivirus firewall (Norton/Avast/Bitdefender...), then 'Test again'. Still closed? Your internet " +
                          "provider blocks incoming connections: use Radmin VPN or Tailscale."
                    : ok == false
                        ? "✖ Port is closed: your router didn't open it (see above). Turn on UPnP in the router or forward the port by hand, or use Radmin VPN / Tailscale."
                    : "Couldn't run the reachability test (website didn't answer). Just try joining with a friend.";
                Log?.Invoke(ReachStatus);
                Changed?.Invoke();
            });
        }

        public void Stop()
        {
            if (!Running) return;
            _server.Stop();
            _server = null;
            SetPriority(false);
            var port = Port;
            Task.Run(() => Upnp.ClosePort(port));
            InternetCode = LanCode = null;
            RouterStatus = null;
            Changed?.Invoke();
        }

        // The game on this same PC eats every core when it loads terrain; the server must still get its turn,
        // or everyone's updates stall while the host turns and swims.
        static void SetPriority(bool hosting)
        {
            try
            {
                using (var me = System.Diagnostics.Process.GetCurrentProcess())
                    me.PriorityClass = hosting ? System.Diagnostics.ProcessPriorityClass.AboveNormal : System.Diagnostics.ProcessPriorityClass.Normal;
            }
            catch { }
        }

        // The server log also goes to a file (server.log next to the launcher settings) so it can be sent along with
        // the game's LogOutput.log when something's wrong.
        public static string LogFilePath => Path.Combine(Settings.Folder, "server.log");
        readonly object _logLock = new object();

        void StartLogFile()
        {
            try
            {
                Directory.CreateDirectory(Settings.Folder);
                File.WriteAllText(LogFilePath, $"Subnautica Multiplayer server log, started {DateTime.Now}{Environment.NewLine}");
            }
            catch { }
        }

        void WriteLogFile(string line)
        {
            try { lock (_logLock) File.AppendAllText(LogFilePath, $"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}"); }
            catch { }
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
