using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using SubnauticaMP.Shared;

namespace SubnauticaMP
{
    // Playing together with Nautilus mods (custom items, creatures, blueprints...):
    //  - every player tells the server which content mods they have; the world needs the host's ones
    //  - Nautilus numbers modded items per PC (in BepInEx\config\Nautilus\TechTypeCache). If two players' numbers
    //    differ, synced items would turn into the wrong thing, so the server sends the right numbers and we write
    //    them into Nautilus's cache; after a restart the numbers match.
    //  - with Nautilus installed, our settings also show up in Options > Mods.
    internal static class NautilusCompat
    {
        public const string NautilusGuid = "com.snmodding.nautilus";
        const string SmlHelperGuid = "com.ahk1221.smlhelper";

        static string FixFile => Path.Combine(Plugin.Folder, "nautilus_fix.txt");

        public static bool NautilusLoaded => Plugins().Any(p => p.guid == NautilusGuid);

        // ---------- what's installed ----------

        // (guid, name, version, dependencies) of every BepInEx plugin, read from BepInEx's chainloader.
        static IEnumerable<(string guid, string name, string version, string[] deps)> Plugins()
        {
            var chainloader = Type.GetType("BepInEx.Bootstrap.Chainloader, BepInEx");
            if (!(chainloader?.GetProperty("PluginInfos", BindingFlags.Public | BindingFlags.Static)?.GetValue(null, null) is IDictionary infos)) yield break;
            foreach (var info in infos.Values)
            {
                if (info == null) continue;
                var meta = info.GetType().GetProperty("Metadata")?.GetValue(info, null);
                if (meta == null) continue;
                var guid = meta.GetType().GetProperty("GUID")?.GetValue(meta, null) as string;
                var name = meta.GetType().GetProperty("Name")?.GetValue(meta, null) as string;
                var version = meta.GetType().GetProperty("Version")?.GetValue(meta, null)?.ToString() ?? "";
                var deps = new List<string>();
                if (info.GetType().GetProperty("Dependencies")?.GetValue(info, null) is IEnumerable ds)
                    foreach (var d in ds)
                        if (d?.GetType().GetProperty("DependencyGUID")?.GetValue(d, null) is string g) deps.Add(g);
                if (!string.IsNullOrEmpty(guid)) yield return (guid, name ?? guid, version, deps.ToArray());
            }
        }

        // Mods that add content through Nautilus or SMLHelper (libraries and client-side mods don't matter).
        public static List<ModInfo> ContentMods()
        {
            try
            {
                return Plugins()
                    .Where(p => p.guid != Plugin.Guid && p.guid != NautilusGuid && p.guid != SmlHelperGuid &&
                                p.deps.Any(d => d == NautilusGuid || d == SmlHelperGuid))
                    .Select(p => new ModInfo { Guid = p.guid, Name = p.name, Version = p.version })
                    .OrderBy(m => m.Guid)
                    .ToList();
            }
            catch (Exception e)
            {
                Game.WarnOnce("mods", "Couldn't list installed mods: " + e.Message);
                return new List<ModInfo>();
            }
        }

        // Modded items (TechTypes that aren't in the game itself) and their numbers on this PC.
        public static Dictionary<string, int> ModdedTechTypes()
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var type = Game.TechType;
            if (type == null) return map;
            try
            {
                var vanilla = new HashSet<int>(type.GetFields(BindingFlags.Public | BindingFlags.Static)
                    .Select(f => Convert.ToInt32(f.GetRawConstantValue())));
                // with Nautilus, Enum.GetValues / ToString also know the modded ones
                foreach (var v in Enum.GetValues(type))
                {
                    int n = Convert.ToInt32(v);
                    if (vanilla.Contains(n)) continue;
                    var name = v.ToString();
                    if (string.IsNullOrEmpty(name) || name == n.ToString()) continue;
                    map[name] = n;
                }
            }
            catch (Exception e) { Game.WarnOnce("techtypes", "Couldn't read modded items: " + e.Message); }
            return map;
        }

