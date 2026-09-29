using System;
using System.Threading;
using SubnauticaMP.Shared;

// Dedicated server: `SubnauticaMP.Server [port]`. Type "quit" to stop.
int port = args.Length > 0 && int.TryParse(args[0], out var p) ? p : Protocol.DefaultPort;

var server = new NetServer();
server.Log += msg => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {msg}");
server.Start(port);

var stop = new ManualResetEventSlim();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Set(); };

new Thread(() =>
{
    string line;
    while ((line = Console.ReadLine()) != null)
    {
        if (line.Trim() == "quit") break;
        if (line.Trim() == "players") Console.WriteLine($"{server.PlayerCount} player(s) online");
    }
    stop.Set();
}) { IsBackground = true }.Start();

stop.Wait();
server.Stop();
