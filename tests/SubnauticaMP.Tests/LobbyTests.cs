using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using SubnauticaMP.Shared;

namespace SubnauticaMP.Tests;

public class LobbyTests
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

    static bool Receives<T>(NetClient client, int ms) where T : Packet
    {
        try { WaitFor<T>(client, timeoutMs: ms); return true; }
        catch (TimeoutException) { return false; }
    }

    static (NetClient, WelcomePacket) Join(NetServer server, string name)
    {
        var c = new NetClient();
        c.Connect("127.0.0.1", server.Port, name);
        return (c, WaitFor<WelcomePacket>(c));
    }

    [Fact]
    public void NewWorldWaitsForHostThenStartsForEveryone()
    {
        var server = new NetServer(gameMode: GameModes.Creative);
        server.Start(0);
        try
        {
            var (host, hw) = Join(server, "Host");
            Assert.False(hw.World.Started);
            Assert.Equal(GameModes.Creative, hw.World.GameMode);
            Assert.Equal(host.LocalId, hw.HostId);
            Assert.False(string.IsNullOrEmpty(hw.World.WorldId));

            var (friend, fw) = Join(server, "Friend");
            Assert.Equal(host.LocalId, fw.HostId);
            Assert.Equal(hw.World.WorldId, fw.World.WorldId);

            // only the host can start it
            friend.Send(new StartGamePacket());
            Assert.False(Receives<StartGamePacket>(host, 300));
            Assert.False(server.SnapshotWorld().Started);

            host.Send(new StartGamePacket());
            WaitFor<StartGamePacket>(host);
            WaitFor<StartGamePacket>(friend);
            Assert.True(server.SnapshotWorld().Started);

            // late joiners skip the lobby
            var (_, lw) = Join(server, "Late");
            Assert.True(lw.World.Started);
        }
        finally { server.Stop(); }
    }

    [Fact]
    public void HostMovesOnWhenHostLeaves()
    {
        var server = new NetServer();
        server.Start(0);
        try
        {
            var (a, _) = Join(server, "A");
            var (b, _) = Join(server, "B");
            a.Disconnect();
            Assert.Equal(b.LocalId, WaitFor<HostPacket>(b).HostId);

            b.Send(new StartGamePacket());
            WaitFor<StartGamePacket>(b);
        }
        finally { server.Stop(); }
    }

    [Fact]
    public void InGameHostSkipsLobbyAndModeIsSaved()
    {
        var path = Path.Combine(Path.GetTempPath(), "snmp-lobby-" + Guid.NewGuid() + ".dat");
        try
        {
            var server = new NetServer(path, GameModes.Hardcore, started: true);
            server.Start(0);
            var (_, w) = Join(server, "A");
            Assert.True(w.World.Started);
            var id = w.World.WorldId;
            server.Stop();

            // reopening with a different mode keeps the saved world's mode
            var again = new NetServer(path, GameModes.Creative);
            again.Start(0);
            try
            {
                var (_, w2) = Join(again, "B");
                Assert.Equal(GameModes.Hardcore, w2.World.GameMode);
                Assert.Equal(id, w2.World.WorldId);
                Assert.True(w2.World.Started);
            }
            finally { again.Stop(); }
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("creative", "Creative", 1790)]
    [InlineData("HARDCORE", "Hardcore", 257)]
    [InlineData("junk", "Survival", 0)]
    public void GameModeNames(string input, string normal, int option)
    {
        Assert.Equal(normal, GameModes.Normalize(input));
        Assert.Equal(option, GameModes.OptionValue(input));
    }
}
