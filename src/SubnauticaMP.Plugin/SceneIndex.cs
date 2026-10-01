using System;
using System.Collections.Generic;
using UnityEngine;

namespace SubnauticaMP
{
    // Keeps lists of the game objects we check often (creatures, power sources, vehicles, bases...)
    // instead of searching the whole world for them every second, which made the game stutter.
    // Harmony hooks add/remove them as the game creates/destroys them; a slow background re-check
    // (one type at a time, never two in one frame) catches anything the hooks missed.
    internal static class SceneIndex
    {
        // A full search of the world (FindObjectsOfType) can take tens of ms in a big world: a hitch. Hooked lists
        // stay right by themselves, so they're only double-checked very rarely; the others are only searched when
        // something actually asks for them (All), never in the background.
        const float HookedRecheckSeconds = 900f;

        sealed class Entry
        {
            public readonly HashSet<Component> Items = new HashSet<Component>();
            public float LastFullScan = -1000f;
            public bool Hooked;
        }

        static readonly Dictionary<Type, Entry> _types = new Dictionary<Type, Entry>();
        static readonly List<Component> _scratch = new List<Component>();
        static int _lastScanFrame = -1;

        public static void Track(Type type, bool hooked)
        {
            if (type == null) return;
            if (!_types.TryGetValue(type, out var e)) _types[type] = e = new Entry();
            e.Hooked |= hooked;
        }

        public static void Add(Component c)
        {
            if (c == null) return;
            foreach (var kv in _types)
                if (kv.Key.IsInstanceOfType(c)) kv.Value.Items.Add(c);
        }

        public static void Remove(Component c)
        {
            if ((object)c == null) return;
            foreach (var e in _types.Values) e.Items.Remove(c);
        }

        // Everything of this type that's loaded right now. The list is reused: don't keep it.
        public static List<Component> All(Type type)
        {
            _scratch.Clear();
            if (type == null) return _scratch;
            if (!_types.TryGetValue(type, out var e)) { Track(type, false); e = _types[type]; }

            // never searched yet, or nothing hooks it: do a full search (at most one per frame)
            bool due = e.LastFullScan < 0 || (!e.Hooked && Time.unscaledTime - e.LastFullScan > 2f);
            if (due && _lastScanFrame != Time.frameCount) FullScan(type, e);

            e.Items.RemoveWhere(c => c == null);
            _scratch.AddRange(e.Items);
            return _scratch;
        }

        // Called every frame: re-checks one stale type now and then, spread out over time.
        public static void Tick()
        {
            if (_lastScanFrame == Time.frameCount) return;
            foreach (var kv in _types)
            {
                if (!kv.Value.Hooked || Time.unscaledTime - kv.Value.LastFullScan < HookedRecheckSeconds) continue;
                FullScan(kv.Key, kv.Value);
                return;
            }
        }

        static void FullScan(Type type, Entry e)
        {
            _lastScanFrame = Time.frameCount;
            e.LastFullScan = Time.unscaledTime;
            e.Items.Clear();
            foreach (var o in UnityEngine.Object.FindObjectsOfType(type))
                if (o is Component c) e.Items.Add(c);
        }

        public static void Clear()
        {
            foreach (var e in _types.Values) { e.Items.Clear(); e.LastFullScan = -1000f; }
        }
    }
}
