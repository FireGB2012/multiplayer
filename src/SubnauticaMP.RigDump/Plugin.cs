using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SubnauticaMP
{
    // Press F10 in a world: writes the diver (first-person arms + body skeleton, meshes, textures, animations) as
    // .glb files to BepInEx\plugins\SubnauticaRigDump\RigExport\ for Blender. Doesn't touch the game otherwise.
    [BepInPlugin("com.firegb2012.subnauticarigdump", "Subnautica Rig Dump", "1.0.1")]
    public sealed class RigDumpPlugin : BaseUnityPlugin
    {
        internal static BepInEx.Logging.ManualLogSource Log;
        internal static string Folder;

        // Subnautica deletes every object that isn't tagged SceneCleanerPreserve when scenes change (the plugin's own
        // object included), so the key check lives on an object of ours that carries the tag and is rebuilt if lost.
        void Awake()
        {
            Log = Logger;
            Folder = Path.GetDirectoryName(typeof(RigDumpPlugin).Assembly.Location);
            EnsureRunner();
            SceneManager.sceneLoaded += (scene, mode) => EnsureRunner();
            Logger.LogInfo("Rig Dump loaded. Press F10 in a world to export the diver.");
        }

        static void EnsureRunner()
        {
            if (RigDumpRunner.Instance != null) return;
            var go = new GameObject("SubnauticaRigDump");
            DontDestroyOnLoad(go);
            var preserve = FindType("SceneCleanerPreserve");
            if (preserve != null && go.GetComponent(preserve) == null) go.AddComponent(preserve);
            go.AddComponent<RigDumpRunner>();
        }

        static Type FindType(string name)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var t = asm.GetType(name, false);
                    if (t != null) return t;
                }
                catch { }
            }
            return null;
        }

        // Subnautica's Player.main, found by name so we don't need the game's own DLL to compile.
        internal static Component FindPlayer()
        {
            try
            {
                var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
                var type = asm != null ? asm.GetType("Player") : null;
                if (type == null) return null;
                const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                var field = type.GetField("main", flags);
                if (field != null) return field.GetValue(null) as Component;
                var prop = type.GetProperty("main", flags);
                return prop != null ? prop.GetValue(null, null) as Component : null;
            }
            catch { return null; }
        }
    }

    sealed class RigDumpRunner : MonoBehaviour
    {
        public static RigDumpRunner Instance;
        string _message = "";
        float _messageUntil;

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Update()
        {
            if (!Input.GetKeyDown(KeyCode.F10)) return;
            RigDumpPlugin.Log.LogInfo("F10 pressed");
            RigExport.Run(RigDumpPlugin.FindPlayer(), Path.Combine(RigDumpPlugin.Folder, "RigExport"), "RigDump 1.0.1", m =>
            {
                RigDumpPlugin.Log.LogInfo(m);
                _message = m;
                _messageUntil = Time.realtimeSinceStartup + 10f;
            });
        }

        void OnGUI()
        {
            if (Time.realtimeSinceStartup > _messageUntil) return;
            var style = new GUIStyle(GUI.skin.box) { wordWrap = true, fontSize = 16, alignment = TextAnchor.MiddleCenter };
            GUI.Box(new Rect(Screen.width / 2f - 360, 20, 720, 70), _message, style);
        }
    }
}
