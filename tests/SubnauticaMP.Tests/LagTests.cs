using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using SubnauticaMP.Shared;

namespace SubnauticaMP.Tests;

public class LagTests
{
    [Fact]
    public void BigPacketsAreCompressedAndComeBackTheSame()
    {
        var data = new byte[2 * 1024 * 1024];
        for (int i = 0; i < data.Length; i++) data[i] = (byte)(i % 7); // base snapshots repeat a lot
        var frame = Protocol.Serialize(new StructurePacket { Id = "base", Data = data });
        Assert.True(frame.Length < data.Length / 4, $"frame is {frame.Length} bytes");

        var back = (StructurePacket)Protocol.ReadPacket(new MemoryStream(frame));
        Assert.Equal("base", back.Id);
        Assert.Equal(data, back.Data);

        // small packets stay as they are
        var chat = (ChatPacket)Protocol.ReadPacket(new MemoryStream(Protocol.Serialize(new ChatPacket { Text = "hi" })));
        Assert.Equal("hi", chat.Text);
    }

    [Fact]
    public void SendingNeverBlocksTheCaller()
    {
        var server = new NetServer();
        server.Start(0);
        try
        {
            var a = new NetClient();
            a.Connect("127.0.0.1", server.Port, "A");
            var sw = Stopwatch.StartNew();
            while (a.State != ClientState.Connected && sw.ElapsedMilliseconds < 3000) Thread.Sleep(5);

            var rnd = new Random(1);
            var blob = new byte[6 * 1024 * 1024];
            rnd.NextBytes(blob); // doesn't compress: worst case
            sw.Restart();
            for (int i = 0; i < 3; i++) a.Send(new StructurePacket { Id = "b" + i, Data = blob });
            Assert.True(sw.ElapsedMilliseconds < 1000, $"Send took {sw.ElapsedMilliseconds} ms");
            SpinWait.SpinUntil(() => server.SnapshotWorld().Structures.Count == 3, 10000);
            Assert.Equal(3, server.SnapshotWorld().Structures.Count);
        }
        finally { server.Stop(); }
    }
}
