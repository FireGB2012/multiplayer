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
        Party = 15, // dancing along at a party: the dance comes from the party's shared clock
        Shove = 16,   // the push (empty hand + click on someone)
        Knocked = 17, // got pushed: ragdoll, then stand up
        // 40 and up: the motion-captured / hand-made clips in EmoteCatalog.g.cs, in order
    }

    public static partial class Emotes
    {
        public const int ClipBase = 40;

        public sealed class Info
        {
            public Emote Emote;
            public string Name;      // what you type after /e
            public string Label;     // button text
            public string Did;       // "Alex waves"
            public float Seconds;    // how long it plays; 0 = keeps going until you move
            public string[] Aliases;
            public string Category = "Gestures";
            public string Clip;      // animation clip name, or null for the posed-by-code ones
        }

        static readonly Info[] Posed =
        {
            new Info { Emote = Emote.Wave, Name = "wave", Label = "Wave", Did = "waves", Seconds = 2.6f, Aliases = new[] { "hi", "hello", "bye" } },
            new Info { Emote = Emote.Point, Name = "point", Label = "Point", Did = "points", Seconds = 2.5f, Aliases = new[] { "look" } },
            new Info { Emote = Emote.Cheer, Name = "cheer", Label = "Cheer", Did = "cheers", Seconds = 2.5f, Aliases = new[] { "yay", "gg" } },
            new Info { Emote = Emote.Dance, Name = "dance", Label = "Bounce", Did = "is dancing", Seconds = 0f, Aliases = new[] { "dance1", "bounce" }, Category = "Dances" },
            new Info { Emote = Emote.Laugh, Name = "laugh", Label = "Laugh", Did = "laughs", Seconds = 2.4f, Aliases = new[] { "lol", "haha" } },
            new Info { Emote = Emote.Nod, Name = "nod", Label = "Nod (yes)", Did = "nods", Seconds = 1.6f, Aliases = new[] { "yes", "ok" } },
            new Info { Emote = Emote.No, Name = "no", Label = "Shake head (no)", Did = "shakes their head", Seconds = 1.6f, Aliases = new[] { "nope", "shake" } },
            new Info { Emote = Emote.Salute, Name = "salute", Label = "Salute", Did = "salutes", Seconds = 2.4f, Aliases = new string[0] },
            new Info { Emote = Emote.Facepalm, Name = "facepalm", Label = "Facepalm", Did = "facepalms", Seconds = 2.4f, Aliases = new[] { "bruh" } },
            new Info { Emote = Emote.Shrug, Name = "shrug", Label = "Shrug", Did = "shrugs", Seconds = 1.8f, Aliases = new[] { "idk" } },
            new Info { Emote = Emote.Clap, Name = "clap", Label = "Clap", Did = "claps", Seconds = 2.6f, Aliases = new[] { "applaud" } },
            new Info { Emote = Emote.Flip, Name = "flip", Label = "Backflip", Did = "does a backflip", Seconds = 1.3f, Aliases = new[] { "backflip" }, Category = "Fun" },
            new Info { Emote = Emote.Spin, Name = "spin", Label = "Spin", Did = "spins", Seconds = 1.3f, Aliases = new string[0], Category = "Fun" },
            new Info { Emote = Emote.Chill, Name = "chill", Label = "Chill", Did = "is chilling", Seconds = 0f, Aliases = new[] { "relax", "float" }, Category = "Poses" },
            new Info { Emote = Emote.Shove, Name = "shove", Label = "Shove", Did = "shoves", Seconds = 0.6f, Aliases = new string[0], Category = SystemCategory },
            new Info { Emote = Emote.Knocked, Name = "knocked", Label = "Knocked over", Did = "got knocked over", Seconds = KnockedSeconds, Aliases = new string[0], Category = SystemCategory },
            new Info { Emote = Emote.Party, Name = "party", Label = "Party!", Did = "joined the party", Seconds = 0f, Aliases = new[] { "rave", "disco" }, Category = "Party" },
        };

        static Info[] _all;

        // Every emote: the posed ones, then the clips.
        public static Info[] All
        {
            get
            {
                if (_all != null) return _all;
                var list = new System.Collections.Generic.List<Info>(Posed);
                for (int i = 0; i < Clips.Length; i++)
                {
                    var c = Clips[i];
                    list.Add(new Info
                    {
                        Emote = (Emote)(ClipBase + i), Name = c.name, Label = c.label, Did = c.did,
                        Seconds = c.loops ? 0f : c.seconds, Aliases = new string[0], Category = c.category, Clip = c.name,
                    });
                }
                return _all = list.ToArray();
            }
        }

        // The party dance for a moment in a party: every player's game picks the same one.
        public static Info PartyDance(double partyTime, int seed)
        {
            var dances = All.Where(i => i.Category == "Dances" && i.Clip != null).ToArray();
            int slot = (int)Math.Floor(Math.Max(0, partyTime) / PartySongSeconds);
            int pick = (int)((uint)(seed * 31 + slot * 7919) % (uint)dances.Length);
            return dances[pick];
        }

        public const double PartySongSeconds = 16;

        // Default emote wheel (8 slots, clockwise from the top).
        public static readonly string[] DefaultWheel = { "wave", "floss", "robot", "breakdance", "worm", "dance", "flip", "laugh" };

        static System.Collections.Generic.Dictionary<Emote, Info> _byId;

        public static Info Get(Emote e)
        {
            if (_byId == null) _byId = All.ToDictionary(i => i.Emote);
            return _byId.TryGetValue(e, out var info) ? info : null;
        }

        public static bool IsValid(Emote e) => e == Emote.None || Get(e) != null;

        public static bool Loops(Emote e) => Get(e) is Info i && i.Seconds <= 0f;

        public static Info Named(string name) => All.FirstOrDefault(i => i.Name == name);

        // "wave", "Wave", "hi" -> Wave
        public const string SystemCategory = "System"; // pushes: not in the wheel or chat, only happen by pushing
        public const float KnockedSeconds = 4f;

        public static bool Pickable(Info i) => i.Category != SystemCategory;

        public static Emote Find(string name)
        {
            name = (name ?? "").Trim().ToLowerInvariant();
            if (name.Length == 0) return Emote.None;
            foreach (var i in All.Where(Pickable))
                if (i.Name == name || i.Aliases.Contains(name)) return i.Emote;
            return Emote.None;
        }

        public static string List() => string.Join(", ", All.Where(Pickable).Select(i => i.Name).ToArray());

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
    // StartTime puts everyone on the same frame: a late joiner (or a slow packet) jumps in where the dance is now.
    public sealed class EmotePacket : Packet
    {
        public int Id;
        public Emote Emote;
        public double StartTime = double.NaN; // game clock (DayNightCycle.timePassed) when it started; NaN = just now
        public override PacketType Type => PacketType.Emote;
        public override void Write(BinaryWriter w) { w.Write(Id); w.Write((byte)Emote); w.Write(StartTime); }
        public override void Read(BinaryReader r) { Id = r.ReadInt32(); Emote = (Emote)r.ReadByte(); StartTime = r.ReadDouble(); }
    }

    // A dance party: whoever joins dances, all to the same dance at the same moment (picked from the party's
    // start time + seed, switching every PartySongSeconds). client -> server: start (Active) / end it.
    // server -> everyone: the party now (LeaderId = who started it). Only one party at a time.
    public sealed class PartyPacket : Packet
    {
        public int LeaderId;
        public bool Active;
        public Vec3 Center;
        public double StartTime; // game clock (DayNightCycle.timePassed), the same on every PC
        public int Seed;
        public override PacketType Type => PacketType.Party;
        public override void Write(BinaryWriter w) { w.Write(LeaderId); w.Write(Active); Center.Write(w); w.Write(StartTime); w.Write(Seed); }
        public override void Read(BinaryReader r) { LeaderId = r.ReadInt32(); Active = r.ReadBoolean(); Center = Vec3.Read(r); StartTime = r.ReadDouble(); Seed = r.ReadInt32(); }
    }

    // Empty hand + click on a teammate: they get shoved. client -> server: Target + Direction.
    // server -> everyone: PusherId pushed TargetId that way (the target's own game moves them).
    public sealed class PushPacket : Packet
    {
        public int PusherId;
        public int TargetId;
        public Vec3 Direction;
        public override PacketType Type => PacketType.Push;
        public override void Write(BinaryWriter w) { w.Write(PusherId); w.Write(TargetId); Direction.Write(w); }
        public override void Read(BinaryReader r) { PusherId = r.ReadInt32(); TargetId = r.ReadInt32(); Direction = Vec3.Read(r); }
    }
}
