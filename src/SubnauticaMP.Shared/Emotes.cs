using System;
using System.IO;
using System.Linq;

namespace SubnauticaMP.Shared
{
    // Emotes (G in game, or "/e wave" in chat like Roblox). Only the number goes over the network;
    // every player's game animates the diver itself.
    public enum Emote : byte
    {
        None = 0, // stop whatever emote is playing
        Wave, Point, Cheer, Dance, Laugh, Nod, No, Salute, Facepalm, Shrug, Clap, Flip, Spin, Chill,
    }

    public static class Emotes
    {
        public sealed class Info
        {
            public Emote Emote;
            public string Name;      // what you type after /e
            public string Label;     // button text
            public string Did;       // "Alex waves"
            public float Seconds;    // how long it plays; 0 = keeps going until you move
            public string[] Aliases;
        }

        public static readonly Info[] All =
        {
            new Info { Emote = Emote.Wave, Name = "wave", Label = "Wave", Did = "waves", Seconds = 2.6f, Aliases = new[] { "hi", "hello", "bye" } },
            new Info { Emote = Emote.Point, Name = "point", Label = "Point", Did = "points", Seconds = 2.5f, Aliases = new[] { "look" } },
            new Info { Emote = Emote.Cheer, Name = "cheer", Label = "Cheer", Did = "cheers", Seconds = 2.5f, Aliases = new[] { "yay", "gg" } },
            new Info { Emote = Emote.Dance, Name = "dance", Label = "Dance", Did = "is dancing", Seconds = 0f, Aliases = new[] { "dance1", "dance2", "dance3" } },
            new Info { Emote = Emote.Laugh, Name = "laugh", Label = "Laugh", Did = "laughs", Seconds = 2.4f, Aliases = new[] { "lol", "haha" } },
            new Info { Emote = Emote.Nod, Name = "nod", Label = "Nod (yes)", Did = "nods", Seconds = 1.6f, Aliases = new[] { "yes", "ok" } },
            new Info { Emote = Emote.No, Name = "no", Label = "Shake head (no)", Did = "shakes their head", Seconds = 1.6f, Aliases = new[] { "nope", "shake" } },
            new Info { Emote = Emote.Salute, Name = "salute", Label = "Salute", Did = "salutes", Seconds = 2.4f, Aliases = new string[0] },
            new Info { Emote = Emote.Facepalm, Name = "facepalm", Label = "Facepalm", Did = "facepalms", Seconds = 2.4f, Aliases = new[] { "bruh" } },
            new Info { Emote = Emote.Shrug, Name = "shrug", Label = "Shrug", Did = "shrugs", Seconds = 1.8f, Aliases = new[] { "idk" } },
            new Info { Emote = Emote.Clap, Name = "clap", Label = "Clap", Did = "claps", Seconds = 2.6f, Aliases = new[] { "applaud" } },
            new Info { Emote = Emote.Flip, Name = "flip", Label = "Backflip", Did = "does a backflip", Seconds = 1.3f, Aliases = new[] { "backflip" } },
            new Info { Emote = Emote.Spin, Name = "spin", Label = "Spin", Did = "spins", Seconds = 1.3f, Aliases = new string[0] },
            new Info { Emote = Emote.Chill, Name = "chill", Label = "Chill", Did = "is chilling", Seconds = 0f, Aliases = new[] { "relax", "float" } },
        };

        public static Info Get(Emote e) => All.FirstOrDefault(i => i.Emote == e);

        public static bool IsValid(Emote e) => e == Emote.None || Get(e) != null;

        public static bool Loops(Emote e) => Get(e) is Info i && i.Seconds <= 0f;

        // "wave", "Wave", "hi" -> Wave
        public static Emote Find(string name)
        {
            name = (name ?? "").Trim().ToLowerInvariant();
            if (name.Length == 0) return Emote.None;
            foreach (var i in All)
                if (i.Name == name || i.Aliases.Contains(name)) return i.Emote;
            return Emote.None;
        }

        public static string List() => string.Join(", ", All.Select(i => i.Name).ToArray());

        // Chat text that means "do an emote": "/e wave", "/emote wave", "/wave".
        // Returns false for normal chat / other commands. listOnly = "/e" or "/emotes" on its own.
        public static bool TryParseCommand(string text, out Emote emote, out bool listOnly)
        {
            emote = Emote.None;
            listOnly = false;
            text = (text ?? "").Trim();
            if (!text.StartsWith("/")) return false;
            var parts = text.Substring(1).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return false;
            var cmd = parts[0].ToLowerInvariant();
            if (cmd == "e" || cmd == "emote" || cmd == "emotes")
            {
                if (parts.Length < 2) { listOnly = true; return true; }
                var arg = parts[1].ToLowerInvariant();
                if (arg == "stop") return true; // Emote.None
                emote = Find(arg);
                listOnly = emote == Emote.None;
                return true;
            }
            emote = Find(cmd);
            return emote != Emote.None && parts.Length == 1;
        }
    }

    // client -> server: I'm doing this emote (None = stopped). server -> everyone else: player Id is doing it.
    // Looping emotes (dance, chill) get sent again every few seconds, so people who join late see them too.
    public sealed class EmotePacket : Packet
    {
        public int Id;
        public Emote Emote;
        public override PacketType Type => PacketType.Emote;
        public override void Write(BinaryWriter w) { w.Write(Id); w.Write((byte)Emote); }
        public override void Read(BinaryReader r) { Id = r.ReadInt32(); Emote = (Emote)r.ReadByte(); }
    }
}
