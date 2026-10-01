using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SubnauticaMP
{
    // Older versions could leave second copies of spawned things behind (same spot, renamed id "s:...#0~1a2b3c4d"),
    // and every save/reload stacked more: 10 crashfish homes in one sulfur plant, each with its own crashfish.
    // This finds those leftovers now and then and removes them, which fixes saves that already have them.
    internal static class DuplicateCleaner
    {
        const float Every = 45f;
        static float _next;
        static readonly List<string> _ids = new List<string>();
        static readonly Dictionary<Vector3Int, Component> _homes = new Dictionary<Vector3Int, Component>();

        public static void Tick()
        {
            if (!Game.InWorld || Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + Every;
            int removed = RenamedCopies() + StackedCrashHomes();
            if (removed > 0) Plugin.Log.LogInfo($"Removed {removed} duplicate object(s) (stacked spawns from older versions)");
        }

        // "s:x,y,z#0~1a2b3c4d" while "s:x,y,z#0" still exists: the renamed one is the extra copy.
        static int RenamedCopies()
        {
            if (!(Game.Get(Game.UniqueIdentifier, null, "identifiers") is IDictionary dict)) return 0;
            _ids.Clear();
            foreach (var key in dict.Keys)
                if (key is string id && id.StartsWith(CreatureSync.IdPrefix) && id.IndexOf('~') > 0) _ids.Add(id);
            int n = 0;
            foreach (var id in _ids)
            {
                if (!dict.Contains(id.Substring(0, id.IndexOf('~')))) continue;
                if (dict[id] is Component c && c != null) { Object.Destroy(c.gameObject); n++; }
            }
            return n;
        }

        // Crashfish homes sitting on top of each other (never happens in the normal game).
        static int StackedCrashHomes()
        {
            if (Game.CrashHome == null) return 0;
            _homes.Clear();
            int n = 0;
            foreach (var home in SceneIndex.All(Game.CrashHome)) // kept up to date by hooks: no world search
            {
                if (home == null) continue;
                var p = home.transform.position * 2f; // half-meter grid
                var cell = new Vector3Int(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y), Mathf.RoundToInt(p.z));
                if (_homes.ContainsKey(cell)) { Object.Destroy(home.gameObject); n++; }
                else _homes[cell] = home;
            }
            return n;
        }
    }
}
