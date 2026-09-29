using System;
using System.Collections.Generic;
using System.IO;

namespace SubnauticaMP
{
    // Remembers which of your save slots belongs to which multiplayer world, so rejoining
    // loads your save instead of starting over. Stored in BepInEx\plugins\SubnauticaMP\worlds.txt
    internal static class WorldSlots
    {
        static string FilePath => Path.Combine(Plugin.Folder, "worlds.txt");

        static Dictionary<string, string> Load()
        {
            var map = new Dictionary<string, string>();
            try
            {
                if (File.Exists(FilePath))
                    foreach (var line in File.ReadAllLines(FilePath))
                    {
                        int eq = line.IndexOf('=');
                        if (eq > 0) map[line.Substring(0, eq)] = line.Substring(eq + 1);
                    }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Couldn't read worlds.txt: " + e.Message); }
            return map;
        }

        public static HashSet<string> AllSlots() => new HashSet<string>(Load().Values);

        public static string Get(string worldId) =>
            !string.IsNullOrEmpty(worldId) && Load().TryGetValue(worldId, out var slot) ? slot : null;

        public static void Set(string worldId, string slot)
        {
            if (string.IsNullOrEmpty(worldId) || string.IsNullOrEmpty(slot) || slot == "default") return;
            var map = Load();
            if (map.TryGetValue(worldId, out var old) && old == slot) return;
            map[worldId] = slot;
            try
            {
                var lines = new List<string>();
                foreach (var kv in map) lines.Add(kv.Key + "=" + kv.Value);
                File.WriteAllLines(FilePath, lines.ToArray());
            }
            catch (Exception e) { Plugin.Log.LogWarning("Couldn't write worlds.txt: " + e.Message); }
        }
    }
}
