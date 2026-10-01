using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace SubnauticaMP.Launcher
{
    // Tracks down Subnautica on Steam, Epic or Xbox/Game Pass, on any drive.
    internal static class GameFinder
    {
        const string SteamAppId = "264710";

        static bool IsGameDir(string dir) => GameFolder.IsGameDir(dir);

        // Quick check of all the usual places. Returns null if nothing turns up.
        public static string Detect()
        {
            foreach (var dir in Candidates())
            {
                try { if (IsGameDir(dir)) return Path.GetFullPath(dir); }
                catch { }
            }
            return null;
        }

        // Whatever the user picked or pasted (the exe, the game folder, or a folder above it) -> the game folder.
        public static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            path = path.Trim().Trim('"');
            try
            {
                if (File.Exists(path) && Path.GetFileName(path).Equals(GameFolder.Exe, StringComparison.OrdinalIgnoreCase))
                    return Path.GetDirectoryName(path);
                if (IsGameDir(path)) return path;
                if (Directory.Exists(path)) return SearchUnder(new[] { path }, maxDepth: 5, CancellationToken.None, null);
            }
            catch { }
            return null;
        }

        // If the game is running right now, it tells us where it lives.
        public static string FromRunningGame()
        {
            foreach (var p in Process.GetProcessesByName("Subnautica"))
            {
                try
                {
                    var file = p.MainModule?.FileName;
                    if (file != null && IsGameDir(Path.GetDirectoryName(file))) return Path.GetDirectoryName(file);
                }
                catch { } // no access to that process
                finally { p.Dispose(); }
            }
            return null;
        }

        // Microsoft Store installs live in a locked folder that mods can't be copied into.
        public static bool IsLockedStoreInstall(string dir) =>
            dir != null && dir.IndexOf(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase) >= 0;

        // Slow, thorough: walks every drive looking for Subnautica.exe.
        public static string SearchAllDrives(CancellationToken cancel, IProgress<string> progress)
        {
            var roots = DriveInfo.GetDrives()
                .Where(d => { try { return d.IsReady && (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable); } catch { return false; } })
                .Select(d => d.RootDirectory.FullName);
            return SearchUnder(roots, maxDepth: 7, cancel, progress);
        }

        static readonly HashSet<string> SkipDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Windows", "$Recycle.Bin", "System Volume Information", "Recovery", "PerfLogs", "AppData",
            "node_modules", ".git", "WindowsApps", "WinSxS", "Microsoft", "Temp", "proc", "sys", "dev",
            "Subnautica_Data", "BepInEx",
        };

        // Breadth-first so shallow installs are found fast.
        public static string SearchUnder(IEnumerable<string> roots, int maxDepth, CancellationToken cancel, IProgress<string> progress)
        {
            var queue = new Queue<(string dir, int depth)>(roots.Select(r => (r, 0)));
            int seen = 0;
            while (queue.Count > 0)
            {
                cancel.ThrowIfCancellationRequested();
                var (dir, depth) = queue.Dequeue();
                if (IsGameDir(dir)) return dir;
                if (depth >= maxDepth) continue;

                if (++seen % 500 == 0) progress?.Report("Searching... " + dir);
                string[] subs;
                try { subs = Directory.GetDirectories(dir); }
                catch { continue; } // no permission etc.
                foreach (var sub in subs)
                {
                    var name = Path.GetFileName(sub);
                    if (SkipDirs.Contains(name)) continue;
                    try { if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) != 0) continue; } // junction loops
                    catch { continue; }
                    queue.Enqueue((sub, depth + 1));
                }
            }
            return null;
        }

        static IEnumerable<string> Candidates()
        {
            var running = FromRunningGame();
            if (running != null) yield return running;

            foreach (var dir in RegistryInstallDirs()) yield return dir;

            foreach (var steam in SteamRoots())
            {
                yield return Path.Combine(steam, "steamapps", "common", "Subnautica");
                foreach (var vdf in new[] { Path.Combine(steam, "steamapps", "libraryfolders.vdf"), Path.Combine(steam, "config", "libraryfolders.vdf") })
                {
                    if (!File.Exists(vdf)) continue;
                    string text;
                    try { text = File.ReadAllText(vdf); } catch { continue; }
                    foreach (Match m in Regex.Matches(text, "\"path\"\\s*\"([^\"]+)\""))
                        yield return Path.Combine(m.Groups[1].Value.Replace("\\\\", "\\"), "steamapps", "common", "Subnautica");
                }
            }

            // Epic Games
            var manifests = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
            string[] items;
            try { items = Directory.Exists(manifests) ? Directory.GetFiles(manifests, "*.item") : Array.Empty<string>(); }
            catch { items = Array.Empty<string>(); }
            foreach (var item in items)
            {
                string text;
                try { text = File.ReadAllText(item); } catch { continue; }
                if (text.IndexOf("Subnautica", StringComparison.OrdinalIgnoreCase) < 0) continue;
                var m = Regex.Match(text, "\"InstallLocation\"\\s*:\\s*\"([^\"]+)\"");
                if (m.Success) yield return m.Groups[1].Value.Replace("\\\\", "\\");
            }

            // Usual folders on every drive (Steam libraries, Epic, Xbox app / Game Pass)
            foreach (var drive in FixedDrives())
            {
                foreach (var rel in new[]
                {
                    @"SteamLibrary\steamapps\common\Subnautica",
                    @"Steam\steamapps\common\Subnautica",
                    @"Program Files (x86)\Steam\steamapps\common\Subnautica",
                    @"Program Files\Steam\steamapps\common\Subnautica",
                    @"Games\Steam\steamapps\common\Subnautica",
                    @"Games\SteamLibrary\steamapps\common\Subnautica",
                    @"Program Files\Epic Games\Subnautica",
                    @"Epic Games\Subnautica",
                    @"Games\Epic Games\Subnautica",
                    @"Games\Subnautica",
                    @"Subnautica",
                    @"XboxGames\Subnautica\Content",
                    @"Program Files\ModifiableWindowsApps\Subnautica",
                })
                    yield return Path.Combine(drive, rel);
            }
        }

        static IEnumerable<string> FixedDrives()
        {
            DriveInfo[] drives;
            try { drives = DriveInfo.GetDrives(); } catch { yield break; }
            foreach (var d in drives)
            {
                bool ok;
                try { ok = d.IsReady && d.DriveType == DriveType.Fixed; } catch { ok = false; }
                if (ok) yield return d.RootDirectory.FullName;
            }
        }

        static IEnumerable<string> RegistryInstallDirs()
        {
            var found = new List<string>();
            if (!OperatingSystem.IsWindows()) return found;
            foreach (var key in new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App " + SteamAppId,
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Steam App " + SteamAppId,
            })
            {
                try
                {
                    using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(key);
                    if (k?.GetValue("InstallLocation") is string dir) found.Add(dir);
                }
                catch { }
            }
            return found;
        }

        // Where Steam itself installed Subnautica (the copy Steam starts), or null.
        public static string SteamInstallDir()
        {
            foreach (var steam in SteamRoots())
            {
                var libs = new List<string> { steam };
                foreach (var vdf in new[] { Path.Combine(steam, "steamapps", "libraryfolders.vdf"), Path.Combine(steam, "config", "libraryfolders.vdf") })
                {
                    try
                    {
                        if (File.Exists(vdf))
                            foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s*\"([^\"]+)\""))
                                libs.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
                    }
                    catch { }
                }
                foreach (var lib in libs)
                {
                    try
                    {
                        var acf = Path.Combine(lib, "steamapps", "appmanifest_" + SteamAppId + ".acf");
                        if (!File.Exists(acf)) continue;
                        var m = Regex.Match(File.ReadAllText(acf), "\"installdir\"\\s*\"([^\"]+)\"");
                        var dir = Path.Combine(lib, "steamapps", "common", m.Success ? m.Groups[1].Value : "Subnautica");
                        if (IsGameDir(dir)) return Path.GetFullPath(dir);
                    }
                    catch { }
                }
            }
            return null;
        }

        public static string SteamExe() =>
            SteamRoots().Select(r => Path.Combine(r, "steam.exe")).FirstOrDefault(File.Exists);

        static IEnumerable<string> SteamRoots()
        {
            var roots = new List<string>();
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    using var cu = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                    if (cu?.GetValue("SteamPath") is string p) roots.Add(p.Replace('/', '\\'));
                    using var lm = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
                    if (lm?.GetValue("InstallPath") is string p2) roots.Add(p2);
                    using var lm2 = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam");
                    if (lm2?.GetValue("InstallPath") is string p3) roots.Add(p3);
                }
                catch { }
            }
            if (OperatingSystem.IsWindows()) roots.Add(@"C:\Program Files (x86)\Steam");
            else
            {
                // Linux / Steam Deck: native, Flatpak and Snap Steam (Subnautica itself runs in Proton)
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                foreach (var rel in new[] { ".steam/steam", ".local/share/Steam", ".steam/root",
                                            ".var/app/com.valvesoftware.Steam/.local/share/Steam", "snap/steam/common/.local/share/Steam" })
                {
                    var dir = Path.Combine(home, rel);
                    if (Directory.Exists(dir)) roots.Add(dir);
                }
            }
            return roots.Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        }
    }
}
