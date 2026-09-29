using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using SubnauticaMP.Shared;

namespace SubnauticaMP.Tests;

public class Batch2Tests
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
    public void PlayerInsideCyclopsSendsRelativePosition()
    {
        var p = new PlayerStatePacket { SubId = "cyclops-1", LocalPosition = new Vec3(1, 2, 3), LocalRotation = new Quat(0, 0, 0, 1), Held = "" };
        var got = (PlayerStatePacket)Protocol.ReadPacket(new MemoryStream(Protocol.Serialize(p)));
        Assert.Equal("cyclops-1", got.SubId);
        Assert.Equal(2, got.LocalPosition.Y);

        var outside = (PlayerStatePacket)Protocol.ReadPacket(new MemoryStream(Protocol.Serialize(new PlayerStatePacket())));
        Assert.Equal("", outside.SubId);
    }

    [Fact]
    public void VehicleStatsSnapshotsDockingAndCyclopsControls()
    {
        var path = Path.Combine(Path.GetTempPath(), "snmp-b2-" + Guid.NewGuid() + ".dat");
        try
        {
            var server = new NetServer(path);
            server.Start(0);
            var (a, _) = Join(server, "A");
            var (b, _) = Join(server, "B");

            a.Send(new VehicleSpawnedPacket { Vehicle = new VehicleInfo { Id = "sm", TechType = "Seamoth" } });
            WaitFor<VehicleSpawnedPacket>(b);

            a.Send(new VehicleStatePacket { Id = "sm", Health = 0.5f, Energy = 0.25f });
            var st = WaitFor<VehicleStatePacket>(b);
            Assert.Equal(0.5f, st.Health);
            Assert.Equal(0.25f, st.Energy);

            // only the driver (A) may replace the saved vehicle
            b.Send(new VehicleSnapshotPacket { Id = "sm", Data = new byte[] { 6, 6, 6 } });
            a.Send(new VehicleSnapshotPacket { Id = "sm", Data = new byte[] { 1, 2 } });
            Assert.Equal(2, WaitFor<VehicleSnapshotPacket>(b).Data.Length);

            a.Send(new VehicleDockPacket { Id = "sm", Docked = true, DockPosition = new Vec3(10, -20, 30) });
            Assert.True(WaitFor<VehicleDockPacket>(b).Docked);

            a.Send(new CyclopsStatePacket { Id = "cy", InternalLights = false, SilentRunning = true, MotorMode = 2 });
            var cy = WaitFor<CyclopsStatePacket>(b);
            Assert.False(cy.InternalLights);
            Assert.Equal(2, cy.MotorMode);
            SpinWait.SpinUntil(() => server.SnapshotWorld().Cyclopses.ContainsKey("cy"), 2000);
            server.Stop();

            var again = new NetServer(path);
            again.Start(0);
            try
            {
                var (_, w) = Join(again, "C");
                var v = w.World.Vehicles["sm"];
                Assert.Equal(0.5f, v.Health);
                Assert.Equal(new byte[] { 1, 2 }, v.Snapshot);
                Assert.True(v.Docked);
                Assert.Equal(-20, v.DockPosition.Y);
                Assert.True(w.World.Cyclopses["cy"].SilentRunning);
            }
            finally { again.Stop(); }
        }
        finally { File.Delete(path); }
    }
}
