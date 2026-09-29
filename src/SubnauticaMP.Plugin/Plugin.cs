using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using SubnauticaMP.Shared;
using UnityEngine;

namespace SubnauticaMP
{
    [BepInPlugin(Guid, "Subnautica Multiplayer", Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.firegb2012.subnauticamp";
        public const string Version = "0.7.0";

        internal static ManualLogSource Log;
        internal static string Folder;
        internal static ConfigEntry<string> PlayerName;
        internal static ConfigEntry<string> ServerAddress;
        internal static ConfigEntry<int> Port;
        internal static ConfigEntry<KeyCode> MenuKey;

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

            Game.Init();
            Patches.Apply(new Harmony(Guid));

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
