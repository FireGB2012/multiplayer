using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.IO.Compression;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SubnauticaMP.Launcher
{
    // Finding Subnautica, checking/installing BepInEx and the mod.
    internal static class GameFolder
    {
        internal const string Exe = "Subnautica.exe";

        static readonly string[] BepInExUrls =
        {
            "https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.2/BepInEx_win_x64_5.4.23.2.zip",
            "https://github.com/BepInEx/BepInEx/releases/download/v5.4.22/BepInEx_x64_5.4.22.0.zip",
        };

        public static bool IsGameDir(string dir) => !string.IsNullOrWhiteSpace(dir) && File.Exists(Path.Combine(dir, Exe));
        public static bool HasBepInEx(string dir) =>
            File.Exists(Path.Combine(dir, "BepInEx", "core", "BepInEx.dll")) && File.Exists(Path.Combine(dir, "winhttp.dll"));
        public static string PluginDir(string dir) => Path.Combine(dir, "BepInEx", "plugins", "SubnauticaMP");
        public static string OptimizerDir(string dir) => Path.Combine(dir, "BepInEx", "plugins", "SubnauticaOptimizer");
        public static bool HasOptimizer(string dir) => File.Exists(Path.Combine(OptimizerDir(dir), "SubnauticaOptimizer.dll"));
        public static bool HasNautilus(string dir) => File.Exists(Path.Combine(dir, "BepInEx", "plugins", "Nautilus", "Nautilus.dll"));
        static string ModPath(string dir) => Path.Combine(PluginDir(dir), "SubnauticaMP.dll");

        // ---------- mod ----------

        public static Version BundledModVersion => typeof(GameFolder).Assembly.GetName().Version;

        public static Version InstalledModVersion(string dir)
        {
            try { return File.Exists(ModPath(dir)) ? AssemblyName.GetAssemblyName(ModPath(dir)).Version : null; }
            catch { return null; }
        }

        // The Steam version (has Steam's DLL next to the game).
        public static bool IsSteamCopy(string dir)
        {
            try
            {
                var plugins = Path.Combine(dir, "Subnautica_Data", "Plugins");
                return Directory.Exists(plugins) && Directory.GetFiles(plugins, "steam_api*.dll", SearchOption.AllDirectories).Length > 0;
            }
            catch { return false; }
        }

        // Without this file the Steam version closes itself when started directly and Steam opens its own copy
        // instead: if that isn't the folder the mod is in, the game comes back without the mod.
        public static void WriteSteamAppId(string dir)
        {
            var path = Path.Combine(dir, "steam_appid.txt");
            try { if (!File.Exists(path) || File.ReadAllText(path).Trim() != "264710") File.WriteAllText(path, "264710"); }
            catch { }
        }

        // Copies the mods packed inside this exe into BepInEx\plugins: the multiplayer mod and the optimizer.
        public static void InstallMod(string dir)
        {
            Directory.CreateDirectory(PluginDir(dir));
            Directory.CreateDirectory(OptimizerDir(dir));
            foreach (var (name, folder) in new[] { ("SubnauticaMP.dll", PluginDir(dir)), ("Mono.Nat.dll", PluginDir(dir)), ("SubnauticaOptimizer.dll", OptimizerDir(dir)) })
            {
                using var res = typeof(GameFolder).Assembly.GetManifestResourceStream(name)
                                ?? throw new InvalidOperationException(name + " is missing from this launcher build");
                try
                {
                    using var file = File.Create(Path.Combine(folder, name));
                    res.CopyTo(file);
                }
                catch (IOException)
                {
                    throw new IOException("Couldn't update the mod files. Close Subnautica first, then try again.");
                }
            }
        }

        public static void ClearLaunchInfo(string dir)
        {
            try { File.Delete(Path.Combine(PluginDir(dir), Shared.LaunchInfo.FileName)); } catch { }
        }

        const string LocalServerFile = "local_server.txt";

        public static void WriteLocalServer(string dir, int port, string world)
        {
            try
            {
                Directory.CreateDirectory(PluginDir(dir));
                File.WriteAllLines(Path.Combine(PluginDir(dir), LocalServerFile), new[] { "port=" + port, "world=" + world });
            }
            catch { }
        }

        public static void ClearLocalServer(string dir)
        {
            try { if (IsGameDir(dir)) File.Delete(Path.Combine(PluginDir(dir), LocalServerFile)); } catch { }
        }

        public static void WriteLaunchInfo(string dir, string name, string host, int port, string password = "")
        {
            new Shared.LaunchInfo { PlayerName = name, Host = host, Port = port, Password = password ?? "" }
                .Save(Path.Combine(PluginDir(dir), Shared.LaunchInfo.FileName));
        }

        // ---------- BepInEx ----------

        public static async Task InstallBepInEx(string dir, IProgress<string> progress)
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("SubnauticaMP-Launcher");
            Exception last = null;
            foreach (var url in BepInExUrls)
            {
                try
                {
                    progress.Report("Downloading BepInEx...");
                    var bytes = await http.GetByteArrayAsync(url);
                    progress.Report("Unpacking BepInEx...");
                    using var zip = new ZipArchive(new MemoryStream(bytes));
                    foreach (var entry in zip.Entries)
                    {
                        var target = Path.GetFullPath(Path.Combine(dir, entry.FullName));
                        if (!target.StartsWith(Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase)) continue; // zip-slip guard
                        if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(target); continue; }
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        entry.ExtractToFile(target, overwrite: true);
                    }
                    Directory.CreateDirectory(Path.Combine(dir, "BepInEx", "core"));
                    Directory.CreateDirectory(Path.Combine(dir, "BepInEx", "plugins"));
                    return;
                }
                catch (Exception e) { last = e; }
            }
            throw new Exception("Couldn't download BepInEx: " + last?.Message);
        }

        // ---------- Nautilus (the modding library most Subnautica mods need) ----------

        const string NautilusReleases = "https://api.github.com/repos/SubnauticaModding/Nautilus/releases?per_page=10";
        const string NautilusFallback = "https://github.com/SubnauticaModding/Nautilus/releases/download/1.0.0-pre.54/Nautilus_SN.STABLE_1.0.0.54.zip";

        // Puts the newest Subnautica (not Below Zero) build of Nautilus into BepInEx\plugins\Nautilus.
        public static async Task InstallNautilus(string dir, IProgress<string> progress)
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("SubnauticaMP-Launcher");
            var urls = new System.Collections.Generic.List<string>();
            try
            {
                progress.Report("Looking up the newest Nautilus...");
                var json = await http.GetStringAsync(NautilusReleases);
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(json,
                             "\"browser_download_url\"\\s*:\\s*\"([^\"]*Nautilus_SN\\.STABLE[^\"]*\\.zip)\""))
                    urls.Add(m.Groups[1].Value);
            }
            catch { } // GitHub API down / rate limited: use the known build
            if (urls.Count > 1) urls.RemoveRange(1, urls.Count - 1); // newest only
            urls.Add(NautilusFallback);

            Exception last = null;
            foreach (var url in urls)
            {
                try
                {
                    progress.Report("Downloading Nautilus...");
                    var bytes = await http.GetByteArrayAsync(url);
                    progress.Report("Unpacking Nautilus...");
                    ExtractNautilus(bytes, dir);
                    return;
                }
                catch (Exception e) { last = e; }
            }
            throw new Exception("Couldn't download Nautilus: " + last?.Message);
        }

        // The zip holds "plugins/Nautilus/Nautilus.dll" (and friends): everything goes under BepInEx.
        internal static void ExtractNautilus(byte[] zipBytes, string dir)
        {
            var bepinex = Path.GetFullPath(Path.Combine(dir, "BepInEx"));
            using var zip = new ZipArchive(new MemoryStream(zipBytes));
            bool any = false;
            foreach (var entry in zip.Entries)
            {
                var name = entry.FullName.Replace('\\', '/');
                if (name.StartsWith("BepInEx/", StringComparison.OrdinalIgnoreCase)) name = name.Substring(8);
                if (!name.StartsWith("plugins/", StringComparison.OrdinalIgnoreCase)) continue;
                var target = Path.GetFullPath(Path.Combine(bepinex, name));
                if (!target.StartsWith(bepinex, StringComparison.OrdinalIgnoreCase)) continue; // zip-slip guard
                if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(target); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                entry.ExtractToFile(target, overwrite: true);
                any = true;
            }
            if (!any) throw new InvalidDataException("That zip doesn't have Nautilus in it");
        }
    }
}
