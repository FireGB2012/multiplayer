using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    [InlineData("/e dance2", Emote.Dance, false)]
    [InlineData("/backflip", Emote.Flip, false)]
    [InlineData("/e stop", Emote.None, false)]
    [InlineData("/e", Emote.None, true)]
    [InlineData("/emotes", Emote.None, true)]
    [InlineData("/e moonwalk", Emote.None, true)]
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
        Assert.Equal(Enum.GetValues(typeof(Emote)).Length - 1, Emotes.All.Length); // every emote is in the table
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
}
