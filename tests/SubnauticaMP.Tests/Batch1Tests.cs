using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using SubnauticaMP.Shared;

namespace SubnauticaMP.Tests;

public class Batch1Tests
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

    [Fact]
    public void VitalsAndHeldItemTravelWithPosition()
    {
        var p = new PlayerStatePacket { Health = 85, Food = 60, Water = 70, Held = "Knife" };
        var got = (PlayerStatePacket)Protocol.ReadPacket(new MemoryStream(Protocol.Serialize(p)));
        Assert.Equal(85, got.Health);
        Assert.Equal(70, got.Water);
        Assert.Equal("Knife", got.Held);
    }

    [Fact]
    public void DroppedItemsDoorsAndDeaths()
    {
        var path = Path.Combine(Path.GetTempPath(), "snmp-b1-" + Guid.NewGuid() + ".dat");
        try
        {
            var server = new NetServer(path);
            server.Start(0);
            var (a, _) = Join(server, "A");
            var (b, _) = Join(server, "B");

            // A picked this up earlier, then drops it again: it's back in the world
            a.Send(new EntityRemovedPacket { EntityId = "titanium-1" });
            WaitFor<EntityRemovedPacket>(b);
            a.Send(new ItemDroppedPacket { Id = "titanium-1", Data = new byte[] { 1, 2, 3 } });
            Assert.Equal(3, WaitFor<ItemDroppedPacket>(b).Data.Length);

            a.Send(new ItemDroppedPacket { Id = "beacon-1", Data = new byte[] { 9 } });
            WaitFor<ItemDroppedPacket>(b, x => x.Id == "beacon-1");
            b.Send(new EntityRemovedPacket { EntityId = "beacon-1" }); // B picks the beacon back up
            WaitFor<EntityRemovedPacket>(a, x => x.EntityId == "beacon-1");

            a.Send(new DoorPacket { Id = "hatch-1", Open = true, Duration = 1f });
            Assert.True(WaitFor<DoorPacket>(b).Open);

            b.Send(new PlayerDiedPacket { Position = new Vec3(5, -100, 5) });
            var died = WaitFor<PlayerDiedPacket>(a);
            Assert.Equal(b.LocalId, died.Id);
            Assert.Equal(-100, died.Position.Y);
            server.Stop();

            var again = new NetServer(path);
            again.Start(0);
            try
            {
                var (_, w) = Join(again, "C");
                Assert.True(w.World.DroppedItems.ContainsKey("titanium-1"));
                Assert.DoesNotContain("titanium-1", w.World.RemovedEntities);
                Assert.False(w.World.DroppedItems.ContainsKey("beacon-1"));
                Assert.True(w.World.Doors["hatch-1"]);
            }
            finally { again.Stop(); }
        }
        finally { File.Delete(path); }
    }
}
