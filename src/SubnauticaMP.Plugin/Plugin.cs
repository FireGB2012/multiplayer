using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using SubnauticaMP.Shared;
using UnityEngine;

namespace SubnauticaMP
{
    [BepInPlugin(Guid, "Subnautica Multiplayer", Version)]
    [BepInDependency(NautilusCompat.NautilusGuid, BepInDependency.DependencyFlags.SoftDependency)] // load after Nautilus when it's there
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.firegb2012.subnauticamp";
        public const string Version = "0.25.0";

        internal static ManualLogSource Log;
        internal static string Folder;
        internal static ConfigEntry<string> PlayerName;
        internal static ConfigEntry<string> ServerAddress;
        internal static ConfigEntry<int> Port;
        internal static ConfigEntry<KeyCode> MenuKey;
        internal static ConfigEntry<KeyCode> EmoteKey;
        internal static ConfigEntry<bool> EmoteCamera;
        internal static ConfigEntry<string> EmoteWheel;
        internal static ConfigEntry<bool> Performance;
        internal static ConfigEntry<KeyCode> PerfKey;
        internal static ConfigEntry<bool> RealRagdoll;
        internal static ConfigEntry<float> AutosaveMinutes;
        internal static ConfigEntry<string> HostPassword;
        internal static ConfigEntry<int> DiverColor;
        internal static ConfigEntry<bool> EnterForChat;

        // Our own GameObject so we survive scene loads (menu -> game).
        static void EnsureSession()
        {
            if (Session.Instance != null) return;
            var host = new GameObject("SubnauticaMP");
            host.transform.SetParent(null);
            Game.KeepAlive(host);
            host.AddComponent<Session>();
            Log.LogInfo("Multiplayer session object created");
        }

        void Awake()
        {
            Log = Logger;
            Folder = Path.GetDirectoryName(typeof(Plugin).Assembly.Location);
            PlayerName = Config.Bind("General", "PlayerName", "Diver", "Name other players see above your head.");
            ServerAddress = Config.Bind("General", "ServerAddress", "", "Join code or IP you last joined.");
            Port = Config.Bind("General", "Port", Protocol.DefaultPort, "Port to host on / join.");
            MenuKey = Config.Bind("General", "MenuKey", KeyCode.F8, "Opens the multiplayer window.");
            EmoteKey = Config.Bind("General", "EmoteKey", KeyCode.G, "Opens the emote picker (wave, dance...). /e wave in chat works too.");
            PerfKey = Config.Bind("General", "PerfKey", KeyCode.F9, "Shows the performance overlay (FPS, frame times, what the mod costs). /perf in chat writes a 10 s lag report.");
            RealRagdoll = Config.Bind("General", "RealRagdoll", true, "Pushed divers go limp with real physics (false = the animated fall).");
            AutosaveMinutes = Config.Bind("General", "AutosaveMinutes", 5f, "Saves your game by itself this often while playing multiplayer (0 = off).");
            Performance = Config.Bind("General", "Performance", true, "Let the game use more CPU / RAM: higher priority, bigger texture upload buffer, full-speed loading behind loading screens.");
            EmoteWheel = Config.Bind("General", "EmoteWheel", string.Join(",", Shared.Emotes.DefaultWheel), "The 8 emotes on your emote wheel (hold a slot in game to change it).");
            EmoteCamera = Config.Bind("General", "EmoteCamera", true, "Camera swings behind you while you do an emote, so you can see it.");
            DiverColor = Config.Bind("General", "DiverColor", Shared.DiverColors.Default, "Your suit color other players see (0xRRGGBB).");
            EnterForChat = Config.Bind("General", "EnterForChat", true, "Press Enter in game to open the chat box.");
            HostPassword = Config.Bind("General", "HostPassword", "", "Password friends need to join worlds you host (empty = no password).");

            // Unity builds a full stack trace for every log line / warning. The game spams warnings while the map
            // streams (hundreds when you turn around), and each trace costs real time: skip them for plain logs
            // and warnings (errors keep theirs).
            try
            {
                Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
                Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);
            }
            catch (System.Exception e) { Log.LogInfo("Couldn't turn off log stack traces: " + e.Message); }

            Game.Init();
            Patches.Apply(new Harmony(Guid));
            NautilusCompat.OnStartup();
            NautilusCompat.RegisterOptions();
            var mods = NautilusCompat.ContentMods();
            if (mods.Count > 0) Log.LogInfo("Content mods (must match the host's): " + string.Join(", ", mods.Select(m => m.ToString()).ToArray()));

            EnsureSession();
            // If a scene load ever wipes our object out, bring it straight back.
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (scene, mode) =>
            {
                Log.LogInfo($"Scene loaded: {scene.name} ({mode}), multiplayer alive: {Session.Instance != null}");
                EnsureSession();
            };

            Log.LogInfo($"Subnautica Multiplayer {Version} loaded. Press {MenuKey.Value} in game.");
        }
    }
}
