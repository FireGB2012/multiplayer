using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using SubnauticaMP.Shared;

namespace SubnauticaMP.Tests;

public class CreatureTests
{
    static T WaitFor<T>(NetClient client, Func<T, bool> match = null, int timeoutMs = 10000) where T : Packet
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

    static void Drain(NetClient c, int ms = 150)
    {
        Thread.Sleep(ms);
        while (c.TryDequeue(out _)) { }
    }

    static (NetClient, WelcomePacket) Join(NetServer server, string name)
    {
        var c = new NetClient();
        c.Connect("127.0.0.1", server.Port, name);
        return (c, WaitFor<WelcomePacket>(c));
    }

    static CreatureOwnerPacket Own(int owner, params string[] ids) => new CreatureOwnerPacket { OwnerId = owner, Ids = new List<string>(ids) };

    [Fact]
    public void FirstSpawnRollWinsAndIsSaved()
    {
        var path = Path.Combine(Path.GetTempPath(), "snmp-cr-" + Guid.NewGuid() + ".dat");
        try
        {
            var server = new NetServer(path);
            server.Start(0);
            var (a, _) = Join(server, "A");
            var (b, _) = Join(server, "B");

            a.Send(new SpawnSlotsPacket { Slots = { new SpawnSlot { Key = "1,2,3", ClassId = "peeper", Count = 3 }, new SpawnSlot { Key = "4,5,6", ClassId = "", Count = 0 } } });
            var got = WaitFor<SpawnSlotsPacket>(b);
            Assert.Equal(2, got.Slots.Count);

            // B rolled something else for the same slot plus one new slot: only the new one goes through
            b.Send(new SpawnSlotsPacket { Slots = { new SpawnSlot { Key = "1,2,3", ClassId = "stalker", Count = 1 }, new SpawnSlot { Key = "7,8,9", ClassId = "boomerang", Count = 2 } } });
            var fromB = WaitFor<SpawnSlotsPacket>(a);
            Assert.Single(fromB.Slots);
            Assert.Equal("7,8,9", fromB.Slots[0].Key);
            SpinWait.SpinUntil(() => server.SnapshotWorld().SpawnBook.Count == 3, 2000);
            server.Stop();

            var again = new NetServer(path);
            again.Start(0);
            try
            {
                var (_, w) = Join(again, "C");
                Assert.Equal(3, w.World.SpawnBook.Count);
                Assert.Equal("peeper", w.World.SpawnBook["1,2,3"].ClassId);
                Assert.Equal(3, w.World.SpawnBook["1,2,3"].Count);
            }
            finally { again.Stop(); }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void OwnershipClaimReleaseHandoffAndDisconnect()
    {
        var server = new NetServer();
        server.Start(0);
        try
        {
            var (a, wa) = Join(server, "A");
            var (b, wb) = Join(server, "B");
            Drain(a);

            a.Send(Own(wa.YourId, "fish1", "fish2"));
            var grant = WaitFor<CreatureOwnerPacket>(b);
            Assert.Equal(wa.YourId, grant.OwnerId);
            Assert.Equal(2, grant.Ids.Count);

            // B can't steal fish1, but gets fish3
            b.Send(Own(wb.YourId, "fish1", "fish3"));
            var bGrant = WaitFor<CreatureOwnerPacket>(a, p => p.OwnerId == wb.YourId);
            Assert.Equal(new[] { "fish3" }, bGrant.Ids);

            // only B's stream for fish3 gets through; A's fake one for fish3 is dropped
            a.Send(new CreatureStatesPacket { States = { new CreatureState { Id = "fish3", Position = new Vec3(9, 9, 9) }, new CreatureState { Id = "fish1", Position = new Vec3(1, 1, 1) } } });
            var st = WaitFor<CreatureStatesPacket>(b);
            Assert.Single(st.States);
            Assert.Equal("fish1", st.States[0].Id);

            // A hands fish1 over to B (it's chasing B)
            a.Send(Own(wb.YourId, "fish1"));
            var handoff = WaitFor<CreatureOwnerPacket>(b, p => p.Ids.Contains("fish1"));
            Assert.Equal(wb.YourId, handoff.OwnerId);

            // A releases fish2
            a.Send(Own(0, "fish2"));
            Assert.Equal(0, WaitFor<CreatureOwnerPacket>(b, p => p.Ids.Contains("fish2")).OwnerId);

            // a late joiner learns who owns what
            var (c, _) = Join(server, "C");
            var known = WaitFor<CreatureOwnerPacket>(c);
            Assert.Equal(wb.YourId, known.OwnerId);
            Assert.Equal(2, known.Ids.Count); // fish1 + fish3

            // B leaves: its creatures are free again
            b.Disconnect();
            var freed = WaitFor<CreatureOwnerPacket>(a, p => p.OwnerId == 0 && p.Ids.Contains("fish1"));
            Assert.Contains("fish3", freed.Ids);
        }
        finally { server.Stop(); }
    }

    [Fact]
    public void DamageGoesToTheOwnerAndDeathsStick()
    {
        var server = new NetServer();
        server.Start(0);
        try
        {
            var (a, wa) = Join(server, "A");
            var (b, wb) = Join(server, "B");
            a.Send(Own(wa.YourId, "shark"));
            WaitFor<CreatureOwnerPacket>(b);

            b.Send(new CreatureDamagePacket { Id = "shark", Damage = 30, DamageType = 0 });
            Assert.Equal(30, WaitFor<CreatureDamagePacket>(a).Damage);

            a.Send(new CreatureDiedPacket { Id = "shark" });
            Assert.Equal("shark", WaitFor<CreatureDiedPacket>(b).Id);
            SpinWait.SpinUntil(() => server.SnapshotWorld().RemovedEntities.Contains("shark"), 2000);
            Assert.Contains("shark", server.SnapshotWorld().RemovedEntities);

            // nobody can claim a dead creature
            Drain(b);
            b.Send(Own(wb.YourId, "shark"));
            Thread.Sleep(200);
            Assert.False(b.TryDequeue(out var p) && p is CreatureOwnerPacket);
        }
        finally { server.Stop(); }
    }
}
