using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SubnauticaMP.Shared
{
    // A content mod (one built on Nautilus / SMLHelper) a player has installed.
    public sealed class ModInfo
    {
        public string Guid;
        public string Name;
        public string Version;

        public override string ToString() => string.IsNullOrEmpty(Version) ? Name : $"{Name} {Version}";

        public void Write(BinaryWriter w) { w.Write(Guid ?? ""); w.Write(Name ?? ""); w.Write(Version ?? ""); }
        public static ModInfo Read(BinaryReader r) => new ModInfo { Guid = r.ReadString(), Name = r.ReadString(), Version = r.ReadString() };

        public static void WriteList(BinaryWriter w, List<ModInfo> mods)
        {
            w.Write(mods.Count);
            foreach (var m in mods) m.Write(w);
        }

        public static List<ModInfo> ReadList(BinaryReader r)
        {
            int n = r.ReadInt32();
            if (n < 0 || n > 2000) throw new InvalidDataException("Bad mod count");
            var list = new List<ModInfo>(n);
            for (int i = 0; i < n; i++) list.Add(Read(r));
            return list;
        }

        // Modded TechTypes: name -> number (Nautilus gives them numbers per PC; they must match for items to sync).
        public static void WriteMap(BinaryWriter w, Dictionary<string, int> map)
        {
            w.Write(map.Count);
            foreach (var kv in map) { w.Write(kv.Key); w.Write(kv.Value); }
        }

        public static Dictionary<string, int> ReadMap(BinaryReader r)
        {
            int n = r.ReadInt32();
            if (n < 0 || n > 100000) throw new InvalidDataException("Bad map size");
            var map = new Dictionary<string, int>(n, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < n; i++) map[r.ReadString()] = r.ReadInt32();
            return map;
        }

        // Nautilus's cache after taking the world's numbers: the world's items get exactly its numbers, and any
        // other cached item whose number is now taken moves to a free one (so Nautilus never sees a clash).
        public static (Dictionary<string, int> active, Dictionary<string, int> off) MergeCache(
            Dictionary<string, int> active, Dictionary<string, int> off, Dictionary<string, int> world)
        {
            var taken = new HashSet<int>(world.Values);
            int next = new[] { active.Values.DefaultIfEmpty(0).Max(), off.Values.DefaultIfEmpty(0).Max(), world.Values.DefaultIfEmpty(0).Max() }.Max() + 1;

            var newActive = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in world) newActive[kv.Key] = kv.Value;
            foreach (var kv in active)
            {
                if (newActive.ContainsKey(kv.Key)) continue;
                int n = taken.Contains(kv.Value) ? next++ : kv.Value;
                taken.Add(n);
                newActive[kv.Key] = n;
            }
            var newOff = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in off)
            {
                if (newActive.ContainsKey(kv.Key)) continue;
                int n = taken.Contains(kv.Value) ? next++ : kv.Value;
                taken.Add(n);
                newOff[kv.Key] = n;
            }
            return (newActive, newOff);
        }

        // Modded item names whose numbers differ between the two lists.
        public static List<string> Mismatches(Dictionary<string, int> world, Dictionary<string, int> player) =>
            world.Where(kv => player.TryGetValue(kv.Key, out var n) && n != kv.Value).Select(kv => kv.Key).ToList();
    }

    // server -> player, just before turning them away: the world's modded item numbers, so the player's
    // game can fix its Nautilus cache and join after a restart.
    public sealed class ModFixPacket : Packet
    {
        public Dictionary<string, int> TechTypes = new Dictionary<string, int>();
        public override PacketType Type => PacketType.ModFix;
        public override void Write(BinaryWriter w) => ModInfo.WriteMap(w, TechTypes);
        public override void Read(BinaryReader r) => TechTypes = ModInfo.ReadMap(r);
    }
}
