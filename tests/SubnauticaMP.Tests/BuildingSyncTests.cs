using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using SubnauticaMP.Shared;

namespace SubnauticaMP.Tests;

public class BuildingSyncTests
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

    static (NetClient, WelcomePacket) Join(NetServer server, string name)
    {
        var c = new NetClient();
        c.Connect("127.0.0.1", server.Port, name);
        return (c, WaitFor<WelcomePacket>(c));
    }

    static byte[] Blob(int size, byte fill)
    {
        var b = new byte[size];
        for (int i = 0; i < size; i++) b[i] = (byte)(fill + i);
        return b;
    }

    [Fact]
    public void BasesLockersAndPdaReachEveryoneAndSurviveRestart()
    {
        var path = Path.Combine(Path.GetTempPath(), "snmp-build-" + Guid.NewGuid() + ".dat");
        try
        {
            var server = new NetServer(path);
            server.Start(0);
            var (a, _) = Join(server, "A");
            var (b, _) = Join(server, "B");

            // a 2 MB base: bigger than the old 64 KB client limit
            var baseBlob = Blob(2 * 1024 * 1024, 7);
            a.Send(new StructurePacket { Id = "base-1", Data = baseBlob });
            Assert.Equal(baseBlob, WaitFor<StructurePacket>(b).Data);

            a.Send(new ContainerPacket { Id = "locker-1", Items = new List<byte[]> { Blob(100, 1), Blob(50, 2) } });
            Assert.Equal(2, WaitFor<ContainerPacket>(b).Items.Count);

            a.Send(new FragmentPacket { TechType = "Seamoth", Unlocked = 2 });
            Assert.Equal(2, WaitFor<FragmentPacket>(b).Unlocked);
            b.Send(new FragmentPacket { TechType = "Seamoth", Unlocked = 1 }); // older progress is ignored

            a.Send(new UnlockPacket { Kind = UnlockKind.PdaLog, Key = "Aurora_Warning" });
            WaitFor<UnlockPacket>(b);

            a.Send(new StructurePacket { Id = "shed", Data = Blob(10, 3) });
            a.Send(new StructurePacket { Id = "shed", Data = new byte[0] }); // deconstructed
            WaitFor<StructurePacket>(b, p => p.Id == "shed" && p.Data.Length == 0);
            server.Stop();

            var again = new NetServer(path);
            again.Start(0);
            try
            {
                var (_, w) = Join(again, "C");
                Assert.Equal(baseBlob, w.World.Structures["base-1"]);
                Assert.False(w.World.Structures.ContainsKey("shed"));
                Assert.Equal(2, w.World.Containers["locker-1"].Count);
                Assert.Equal(2, w.World.Fragments["Seamoth"]);
                Assert.Contains("Aurora_Warning", w.World.PdaLog);
            }
            finally { again.Stop(); }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void StrangersCantSendHugePacketsBeforeHello()
    {
        var server = new NetServer();
        server.Start(0);
        try
        {
            using var tcp = new System.Net.Sockets.TcpClient("127.0.0.1", server.Port);
            var stream = tcp.GetStream();
            stream.Write(BitConverter.GetBytes(1024 * 1024), 0, 4); // 1 MB claim before saying hello
            stream.ReadTimeout = 3000;
            Assert.Equal(0, stream.Read(new byte[1], 0, 1));
        }
        finally { server.Stop(); }
    }
}
