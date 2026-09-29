using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SubnauticaMP
{
    // Servers you've added or joined, newest first. BepInEx\plugins\SubnauticaMP\servers.txt (name|address|password per line)
    internal static class ServerList
    {
        public sealed class Entry
        {
            public string Name;
            public string Address;
            public string Password = "";
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
                    var parts = line.Split('|');
                    if (parts.Length < 2 || parts[0].Length == 0 || parts[1].Length == 0) continue;
                    list.Add(new Entry { Name = parts[0], Address = parts[1], Password = parts.Length > 2 ? string.Join("|", parts.Skip(2).ToArray()) : "" });
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Couldn't read servers.txt: " + e.Message); }
            return list;
        }

        static void Save(List<Entry> list)
        {
            try { File.WriteAllLines(FilePath, list.Select(e => e.Name.Replace("|", "/") + "|" + e.Address + "|" + (e.Password ?? "")).ToArray()); }
            catch (Exception e) { Plugin.Log.LogWarning("Couldn't write servers.txt: " + e.Message); }
        }

        // Adds the server (or moves it to the top). Keeps an existing custom name unless a new one is given.
        public static Entry Find(string address) =>
            Load().FirstOrDefault(e => string.Equals(e.Address, (address ?? "").Trim(), StringComparison.OrdinalIgnoreCase));

        public static void Remember(string address, string name = null, string password = null)
        {
            if (string.IsNullOrWhiteSpace(address) || address.StartsWith("127.0.0.1")) return;
            var list = Load();
            var existing = list.FirstOrDefault(e => string.Equals(e.Address, address, StringComparison.OrdinalIgnoreCase));
            list.Remove(existing);
            var entry = existing ?? new Entry { Address = address.Trim(), Name = address.Trim() };
            if (!string.IsNullOrWhiteSpace(name)) entry.Name = name.Trim();
            if (password != null) entry.Password = password;
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
