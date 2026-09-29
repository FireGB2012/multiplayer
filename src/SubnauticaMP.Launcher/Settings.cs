using System;
using System.Collections.Generic;
using System.IO;

namespace SubnauticaMP.Launcher
{
    // Remembers your name, game folder etc. in %AppData%\SubnauticaMP\launcher.cfg
    internal sealed class Settings
    {
        public static readonly string Folder =
            Environment.GetEnvironmentVariable("SNMP_DATA_DIR") is string d && d.Length > 0
                ? d
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SubnauticaMP");
        static readonly string FilePath = Path.Combine(Folder, "launcher.cfg");

        public string PlayerName = "";
        public string GameDir = "";
        public string LastJoin = "";
        public string WorldName = "My World";
        public int Port = Shared.Protocol.DefaultPort;

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                var v = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0) v[line.Substring(0, eq)] = line.Substring(eq + 1);
                }
                if (v.TryGetValue("name", out var n)) s.PlayerName = n;
                if (v.TryGetValue("gameDir", out var g)) s.GameDir = g;
                if (v.TryGetValue("lastJoin", out var j)) s.LastJoin = j;
                if (v.TryGetValue("world", out var w) && w.Trim().Length > 0) s.WorldName = w;
                if (v.TryGetValue("port", out var p) && int.TryParse(p, out var port)) s.Port = port;
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllLines(FilePath, new[]
                {
                    "name=" + PlayerName, "gameDir=" + GameDir, "lastJoin=" + LastJoin,
                    "world=" + WorldName, "port=" + Port,
                });
            }
            catch { }
        }
    }
}
