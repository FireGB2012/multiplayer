using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using SubnauticaMP.Shared;
using UnityEngine;

namespace SubnauticaMP
{
    [BepInPlugin(Guid, "Subnautica Multiplayer", "0.1.0")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.firegb2012.subnauticamp";

        internal static ManualLogSource Log;
        internal static ConfigEntry<string> PlayerName;
        internal static ConfigEntry<string> ServerAddress;
        internal static ConfigEntry<int> Port;
        internal static ConfigEntry<KeyCode> MenuKey;

        void Awake()
        {
            Log = Logger;
            PlayerName = Config.Bind("General", "PlayerName", "Diver", "Name other players see above your head.");
            ServerAddress = Config.Bind("General", "ServerAddress", "127.0.0.1", "Last server you joined.");
            Port = Config.Bind("General", "Port", Protocol.DefaultPort, "Port to host on / join.");
            MenuKey = Config.Bind("General", "MenuKey", KeyCode.F8, "Opens the multiplayer window.");

            // Our own GameObject so we survive scene loads (menu -> game).
            var host = new GameObject("SubnauticaMP");
            DontDestroyOnLoad(host);
            host.AddComponent<Multiplayer>();

            Log.LogInfo($"Subnautica Multiplayer loaded. Press {MenuKey.Value} in game.");
        }
    }
}
