using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SubnauticaMP
{
    // What a diver looks like beyond their position: suit / fins / helmet / tank models, and the arm
    // animations for tools (knife swings, scanning, building, PDA...).
    internal static class PlayerLooks
    {
        // animator switches worth copying; movement ones are worked out from speed instead
        static readonly string[] AnimPrefixes = { "holding_", "using_tool", "using_builder", "using_pda", "terraformer_mode_on", "bash", "grab" };

        sealed class Slot
        {
            public string Name;
            public string DefaultPath;
            public readonly List<(string tech, string path)> Models = new List<(string, string)>();
        }

        static List<Slot> _slots;
        static Animator _animFor;
        static (string name, int hash)[] _animParams = new (string, int)[0];
        static readonly List<string> _animOn = new List<string>(), _lastOn = new List<string>();
        static string _animKey = "";

        public static bool IsToolAnim(string param) => AnimPrefixes.Any(param.StartsWith);

        // ---------- reading the local player ----------

        // "Body=RadiationSuit;Foots=Fins;Head=None"
        public static string ReadGear()
        {
            var player = Game.LocalPlayer;
            var slots = Table(player);
            if (slots == null || slots.Count == 0) return "";
            var inv = Game.TryGet(Game.Inventory, null, "main");
            var equipment = inv != null ? Game.TryGet(Game.Inventory, inv, "equipment") : null;
            if (equipment == null) return "";
            var parts = new List<string>();
            foreach (var s in slots)
            {
                var tech = Game.Call(equipment.GetType(), equipment, "GetTechTypeInSlot", s.Name);
                parts.Add(s.Name + "=" + (tech?.ToString() ?? "None"));
            }
            return string.Join(";", parts.ToArray());
        }

        // "holding_knife,using_tool"
        public static string ReadAnim()
        {
            var player = Game.LocalPlayer;
            var animator = player != null ? Game.TryGet(Game.Player, player, "playerAnimator") as Animator : null;
            if (animator == null || !animator.isActiveAndEnabled) return "";
            if (animator != _animFor)
            {
                // animator.parameters makes a new array every call: read the list once
                _animFor = animator;
                _animParams = animator.parameters
                    .Where(p => p.type == AnimatorControllerParameterType.Bool && IsToolAnim(p.name))
                    .Select(p => (p.name, p.nameHash)).ToArray();
            }
            _animOn.Clear();
            foreach (var (name, hash) in _animParams)
                if (animator.GetBool(hash)) _animOn.Add(name);
            if (_animOn.Count == 0) return "";
            _animKey = _animOn.Count == _lastOn.Count && _animOn.SequenceEqual(_lastOn) ? _animKey : string.Join(",", _animOn.ToArray());
            _lastOn.Clear();
            _lastOn.AddRange(_animOn);
            return _animKey;
        }

        // Where each suit model lives inside the player's "body", read once from the local player.
        static List<Slot> Table(Component player)
        {
            if (_slots != null) return _slots;
            if (player == null) return null;
            var body = player.transform.Find("body");
            var types = Game.TryGet(Game.Player, player, "equipmentModels") as IEnumerable;
            if (body == null || types == null) return null;

            var list = new List<Slot>();
            try
            {
                foreach (var type in types)
                {
                    var t = type.GetType();
                    var slot = new Slot { Name = Game.TryGet(t, type, "slot") as string };
                    if (string.IsNullOrEmpty(slot.Name)) continue;
                    slot.DefaultPath = PathOf(Game.TryGet(t, type, "defaultModel") as GameObject, body);
                    if (Game.TryGet(t, type, "equipment") is IEnumerable models)
                        foreach (var m in models)
                        {
                            var mt = m.GetType();
                            var path = PathOf(Game.TryGet(mt, m, "model") as GameObject, body);
                            if (path != null) slot.Models.Add((Game.TryGet(mt, m, "techType")?.ToString() ?? "", path));
                        }
                    list.Add(slot);
                }
            }
            catch (Exception e)
            {
                Game.WarnOnce("gear", "Couldn't read suit models: " + e.GetBaseException().Message);
            }
            _slots = list;
            return list;
        }

        static string PathOf(GameObject go, Transform root)
        {
            if (go == null) return null;
            var names = new List<string>();
            for (var t = go.transform; t != null && t != root; t = t.parent)
            {
                names.Add(t.name);
                if (t.parent == null) return null; // not under the body
            }
            names.Reverse();
            return string.Join("/", names.ToArray());
        }

        // ---------- showing it on someone else's diver ----------

        public static void ApplyGear(GameObject diver, string gear)
        {
            var slots = Table(Game.LocalPlayer);
            if (diver == null || slots == null) return;
            var wearing = new Dictionary<string, string>();
            foreach (var part in (gear ?? "").Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq > 0) wearing[part.Substring(0, eq)] = part.Substring(eq + 1);
            }

            foreach (var s in slots)
            {
                wearing.TryGetValue(s.Name, out var tech);
                bool any = false;
                foreach (var (modelTech, path) in s.Models)
                {
                    bool on = modelTech == tech;
                    any |= on;
                    var t = diver.transform.Find(path);
                    if (t != null) t.gameObject.SetActive(on);
                }
                if (s.DefaultPath != null)
                {
                    var d = diver.transform.Find(s.DefaultPath);
                    if (d != null) d.gameObject.SetActive(!any);
                }
            }
        }

        public static void Forget() => _slots = null;
    }
}
