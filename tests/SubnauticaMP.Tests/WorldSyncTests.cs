using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using SubnauticaMP.Shared;

namespace SubnauticaMP.Tests;

public class WorldSyncTests
{
    static T WaitFor<T>(NetClient client, Func<T, bool> match = null, int timeoutMs = 3000) where T : Packet
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            while (client.TryDequeue(out var p))
                if (p is T t && (match == null || match(t))) return t;
            Thread.Sleep(5);
        }
        throw new TimeoutException("No " + typeof(T).Name);
    }

    static (NetClient client, WelcomePacket welcome) Join(NetServer server, string name)
    {
        var c = new NetClient();
        c.Connect("127.0.0.1", server.Port, name);
        return (c, WaitFor<WelcomePacket>(c));
    }

    static void Drain(NetClient c) { while (c.TryDequeue(out _)) { } }

    [Fact]
    public void UnlocksReachOthersAndLateJoiners()
    {
        var server = new NetServer();
        server.Start(0);
        try
        {
            var (a, _) = Join(server, "A");
            var (b, _) = Join(server, "B");
            Drain(a);

            a.Send(new UnlockPacket { Kind = UnlockKind.Blueprint, Key = "Seamoth" });
            a.Send(new UnlockPacket { Kind = UnlockKind.Databank, Key = "Peeper" });
            var got = WaitFor<UnlockPacket>(b);
            Assert.Equal("Seamoth", got.Key);

            // duplicates don't get re-broadcast
            a.Send(new UnlockPacket { Kind = UnlockKind.Blueprint, Key = "Seamoth" });
            a.Send(new EntityRemovedPacket { EntityId = "rock-1" });
            Assert.Equal("rock-1", WaitFor<EntityRemovedPacket>(b).EntityId);

            var (_, welcome) = Join(server, "C");
            Assert.Contains("Seamoth", welcome.World.Blueprints);
            Assert.Contains("Peeper", welcome.World.Databank);
            Assert.Contains("rock-1", welcome.World.RemovedEntities);
            Assert.Equal(2, welcome.Players.Count);
        }
        finally { server.Stop(); }
    }

    [Fact]
    public void VehicleOwnershipRules()
    {
        var server = new NetServer();
        server.Start(0);
        try
        {
            var (a, _) = Join(server, "A");
            var (b, _) = Join(server, "B");
            Drain(a);

            a.Send(new VehicleSpawnedPacket { Vehicle = new VehicleInfo { Id = "v1", TechType = "Seamoth", Position = new Vec3(1, -5, 1) } });
            Assert.Equal("Seamoth", WaitFor<VehicleSpawnedPacket>(b).Vehicle.TechType);

            // B isn't the owner, so B can't move it
            b.Send(new VehicleStatePacket { Id = "v1", Position = new Vec3(99, 99, 99) });
            a.Send(new VehicleStatePacket { Id = "v1", Position = new Vec3(2, -6, 2) });
            Assert.Equal(2, WaitFor<VehicleStatePacket>(b).Position.X);

            // B hops in and claims it; everyone hears about it
            b.Send(new VehicleOwnerPacket { Id = "v1" });
            Assert.Equal(b.LocalId, WaitFor<VehicleOwnerPacket>(a).OwnerId);
            b.Send(new VehicleStatePacket { Id = "v1", Position = new Vec3(3, -7, 3) });
            Assert.Equal(3, WaitFor<VehicleStatePacket>(a).Position.X);

            // B leaves: vehicle is released, not deleted
            b.Disconnect();
            var released = WaitFor<VehicleOwnerPacket>(a, p => p.OwnerId == 0);
            Assert.Equal("v1", released.Id);
            var v = server.SnapshotWorld().Vehicles["v1"];
            Assert.Equal(3, v.Position.X);
            Assert.Equal(0, v.OwnerId);

            a.Send(new VehicleRemovedPacket { Id = "v1" });
            SpinUntil(() => server.SnapshotWorld().Vehicles.Count == 0);
        }
        finally { server.Stop(); }
    }

    [Fact]
    public void FirstPlayerSeedsTheClockAndItKeepsTicking()
    {
        var server = new NetServer();
        server.Start(0);
        try
        {
            var (a, welcome) = Join(server, "A");
            Assert.False(welcome.World.HasTime);

            a.Send(new TimeSyncPacket { TimePassed = 5000 });
            SpinUntil(() => server.SnapshotWorld().HasTime);
            a.Send(new TimeSyncPacket { TimePassed = 1 }); // second report is ignored

            var sync = WaitFor<TimeSyncPacket>(a, timeoutMs: 8000);
            Assert.InRange(sync.TimePassed, 5000, 5010);
        }
        finally { server.Stop(); }
    }

    [Fact]
    public void WorldSurvivesServerRestart()
    {
        var path = Path.Combine(Path.GetTempPath(), "snmp-test-" + Guid.NewGuid() + ".dat");
        try
        {
            var server = new NetServer(path);
            server.Start(0);
            var (a, _) = Join(server, "A");
            a.Send(new UnlockPacket { Kind = UnlockKind.Blueprint, Key = "Cyclops" });
            a.Send(new VehicleSpawnedPacket { Vehicle = new VehicleInfo { Id = "c1", TechType = "Cyclops" } });
            a.Send(new TimeSyncPacket { TimePassed = 1234 });
            SpinUntil(() => server.SnapshotWorld().Vehicles.Count == 1 && server.SnapshotWorld().HasTime);
            server.Stop();

            var again = new NetServer(path);
            again.Start(0);
            try
            {
                var (_, welcome) = Join(again, "B");
                Assert.Contains("Cyclops", welcome.World.Blueprints);
                Assert.Equal(0, welcome.World.Vehicles["c1"].OwnerId);
                Assert.True(welcome.World.HasTime);
                Assert.InRange(welcome.World.TimePassed, 1234, 1300);
            }
            finally { again.Stop(); }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void BogusPacketBeforeHelloGetsKicked()
    {
        var server = new NetServer();
        server.Start(0);
        try
        {
            using var tcp = new System.Net.Sockets.TcpClient("127.0.0.1", server.Port);
            var stream = tcp.GetStream();
            // claims a 10MB packet: over the client limit, server should just hang up
            stream.Write(BitConverter.GetBytes(10 * 1024 * 1024), 0, 4);
            stream.ReadTimeout = 3000;
            Assert.Equal(0, stream.Read(new byte[1], 0, 1));
        }
        finally { server.Stop(); }
    }

    static void SpinUntil(Func<bool> cond)
    {
        Assert.True(SpinWait.SpinUntil(cond, 3000), "condition never became true");
    }
}

public class JoinCodeTests
{
    [Theory]
    [InlineData("86.12.200.7", 11000)]
    [InlineData("255.255.255.255", 65535)]
    [InlineData("1.2.3.4", 1)]
    public void RoundTrips(string ip, int port)
    {
        var code = JoinCode.Encode(IPAddress.Parse(ip), port);
        Assert.Matches("^[A-Z2-9]{5}-[A-Z2-9]{5}$", code);
        Assert.True(JoinCode.TryDecode(code.ToLowerInvariant().Replace("-", " "), out var host, out var p));
        Assert.Equal(ip, host);
        Assert.Equal(port, p);
    }

    [Theory]
    [InlineData("192.168.1.5", "192.168.1.5", 11000)]
    [InlineData("192.168.1.5:2000", "192.168.1.5", 2000)]
    [InlineData("play.example.com:4000", "play.example.com", 4000)]
    [InlineData("localhost", "127.0.0.1", 11000)]
    public void ParsesAddresses(string input, string host, int port)
    {
        Assert.True(JoinCode.TryParseAddress(input, 11000, out var h, out var p));
        Assert.Equal(host, h);
        Assert.Equal(port, p);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1.2.3.4:99999")]
    [InlineData("has space")]
    public void RejectsJunk(string input)
    {
        Assert.False(JoinCode.TryParseAddress(input, 11000, out _, out _));
    }
}

public class LaunchInfoTests
{
    [Fact]
    public void RoundTripsAndExpires()
    {
        var path = Path.GetTempFileName();
        try
        {
            new LaunchInfo { PlayerName = "Mark", Host = "1.2.3.4", Port = 5000 }.Save(path);
            var info = LaunchInfo.TryLoad(path);
            Assert.Equal("Mark", info.PlayerName);
            Assert.Equal("1.2.3.4", info.Host);
            Assert.Equal(5000, info.Port);

            new LaunchInfo { PlayerName = "Old", Host = "1.2.3.4", CreatedUtc = DateTime.UtcNow.AddHours(-2) }.Save(path);
            Assert.Null(LaunchInfo.TryLoad(path));
        }
        finally { File.Delete(path); }
    }
}

public class UpnpTests
{
    [Theory]
    [InlineData("100.72.1.1", true)]
    [InlineData("192.168.0.2", true)]
    [InlineData("10.0.0.1", true)]
    [InlineData("86.12.200.7", false)]
    public void SpotsNonPublicAddresses(string ip, bool expected) => Assert.Equal(expected, Upnp.IsNonPublic(ip));

    [Theory]
    [InlineData("100.72.1.1", "86.1.1.1", true, false)]   // ISP shares the IP: CGNAT
    [InlineData("192.168.1.1", "86.1.1.1", false, true)]  // modem in front of the router: double NAT
    [InlineData("86.1.1.1", "86.1.1.1", false, false)]    // all good
    [InlineData("86.1.1.1", "90.2.2.2", false, true)]     // router's outside IP isn't what the web sees
    public void ClassifiesNetworkSetups(string routerIp, string webIp, bool cgnat, bool doubleNat)
    {
        var r = new PortForwardResult { Success = true, ExternalIp = routerIp, PublicIp = webIp };
        Upnp.Classify(r);
        Assert.Equal(cgnat, r.BehindCgnat);
        Assert.Equal(doubleNat, r.DoubleNat);
    }

    [Fact]
    public void TalksToARouter()
    {
        // Tiny fake router: serves a device description and answers the two SOAP calls.
        int port = FreePort();
        var prefix = $"http://127.0.0.1:{port}/";
        using var http = new HttpListener();
        http.Prefixes.Add(prefix);
        http.Start();
        string addBody = null;

        var thread = new Thread(() =>
        {
            try
            {
                while (http.IsListening)
                {
                    var ctx = http.GetContext();
                    string reply;
                    if (ctx.Request.Url.AbsolutePath == "/desc.xml")
                    {
                        reply = "<?xml version=\"1.0\"?><root xmlns=\"urn:schemas-upnp-org:device-1-0\"><device><deviceList><device><serviceList>" +
                                "<service><serviceType>urn:schemas-upnp-org:service:WANIPConnection:1</serviceType><controlURL>/ctl/IPConn</controlURL></service>" +
                                "</serviceList></device></deviceList></device></root>";
                    }
                    else
                    {
                        var body = new StreamReader(ctx.Request.InputStream).ReadToEnd();
                        var action = ctx.Request.Headers["SOAPACTION"];
                        if (action.Contains("AddPortMapping")) addBody = body;
                        reply = action.Contains("GetExternalIPAddress")
                            ? "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><u:GetExternalIPAddressResponse xmlns:u=\"urn:schemas-upnp-org:service:WANIPConnection:1\"><NewExternalIPAddress>86.12.200.7</NewExternalIPAddress></u:GetExternalIPAddressResponse></s:Body></s:Envelope>"
                            : "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body/></s:Envelope>";
                    }
                    var bytes = Encoding.UTF8.GetBytes(reply);
                    ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
                    ctx.Response.Close();
                }
            }
            catch { }
        }) { IsBackground = true };
        thread.Start();

        var result = Upnp.OpenPortAt(prefix + "desc.xml", 11000, "SubnauticaMP");
        http.Stop();

        Assert.True(result.Success, result.Error);
        Assert.Equal("86.12.200.7", result.ExternalIp);
        Assert.False(result.BehindCgnat);
        Assert.Contains("<NewExternalPort>11000</NewExternalPort>", addBody);
        Assert.Contains("<NewInternalClient>127.0.0.1</NewInternalClient>", addBody);
    }

    static int FreePort()
    {
        var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int p = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }
}
