using System;
using System.Diagnostics;
using System.IO;
using SubnauticaMP.Shared;

namespace SubnauticaMP.Tests;

public class NetworkTests
{
    static T WaitFor<T>(NetClient client, int timeoutMs = 10000) where T : Packet
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            while (client.TryDequeue(out var p))
                if (p is T match) return match;
            System.Threading.Thread.Sleep(5);
        }
        throw new TimeoutException("No " + typeof(T).Name);
    }

    static NetServer StartServer()
    {
        var server = new NetServer();
        server.Start(0); // random free port
        return server;
    }

    [Fact]
    public void PacketRoundTrips()
    {
        var sent = new PlayerStatePacket
        {
            Id = 7,
            Position = new Vec3(1, -250.5f, 3),
            Rotation = new Quat(0, 0.7f, 0, 0.7f),
            Flags = PlayerFlags.Underwater | PlayerFlags.InVehicle,
        };
        var got = (PlayerStatePacket)Protocol.ReadPacket(new MemoryStream(Protocol.Serialize(sent)));
        Assert.Equal(7, got.Id);
        Assert.Equal(-250.5f, got.Position.Y);
        Assert.Equal(0.7f, got.Rotation.W);
        Assert.Equal(sent.Flags, got.Flags);
    }

    [Fact]
    public void RejectsOversizedFrame()
    {
        var bytes = BitConverter.GetBytes(Protocol.MaxPacketSize + 1);
        Assert.Throws<InvalidDataException>(() => Protocol.ReadPacket(new MemoryStream(bytes)));
    }

    [Fact]
    public void TwoPlayersSeeEachOther()
    {
        var server = StartServer();
        try
        {
            var a = new NetClient();
            a.Connect("127.0.0.1", server.Port, "Mark");
            var welcomeA = WaitFor<WelcomePacket>(a);
            Assert.Empty(welcomeA.Players);

            var b = new NetClient();
            b.Connect("127.0.0.1", server.Port, "Omni-Man");
            var welcomeB = WaitFor<WelcomePacket>(b);
            Assert.Single(welcomeB.Players);
            Assert.Equal("Mark", welcomeB.Players[0].Name);

            var joined = WaitFor<PlayerJoinedPacket>(a);
            Assert.Equal("Omni-Man", joined.Name);
            Assert.Equal(b.LocalId, joined.Id);

            // state relays to the other player with the sender's id stamped by the server
            b.Send(new PlayerStatePacket { Id = 999, Position = new Vec3(10, -20, 30) });
            var state = WaitFor<PlayerStatePacket>(a);
            Assert.Equal(b.LocalId, state.Id);
            Assert.Equal(-20, state.Position.Y);

            // chat goes to everyone including the sender
            a.Send(new ChatPacket { Text = "  think mark  " });
            Assert.Equal("think mark", WaitFor<ChatPacket>(a).Text);
            var chatB = WaitFor<ChatPacket>(b);
            Assert.Equal(a.LocalId, chatB.SenderId);

            b.Disconnect();
            Assert.Equal(b.LocalId, WaitFor<PlayerLeftPacket>(a).Id);
            Assert.Equal(ClientState.Disconnected, b.State);
        }
        finally { server.Stop(); }
    }

    [Fact]
    public void VersionMismatchGetsRejected()
    {
        var server = StartServer();
        try
        {
            using var tcp = new System.Net.Sockets.TcpClient("127.0.0.1", server.Port);
            var stream = tcp.GetStream();
            var hello = Protocol.Serialize(new HelloPacket { ProtocolVersion = Protocol.Version + 1, Name = "x" });
            stream.Write(hello, 0, hello.Length);
            var reply = Protocol.ReadPacket(stream);
            Assert.IsType<RejectedPacket>(reply);
        }
        finally { server.Stop(); }
    }

    [Fact]
    public void ConnectToDeadPortFails()
    {
        var server = StartServer();
        int port = server.Port;
        server.Stop();

        var c = new NetClient();
        c.Connect("127.0.0.1", port, "x", timeoutMs: 1000);
        var sw = Stopwatch.StartNew();
        while (c.State != ClientState.Disconnected && sw.ElapsedMilliseconds < 3000) System.Threading.Thread.Sleep(10);
        Assert.Equal(ClientState.Disconnected, c.State);
        Assert.NotNull(c.LastError);
    }
}
