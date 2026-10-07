using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using UnityEngine;

namespace SubnauticaMP
{
    // Press F10 in a world: writes the diver (first-person arms + body skeleton, meshes, textures, animations) as
    // .glb files to BepInEx\plugins\SubnauticaRigDump\RigExport\ for Blender. Doesn't touch the game otherwise.
    [BepInPlugin("com.firegb2012.subnauticarigdump", "Subnautica Rig Dump", "1.0.0")]
    public sealed class RigDumpPlugin : BaseUnityPlugin
    {
        string _message = "";
        float _messageUntil;

        void Awake() => Logger.LogInfo("Rig Dump loaded. Press F10 in a world to export the diver.");

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F10)) Export();
        }

        void Export()
        {
            var dir = Path.Combine(Path.GetDirectoryName(typeof(RigDumpPlugin).Assembly.Location), "RigExport");
            RigExport.Run(FindPlayer(), dir, "RigDump 1.0.0", m =>
            {
                Logger.LogInfo(m);
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

        // Subnautica's Player.main, found by name so we don't need the game's own DLL to compile.
        static Component FindPlayer()
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
}
