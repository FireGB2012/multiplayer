using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace SubnauticaMP.Shared
{
    // The launcher drops this file next to the mod before starting the game;
    // the mod reads it and auto-joins once you're loaded into a save.
    public sealed class LaunchInfo
    {
        public const string FileName = "launch.txt";
        static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(30);

        public string PlayerName;
        public string Host;
        public int Port = Protocol.DefaultPort;
        public string Password = "";
        public DateTime CreatedUtc = DateTime.UtcNow;

        public void Save(string path)
        {
            File.WriteAllLines(path, new[]
            {
                "name=" + (PlayerName ?? "").Replace("\n", " "),
                "host=" + Host,
                "port=" + Port.ToString(CultureInfo.InvariantCulture),
                "password=" + (Password ?? "").Replace("\n", " "),
                "created=" + CreatedUtc.ToString("o", CultureInfo.InvariantCulture),
            });
        }

        // Returns null if the file is missing, broken, or stale (game started some other way later).
        public static LaunchInfo TryLoad(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in File.ReadAllLines(path))
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0) values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }

                var info = new LaunchInfo();
                values.TryGetValue("name", out info.PlayerName);
                values.TryGetValue("host", out info.Host);
                if (values.TryGetValue("password", out var pw)) info.Password = pw;
                if (values.TryGetValue("port", out var p)) int.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out info.Port);
                if (!values.TryGetValue("created", out var c) ||
                    !DateTime.TryParse(c, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out info.CreatedUtc)) return null;

                if (string.IsNullOrEmpty(info.Host) || info.Port <= 0 || info.Port > 65535) return null;
                if (DateTime.UtcNow - info.CreatedUtc.ToUniversalTime() > MaxAge) return null;
                return info;
            }
            catch
            {
                return null;
            }
        }
    }
}
