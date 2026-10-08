using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using SubnauticaMP.Shared;
using Xunit;

namespace SubnauticaMP.Tests;

public class BatTests
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

    static List<Packet> Drain(NetClient client, int ms)
    {
        var got = new List<Packet>();
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms)
        {
            while (client.TryDequeue(out var p)) got.Add(p);
            Thread.Sleep(5);
        }
        return got;
    }

    static NetClient Join(NetServer server, string name)
    {
        var c = new NetClient();
        c.Connect("127.0.0.1", server.Port, name);
        WaitFor<WelcomePacket>(c);
        return c;
    }

    static void Hold(NetClient c, string tech) => c.Send(new PlayerStatePacket { Held = tech, Rotation = new Quat(0, 0, 0, 1) });

    static float Len(Vec3 v) => (float)Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
    static double PitchOf(Vec3 v) => Math.Asin(v.Y / Len(v)) * 180 / Math.PI;

    [Fact]
    public void LookingUpLaunchesUpward()
    {
        var flat = Bat.LaunchVelocity(new Vec3(0, 0, 1), underwater: false, indoors: false);
        var up = Bat.LaunchVelocity(new Vec3(0, 1, 1), underwater: false, indoors: false);
        var straightUp = Bat.LaunchVelocity(new Vec3(0, 1, 0), underwater: false, indoors: false);
        Assert.True(PitchOf(flat) > 15 && PitchOf(flat) < 30, "on land a level hit still arcs up a bit");
        Assert.True(PitchOf(up) > 55, "looking up sends them up");
        Assert.True(PitchOf(straightUp) > 75, "looking straight up: nearly straight up");
        Assert.True(flat.Z > 0 && up.Z > 0, "away from the hitter");
        Assert.True(Bat.HomeRun(new Vec3(0, 1, 1)) && !Bat.HomeRun(new Vec3(0, 0.2f, 1)));
    }

    [Fact]
    public void LaunchIsFarHarderThanAPushAndSofterIndoors()
    {
        var water = Bat.LaunchVelocity(new Vec3(1, 0, 0), underwater: true, indoors: false);
        Assert.True(Len(water) > 2 * 9f, "a push is 9 m/s underwater; the bat at least twice that");
        Assert.Equal(0.0, PitchOf(water), 1); // underwater: straight along the look
        var down = Bat.LaunchVelocity(new Vec3(0, -1, 1), underwater: true, indoors: false);
        Assert.True(down.Y < 0, "underwater you can hit them down too");
        var inside = Bat.LaunchVelocity(new Vec3(1, 0, 0), underwater: false, indoors: true);
        var outside = Bat.LaunchVelocity(new Vec3(1, 0, 0), underwater: false, indoors: false);
        Assert.True(Len(inside) < Len(outside) * 0.6f);
        var bad = Bat.LaunchVelocity(new Vec3(float.NaN, 0, 0), underwater: false, indoors: false);
        Assert.False(float.IsNaN(bad.X) || float.IsNaN(bad.Y) || float.IsNaN(bad.Z));
    }

    [Theory]
    [InlineData(0.23f, 1f, 0.32f, 1f)]
    [InlineData(0.645f, 0.045f, 0.355f, 1f)]
    [InlineData(0.32f, 0f, 0.67f, 0f)]
    public void EasingCurvesStartAndEndInPlaceAndOnlyGoForward(float x1, float y1, float x2, float y2)
    {
        Assert.Equal(0f, Ease.Bezier(x1, y1, x2, y2, 0f));
        Assert.Equal(1f, Ease.Bezier(x1, y1, x2, y2, 1f));
        float last = 0f;
        for (int i = 1; i < 100; i++)
        {
            float y = Ease.Bezier(x1, y1, x2, y2, i / 100f);
            Assert.True(y >= last - 1e-4f);
            last = y;
        }
        Assert.True(Ease.OutQuint(0.2f) > 0.5f, "ease-out is fast at the start");
        Assert.True(Ease.InCubic(0.2f) < 0.1f, "ease-in is slow at the start");
    }

    [Fact]
    public void SwingKeysAreInOrderAndHitInFront()
    {
        var keys = BatSwing.Keys;
        for (int i = 1; i < keys.Length; i++) Assert.True(keys[i].T > keys[i - 1].T);
        Assert.Equal(BatSwing.Seconds, keys[keys.Length - 1].T);
        Assert.Contains(keys, k => k.T == BatSwing.Contact);
        Assert.True(BatSwing.StrikeStart < BatSwing.Contact);
        var contact = keys.First(k => k.T == BatSwing.Contact);
        Assert.True(contact.Bat.Z > 0.8f, "at contact the bat points forward, at what you're looking at");
        Assert.True(BatSwing.Contact < 0.35f, "a user-started action: the hit lands fast");
        var (_, to, f) = BatSwing.At(BatSwing.Contact);
        Assert.Equal(BatSwing.Contact, to.T);
        Assert.Equal(1.0, (double)f, 3);
        // the way back is shorter than the swing out (exits faster than entrances)
        Assert.True(BatSwing.Seconds - 0.40f < 0.40f);
    }

    [Fact]
    public void PacketsRoundTrip()
    {
        var hit = new BatHitPacket { HitterId = 3, Targets = new List<int> { 4, 7 }, Direction = new Vec3(0, 0.5f, 1) };
        var ms = new MemoryStream();
        hit.Write(new BinaryWriter(ms));
        ms.Position = 0;
        var back = new BatHitPacket();
        back.Read(new BinaryReader(ms));
        Assert.Equal(3, back.HitterId);
        Assert.Equal(new[] { 4, 7 }, back.Targets);
        Assert.Equal(0.5f, back.Direction.Y);
        Assert.IsType<BatHitPacket>(Packet.Create(PacketType.BatHit));
        Assert.IsType<BatSwingPacket>(Packet.Create(PacketType.BatSwing));
    }

    [Fact]
    public void BatHitsNeedTheBatAndLaunchEachTargetOnce()
    {
        var server = new NetServer();
        server.Start(0);
        try
        {
            var a = Join(server, "A");
            var b = Join(server, "B");
            var c = Join(server, "C");
            WaitFor<PlayerJoinedPacket>(a, p => p.Name == "C");

            // no bat in hand: nothing
            a.Send(new BatHitPacket { Targets = new List<int> { b.LocalId }, Direction = new Vec3(0, 0, 1) });
            Assert.DoesNotContain(Drain(b, 400), p => p is BatHitPacket);

            Hold(a, Bat.TechName);
            WaitFor<PlayerStatePacket>(b, p => p.Held == Bat.TechName);
            a.Send(new BatHitPacket { Targets = new List<int> { b.LocalId, b.LocalId, a.LocalId, c.LocalId, 9999 }, Direction = new Vec3(0, 3, 4) });
            var got = WaitFor<BatHitPacket>(c);
            Assert.Equal(a.LocalId, got.HitterId);
            Assert.Equal(new[] { b.LocalId, c.LocalId }, got.Targets); // no doubles, not yourself, nobody unknown
            Assert.Equal(0.6, (double)got.Direction.Y, 3);           // normalized
            Assert.Contains(WaitFor<BatHitPacket>(b).Targets, id => id == b.LocalId);
            WaitFor<BatHitPacket>(a); // the hitter hears it too (for the "you launched" line)

            // they're flying now: can't be hit again until they're up
            Thread.Sleep((int)Bat.SwingCooldownMs + 50);
            a.Send(new BatHitPacket { Targets = new List<int> { b.LocalId }, Direction = new Vec3(0, 0, 1) });
            Assert.DoesNotContain(Drain(b, 400), p => p is BatHitPacket);
        }
        finally { server.Stop(); }
    }

    [Fact]
    public void SwingsReachEveryoneElse()
    {
        var server = new NetServer();
        server.Start(0);
        try
        {
            var a = Join(server, "A");
            var b = Join(server, "B");
            WaitFor<PlayerJoinedPacket>(a, p => p.Name == "B");
            a.Send(new BatSwingPacket { Pitch = 45f });
            var got = WaitFor<BatSwingPacket>(b);
            Assert.Equal(a.LocalId, got.Id);
            Assert.Equal(45f, got.Pitch);
            Assert.DoesNotContain(Drain(a, 300), p => p is BatSwingPacket);
        }
        finally { server.Stop(); }
    }
}
