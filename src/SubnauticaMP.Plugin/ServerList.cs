using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SubnauticaMP
{
    // Servers you've added or joined, newest first. BepInEx\plugins\SubnauticaMP\servers.txt (name|address per line)
    internal static class ServerList
    {
        public sealed class Entry
        {
            public string Name;
            public string Address;
        }

        static string FilePath => Path.Combine(Plugin.Folder, "servers.txt");

        public static List<Entry> Load()
        {
            var list = new List<Entry>();
            try
            {
                if (!File.Exists(FilePath)) return list;
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    int bar = line.LastIndexOf('|');
                    if (bar <= 0 || bar == line.Length - 1) continue;
                    list.Add(new Entry { Name = line.Substring(0, bar), Address = line.Substring(bar + 1) });
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Couldn't read servers.txt: " + e.Message); }
            return list;
        }

        static void Save(List<Entry> list)
        {
            try { File.WriteAllLines(FilePath, list.Select(e => e.Name.Replace("|", "/") + "|" + e.Address).ToArray()); }
            catch (Exception e) { Plugin.Log.LogWarning("Couldn't write servers.txt: " + e.Message); }
        }

        // Adds the server (or moves it to the top). Keeps an existing custom name unless a new one is given.
        public static void Remember(string address, string name = null)
        {
            if (string.IsNullOrWhiteSpace(address) || address.StartsWith("127.0.0.1")) return;
            var list = Load();
            var existing = list.FirstOrDefault(e => string.Equals(e.Address, address, StringComparison.OrdinalIgnoreCase));
            list.Remove(existing);
            var entry = existing ?? new Entry { Address = address.Trim(), Name = address.Trim() };
            if (!string.IsNullOrWhiteSpace(name)) entry.Name = name.Trim();
            list.Insert(0, entry);
            Save(list);
        }

        public static void Remove(string address)
        {
            var list = Load();
            list.RemoveAll(e => string.Equals(e.Address, address, StringComparison.OrdinalIgnoreCase));
            Save(list);
        }
    }
}
