using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using SubnauticaMP.Shared;

namespace SubnauticaMP.Tests;

public class EmoteTests
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

    // Everything that arrives within `ms`.
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

    static (NetClient, WelcomePacket) Join(NetServer server, string name)
    {
        var c = new NetClient();
        c.Connect("127.0.0.1", server.Port, name);
        return (c, WaitFor<WelcomePacket>(c));
    }

    [Theory]
    [InlineData("/e wave", Emote.Wave, false)]
    [InlineData("/E Wave", Emote.Wave, false)]
    [InlineData("/emote dance", Emote.Dance, false)]
    [InlineData("/wave", Emote.Wave, false)]
    [InlineData("/hi", Emote.Wave, false)]
    [InlineData("/e dance1", Emote.Dance, false)]
    [InlineData("/backflip", Emote.Flip, false)]
    [InlineData("/party", Emote.Party, false)]
    [InlineData("/e stop", Emote.None, false)]
    [InlineData("/e", Emote.None, true)]
    [InlineData("/emotes", Emote.None, true)]
    [InlineData("/e fortnitecard", Emote.None, true)]
    public void ChatCommandsPickTheRightEmote(string text, Emote expected, bool listOnly)
    {
        Assert.True(Emotes.TryParseCommand(text, out var emote, out var list));
        Assert.Equal(expected, emote);
        Assert.Equal(listOnly, list);
    }

    [Theory]
    [InlineData("wave")]          // plain chat
    [InlineData("/kick bob")]     // server commands
    [InlineData("/help")]
    [InlineData("/wave at bob")]  // not just the emote
    [InlineData("")]
    public void NormalChatIsNotAnEmote(string text)
    {
        Assert.False(Emotes.TryParseCommand(text, out _, out _));
    }

    [Fact]
    public void EveryEmoteHasAUniqueNameAndOnlyDanceAndChillLoop()
    {
        var names = new HashSet<string>();
        foreach (var e in Emotes.All)
        {
            Assert.True(names.Add(e.Name), e.Name);
            foreach (var a in e.Aliases) Assert.True(names.Add(a), a);
            Assert.Equal(e.Emote, Emotes.Find(e.Name));
        }
        Assert.True(Emotes.Loops(Emote.Dance));
        Assert.True(Emotes.Loops(Emote.Chill));
        Assert.False(Emotes.Loops(Emote.Wave));
        foreach (Emote e in Enum.GetValues(typeof(Emote)))
            if (e != Emote.None) Assert.NotNull(Emotes.Get(e)); // every named emote is in the table
        Assert.True(Emotes.All.Length > 35);
    }

    [Fact]
    public void EmotesReachEveryoneElseButCantBeSpammed()
    {
        var server = new NetServer();
        server.Start(0);
        try
        {
            var (a, _) = Join(server, "A");
            var (b, _) = Join(server, "B");
            var (c, _) = Join(server, "C");
            WaitFor<PlayerJoinedPacket>(a, p => p.Name == "C");

            a.Send(new EmotePacket { Emote = Emote.Wave });
            var got = WaitFor<EmotePacket>(b);
            Assert.Equal(a.LocalId, got.Id);
            Assert.Equal(Emote.Wave, got.Emote);
            Assert.Equal(Emote.Wave, WaitFor<EmotePacket>(c).Emote);

            // straight after: too fast, dropped. Stopping always gets through.
            a.Send(new EmotePacket { Emote = Emote.Dance });
            a.Send(new EmotePacket { Emote = (Emote)200 }); // nonsense
            a.Send(new EmotePacket { Emote = Emote.None });
            var burst = Drain(b, 400);
            Assert.DoesNotContain(burst, p => p is EmotePacket e && e.Emote == Emote.Dance);
            Assert.DoesNotContain(burst, p => p is EmotePacket e && e.Emote == (Emote)200);
            Assert.Contains(burst, p => p is EmotePacket e && e.Emote == Emote.None);

            // a bit later it works again
            a.Send(new EmotePacket { Emote = Emote.Dance });
            Assert.Equal(Emote.Dance, WaitFor<EmotePacket>(b).Emote);

            // the sender plays their own; they don't get it echoed back
            Assert.DoesNotContain(Drain(a, 200), p => p is EmotePacket);
        }
        finally { server.Stop(); }
    }

    [Fact]
    public void SlashEInChatBecomesAnEmoteNotAMessage()
    {
        var server = new NetServer();
        server.Start(0);
        try
        {
            var (a, _) = Join(server, "A");
            var (b, _) = Join(server, "B");
            a.Send(new ChatPacket { Text = "/e salute" });
            Assert.Equal(Emote.Salute, WaitFor<EmotePacket>(b).Emote);

            Thread.Sleep(300);
            a.Send(new ChatPacket { Text = "/e" });
            Assert.Contains("wave", WaitFor<ChatPacket>(a, p => p.SenderId == 0).Text);
            Assert.DoesNotContain(Drain(b, 200), p => p is ChatPacket);
        }
        finally { server.Stop(); }
    }

    [Theory]
    [InlineData("/floss", "floss")]
    [InlineData("/e robot", "robot")]
    [InlineData("/e worm", "worm")]
    [InlineData("/e breakdance", "breakdance")]
    [InlineData("/e macarena", "macarena")]
    public void DanceClipsCanBeTyped(string text, string clip)
    {
        Assert.True(Emotes.TryParseCommand(text, out var emote, out _));
        Assert.Equal(clip, Emotes.Get(emote).Clip);
        Assert.True(Emotes.Loops(emote));
    }

    [Fact]
    public void WheelDefaultsAndPartyDancesExist()
    {
        Assert.Equal(8, Emotes.DefaultWheel.Length);
        foreach (var n in Emotes.DefaultWheel) Assert.NotNull(Emotes.Named(n));
        // every PC picks the same dance for the same moment of the same party
        Assert.Equal(Emotes.PartyDance(40, 1234).Name, Emotes.PartyDance(40, 1234).Name);
        Assert.Equal("Dances", Emotes.PartyDance(3, 99).Category);
        var picks = new HashSet<string>();
        for (int i = 0; i < 20; i++) picks.Add(Emotes.PartyDance(i * Emotes.PartySongSeconds, 777).Name);
        Assert.True(picks.Count > 3); // it changes songs
    }

    // The animation file baked into the plugin has exactly the clips the shared catalog lists.
    [Fact]
    public void AnimationFileMatchesTheCatalog()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir, "SubnauticaMP.sln"))) dir = System.IO.Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        var raw = System.IO.File.ReadAllBytes(System.IO.Path.Combine(dir, "src", "SubnauticaMP.Plugin", "Emotes.bin"));
        using var ms = new System.IO.MemoryStream(raw, 2, raw.Length - 2); // skip zlib header
        using var z = new System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionMode.Decompress);
        using var r = new System.IO.BinaryReader(z);
        Assert.Equal("SNEM", new string(r.ReadChars(4)));
        r.ReadByte();
        int count = r.ReadUInt16();
        var clips = Emotes.All.Where(i => i.Clip != null).ToList();
        Assert.Equal(clips.Count, count);
        for (int c = 0; c < count; c++)
        {
            var name = new string(r.ReadChars(r.ReadByte()));
            Assert.Equal(clips[c].Clip, name);
            r.ReadByte();
            int frames = r.ReadUInt16();
            bool loops = r.ReadByte() != 0;
            Assert.Equal(clips[c].Seconds <= 0, loops);
            Assert.True(frames > 20, name);
            r.ReadBytes(frames * (4 + 6 + 27));
        }
    }

    [Fact]
    public void PartiesReachEveryoneIncludingLateJoinersAndEndWithTheirLeader()
    {
        var server = new NetServer();
        server.Start(0);
        try
        {
            var (a, _) = Join(server, "A");
            var (b, _) = Join(server, "B");
            a.Send(new PartyPacket { Active = true, Center = new Vec3(1, 2, 3), StartTime = 500, Seed = 42 });
            var got = WaitFor<PartyPacket>(b);
            Assert.True(got.Active);
            Assert.Equal(a.LocalId, got.LeaderId);
            Assert.Equal(42, got.Seed);
            Assert.Equal(500, got.StartTime);
            Assert.True(WaitFor<PartyPacket>(a).Active); // the DJ hears it back too

            // B can't end A's party
            b.Send(new PartyPacket { Active = false });
            Assert.DoesNotContain(Drain(a, 300), p => p is PartyPacket);

            // someone joining now finds the party going
            var (c, _) = Join(server, "C");
            Assert.Equal(42, WaitFor<PartyPacket>(c).Seed);

            // the DJ leaves: party's over
            a.Disconnect();
            Assert.False(WaitFor<PartyPacket>(b).Active);
            var (d, _) = Join(server, "D");
            Assert.DoesNotContain(Drain(d, 300), p => p is PartyPacket);
        }
        finally { server.Stop(); }
    }
}
