using System;
using System.IO;
using System.Net;
using System.Threading;
using SubnauticaMP.Shared;

// Dedicated server: `SubnauticaMP.Server [port] [world file]`
// Commands: players, save, quit
int port = args.Length > 0 && int.TryParse(args[0], out var p) ? p : Protocol.DefaultPort;
string worldFile = args.Length > 1 ? args[1] : Path.Combine(AppContext.BaseDirectory, "world.dat");

void Log(string msg) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {msg}");

var server = new NetServer(worldFile);
server.Log += Log;
server.Start(port);
Log("World file: " + worldFile);

new Thread(() =>
{
    var r = Upnp.OpenPort(server.Port, "Subnautica Multiplayer server");
    var ip = r.PublicIp ?? (r.Success ? r.ExternalIp : null);
    if (r.Success && !r.BehindCgnat && !r.DoubleNat) Log("Router port opened automatically.");
    else if (r.BehindCgnat) Log("ISP uses CGNAT: people outside your network can't reach this server directly. Use a VPN like Tailscale.");
    else if (r.DoubleNat) Log($"Router opened the port, but an ISP modem ({r.ExternalIp}) is in front of it. Forward TCP {server.Port} there too.");
    else Log($"Couldn't open router port automatically ({r.Error}). Forward TCP {server.Port} by hand if friends can't join.");
    Log("Router details: " + r.Report());
    var reach = Upnp.CheckReachable(server.Port);
    Log(reach == true ? "Reachable from the internet." : reach == false ? "NOT reachable from the internet (check firewall / port forward)." : "Couldn't test reachability.");
    if (ip != null && IPAddress.TryParse(ip, out var addr)) Log($"Join code: {JoinCode.Encode(addr, server.Port)}  (or {ip}:{server.Port})");
}) { IsBackground = true }.Start();

var stop = new ManualResetEventSlim();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Set(); };

new Thread(() =>
{
    string line;
    while ((line = Console.ReadLine()) != null)
    {
        switch (line.Trim())
        {
            case "quit": stop.Set(); return;
            case "save": server.SaveWorld(); Log("Saved."); break;
            case "players":
                var list = server.Players;
                Log($"{list.Count} online: " + string.Join(", ", list.ConvertAll(x => x.Name)));
                break;
        }
    }
    stop.Set();
}) { IsBackground = true }.Start();

stop.Wait();
server.Stop();
Upnp.ClosePort(server.Port);