        // ---------- fixing item numbers ----------

        static string CacheFolder
        {
            get
            {
                // BepInEx\plugins\SubnauticaMP -> BepInEx\config\Nautilus\TechTypeCache
                var bepinex = Directory.GetParent(Directory.GetParent(Plugin.Folder).FullName).FullName;
                return Path.Combine(bepinex, "config", "Nautilus", "TechTypeCache");
            }
        }

        // Writes the world's numbers into Nautilus's cache. Takes effect the next time the game starts.
        public static bool ApplyFix(Dictionary<string, int> world)
        {
            if (world == null || world.Count == 0) return false;
            try
            {
                File.WriteAllLines(FixFile, world.Select(kv => kv.Key + ":" + kv.Value).ToArray()); // re-applied on quit too
                WriteCache(world);
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Couldn't fix Nautilus's item numbers: " + e);
                return false;
            }
        }

        static void WriteCache(Dictionary<string, int> world)
        {
            Directory.CreateDirectory(CacheFolder);
            var activePath = Path.Combine(CacheFolder, "TechTypeCache.txt");
            var offPath = Path.Combine(CacheFolder, "TechTypeDeactivatedCache.txt");
            var active = ReadCache(activePath);
            var off = ReadCache(offPath);

            var (newActive, newOff) = ModInfo.MergeCache(active, off, world);

            if (File.Exists(activePath)) File.Copy(activePath, activePath + ".bak", true);
            if (File.Exists(offPath)) File.Copy(offPath, offPath + ".bak", true);
            File.WriteAllLines(activePath, newActive.OrderBy(kv => kv.Value).Select(kv => kv.Key + ":" + kv.Value).ToArray());
            File.WriteAllLines(offPath, newOff.OrderBy(kv => kv.Value).Select(kv => kv.Key + ":" + kv.Value).ToArray());
            Plugin.Log.LogInfo($"Nautilus item numbers set to the world's ({world.Count} items). Restart the game to use them.");
        }

        static Dictionary<string, int> ReadCache(string path)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(path)) return map;
            foreach (var line in File.ReadAllLines(path))
            {
                int colon = line.LastIndexOf(':');
                if (colon > 0 && int.TryParse(line.Substring(colon + 1).Trim(), out var n)) map[line.Substring(0, colon)] = n;
            }
            return map;
        }

        // Nautilus rewrites its cache when you save the game; if a fix is waiting for a restart, put it back
        // when the game closes. Once the numbers in the running game match, the fix is done.
        public static void OnStartup()
        {
            if (!File.Exists(FixFile)) return;
            var fix = ReadCache(FixFile);
            UnityEngine.Application.quitting += () => { try { if (File.Exists(FixFile)) WriteCache(ReadCache(FixFile)); } catch { } };
            Session.RunLater(10f, () =>
            {
                var live = ModdedTechTypes();
                if (fix.Count > 0 && ModInfo.Mismatches(fix, live).Count == 0)
                {
                    try { File.Delete(FixFile); } catch { }
                    Plugin.Log.LogInfo("Nautilus item numbers now match the multiplayer world.");
                }
            });
        }

        // ---------- Options > Mods page ----------

        // The options page class is built on Nautilus's own types, so it's only ever touched (even by the
        // JIT) from these small separate methods, and only when Nautilus is actually there.
        static bool _optionsOn;

        public static void RegisterOptions()
        {
            if (!NautilusLoaded) return;
            try { RegisterPage(); _optionsOn = true; }
            catch (Exception e) { Plugin.Log.LogWarning("Couldn't add the options page to Nautilus's Mods menu: " + e.GetBaseException().Message); }
        }

        public static void SyncOptions()
        {
            if (!_optionsOn) return;
            try { SyncPage(); }
            catch (Exception e) { _optionsOn = false; Game.WarnOnce("nautopts", "Options page sync failed: " + e.GetBaseException().Message); }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static void RegisterPage() => NautilusOptionsPage.Register();

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static void SyncPage() => NautilusOptionsPage.Sync();
    }
}
