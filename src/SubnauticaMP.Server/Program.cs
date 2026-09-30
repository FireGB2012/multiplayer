using System;
using System.IO;
using System.Net;
using System.Threading;
using SubnauticaMP.Shared;

// Dedicated server: `SubnauticaMP.Server [port] [world file] [--password secret] [--autosave minutes] [--backups count]`
// Commands: players, kick <name>, ban <name>, unban <name>, bans, save, quit
var plain = new System.Collections.Generic.List<string>();
string password = "";
double autosave = 2;
int backups = 10;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--password" && i + 1 < args.Length) password = args[++i];
    else if (args[i] == "--autosave" && i + 1 < args.Length && double.TryParse(args[++i], out var mins)) autosave = mins;
    else if (args[i] == "--backups" && i + 1 < args.Length && int.TryParse(args[++i], out var keep)) backups = keep;
    else plain.Add(args[i]);
}
int port = plain.Count > 0 && int.TryParse(plain[0], out var p) ? p : Protocol.DefaultPort;
string worldFile = plain.Count > 1 ? plain[1] : Path.Combine(AppContext.BaseDirectory, "world.dat");

void Log(string msg) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {msg}");

var server = new NetServer(worldFile) { Password = password, TrustLocalPlayers = false, AutosaveMinutes = autosave, MaxBackups = backups };
server.Log += Log;
server.Start(port);

int? FindPlayer(string name)
{
    var match = server.Players.Find(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
    return match?.Id;
}

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
        var cmd = line.Trim();
        int space = cmd.IndexOf(' ');
        string arg = space > 0 ? cmd.Substring(space + 1).Trim() : "";
        if (space > 0) cmd = cmd.Substring(0, space);
        switch (cmd)
        {
            case "kick":
            case "ban":
                var id = FindPlayer(arg);
                if (id == null) Log($"No player called '{arg}' online.");
                else server.Kick(id.Value, cmd == "ban");
                break;
            case "unban": Log(server.Unban(arg) ? $"Unbanned {arg}." : $"'{arg}' wasn't banned."); break;
            case "bans":
                var bans = server.Bans;
                Log(bans.Count == 0 ? "Nobody is banned." : "Banned: " + string.Join(", ", bans.ConvertAll(b => b.Name + (b.Ip.Length > 0 ? $" ({b.Ip})" : ""))));
                break;
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
