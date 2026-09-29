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
        static string ModPath(string dir) => Path.Combine(PluginDir(dir), "SubnauticaMP.dll");

        // ---------- mod ----------

        public static Version BundledModVersion => typeof(GameFolder).Assembly.GetName().Version;

        public static Version InstalledModVersion(string dir)
        {
            try { return File.Exists(ModPath(dir)) ? AssemblyName.GetAssemblyName(ModPath(dir)).Version : null; }
            catch { return null; }
        }

        // Copies the mod DLL packed inside this exe into BepInEx\plugins.
        public static void InstallMod(string dir)
        {
            Directory.CreateDirectory(PluginDir(dir));
            using var res = typeof(GameFolder).Assembly.GetManifestResourceStream("SubnauticaMP.dll")
                            ?? throw new InvalidOperationException("Mod is missing from this launcher build");
            try
            {
                using var file = File.Create(ModPath(dir));
                res.CopyTo(file);
            }
            catch (IOException)
            {
                throw new IOException("Couldn't update the mod file. Close Subnautica first, then try again.");
            }
        }

        public static void WriteLaunchInfo(string dir, string name, string host, int port)
        {
            new Shared.LaunchInfo { PlayerName = name, Host = host, Port = port }
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
    }
}
