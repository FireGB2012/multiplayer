using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using SubnauticaMP.Shared;

namespace SubnauticaMP.Tests;

public class Batch5Tests
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

    static (NetClient, WelcomePacket) Join(NetServer server, string name, string password = null)
    {
        var c = new NetClient();
        c.Connect("127.0.0.1", server.Port, name, password: password);
        return (c, WaitFor<WelcomePacket>(c));
    }

    static string Rejection(NetServer server, string name, string password = null)
    {
        var c = new NetClient();
        c.Connect("127.0.0.1", server.Port, name, password: password);
        return WaitFor<RejectedPacket>(c).Reason;
    }

    [Fact]
    public void PasswordIsChecked()
    {
        var server = new NetServer { Password = "reef", TrustLocalPlayers = false };
        server.Start(0);
        try
        {
            Assert.Contains("needs a password", Rejection(server, "A"));
            Assert.Contains("Wrong password", Rejection(server, "A", "nope"));
            Join(server, "A", "reef");
        }
        finally { server.Stop(); }
    }

    [Fact]
    public void HostCanKickAndBanAndBansAreSavedButNotShared()
    {
        var path = Path.Combine(Path.GetTempPath(), "snmp-b5-" + Guid.NewGuid() + ".dat");
        try
        {
            var server = new NetServer(path) { TrustLocalPlayers = false };
            server.Start(0);
            var (host, wh) = Join(server, "Host");
            var (b, wb) = Join(server, "Bob");
            var (c, wc) = Join(server, "Cat");

            // not the host: ignored
            b.Send(new KickPacket { TargetId = wc.YourId });
            Thread.Sleep(200);
            Assert.Equal(3, server.PlayerCount);

            host.Send(new KickPacket { TargetId = wc.YourId });
            Assert.Contains("kicked", WaitFor<RejectedPacket>(c).Reason);
            Join(server, "Cat"); // kicked, not banned: can come back

            host.Send(new KickPacket { TargetId = wb.YourId, Ban = true });
            Assert.Contains("banned", WaitFor<RejectedPacket>(b).Reason);
            Assert.Contains("banned", Rejection(server, "bob"));
            Assert.Single(server.Bans);
            server.Stop();

            var again = new NetServer(path) { TrustLocalPlayers = false };
            again.Start(0);
            try
            {
                Assert.Contains("banned", Rejection(again, "Bob"));
                var (_, w) = Join(again, "Dan");
                Assert.Empty(w.World.Bans); // players never see the ban list (it has IPs)
                Assert.True(again.Unban("BOB"));
            }
            finally { again.Stop(); }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void NightIsSkippedOnlyWhenEveryoneSleeps()
    {
        var server = new NetServer();
        server.Start(0);
        try
        {
            var (a, _) = Join(server, "A");
            var (b, _) = Join(server, "B");
            a.Send(new TimeSyncPacket { TimePassed = 1000 });
            SpinWait.SpinUntil(() => server.SnapshotWorld().HasTime, 2000);

            a.Send(new SleepPacket { Asleep = true, Amount = 396 });
            var seen = WaitFor<SleepPacket>(b);
            Assert.True(seen.Asleep);
            Assert.False(seen.Skip);
            Assert.True(server.SnapshotWorld().TimePassed < 1100);

            b.Send(new SleepPacket { Asleep = true, Amount = 396 });
            Assert.Equal(396, WaitFor<SleepPacket>(a, p => p.Skip).Amount);
            Assert.True(server.SnapshotWorld().TimePassed >= 1396);
        }
        finally { server.Stop(); }
    }

    [Fact]
    public void OnePlayerRunsEachPowerSourceAndOthersSendWhatTheyUse()
    {
        var server = new NetServer();
        server.Start(0);
        try
        {
            var (a, wa) = Join(server, "A");
            var (b, _) = Join(server, "B");

            a.Send(new PowerPacket { Id = "solar1", Power = 50 });
            var got = WaitFor<PowerPacket>(b);
            Assert.Equal(wa.YourId, got.WriterId);

            // B's report is ignored while A runs it
            b.Send(new PowerPacket { Id = "solar1", Power = 999 });
            // B used 5 power: goes to A
            b.Send(new PowerPacket { Id = "solar1", Drain = 5 });
            Assert.Equal(5, WaitFor<PowerPacket>(a, p => p.Drain > 0).Drain);
            Assert.Equal(45, server.SnapshotWorld().Power["solar1"]);
        }
        finally { server.Stop(); }
    }

    [Fact]
    public void NewPacketsRoundTrip()
    {
        T Trip<T>(T p) where T : Packet => (T)Protocol.ReadPacket(new MemoryStream(Protocol.Serialize(p)));

        var st = Trip(new PlayerStatePacket { Held = "Knife", Gear = "Body=RadiationSuit;Foots=Fins", Anim = "holding_knife,using_tool", Flags = PlayerFlags.Sleeping });
        Assert.Equal("Body=RadiationSuit;Foots=Fins", st.Gear);
        Assert.Equal("holding_knife,using_tool", st.Anim);
        Assert.Equal(PlayerFlags.Sleeping, st.Flags);

        Assert.Equal("Titanium", Trip(new CraftPacket { CrafterId = "fab", TechType = "Titanium", Duration = 3 }).TechType);
        Assert.True(Trip(new BuildGhostPacket { TechType = "BaseRoom", CanPlace = true }).CanPlace);
        Assert.Equal(2, Trip(new FiresPacket { SubId = "cy", Nodes = { "a", "b" } }).Nodes.Count);
        Assert.Equal(-12.5f, Trip(new HullHealthPacket { Key = "base|1,2,3", Delta = -12.5f }).Delta);
        Assert.Equal("x|1,1,1", Trip(new PickedPacket { Key = "x|1,1,1" }).Key);
        Assert.Equal("pw", Trip(new HelloPacket { Name = "A", Password = "pw" }).Password);
    }

    [Fact]
    public void LobbyNamesAndColorsAreSharedAndUnique()
    {
        var server = new NetServer();
        server.Start(0);
        try
        {
            var a = new NetClient();
            a.Connect("127.0.0.1", server.Port, "Diver", color: 0xFF4D4D);
            WaitFor<WelcomePacket>(a);
            var b = new NetClient();
            b.Connect("127.0.0.1", server.Port, "Diver", color: 0x3DC8FF);
            var wb = WaitFor<WelcomePacket>(b);
            Assert.Equal(0xFF4D4D, wb.Players[0].Color);
            var joined = WaitFor<PlayerJoinedPacket>(a);
            Assert.Equal("Diver 2", joined.Name);          // same name gets a number
            Assert.Equal(0x3DC8FF, joined.Color);

            b.Send(new PlayerProfilePacket { Name = "Mark", Color = 0x5BD68B });
            var p = WaitFor<PlayerProfilePacket>(a);
            Assert.Equal(wb.YourId, p.Id);
            Assert.Equal("Mark", p.Name);
            Assert.Equal(0x5BD68B, p.Color);
            Assert.Contains(server.Players, x => x.Name == "Mark");
        }
        finally { server.Stop(); }
    }

    [Fact]
    public void LauncherPassesThePassword()
    {
        var path = Path.Combine(Path.GetTempPath(), "snmp-launch-" + Guid.NewGuid() + ".txt");
        try
        {
            new LaunchInfo { PlayerName = "A", Host = "1.2.3.4", Port = 11000, Password = "reef" }.Save(path);
            Assert.Equal("reef", LaunchInfo.TryLoad(path).Password);
        }
        finally { File.Delete(path); }
    }
}
