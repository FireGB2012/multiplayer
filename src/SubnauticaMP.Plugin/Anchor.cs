using System;
using System.Globalization;
using UnityEngine;

namespace SubnauticaMP
{
    // Names things that have no id of their own (a fire spot, a fruit, a power source inside a base module...):
    // "<id of the nearest parent that has one>|<where it sits relative to that parent>". Same on every PC.
    internal static class Anchor
    {
        const float MatchDistance = 0.3f;

        public static string KeyOf(GameObject go)
        {
            if (go == null) return null;
            for (var t = go.transform; t != null; t = t.parent)
            {
                var id = Game.GetId(t.gameObject);
                if (string.IsNullOrEmpty(id)) continue;
                if (t == go.transform) return id + "|";
                var p = t.InverseTransformPoint(go.transform.position);
                return id + "|" + R(p.x) + "," + R(p.y) + "," + R(p.z);
            }
            return null;
        }

        static string R(float v) => v.ToString("0.00", CultureInfo.InvariantCulture);

        // The component of this type that the key points at, or null.
        public static Component Find(string key, Type type)
        {
            if (string.IsNullOrEmpty(key) || type == null) return null;
            int bar = key.IndexOf('|');
            if (bar <= 0) return null;
            var anchor = Game.FindById(key.Substring(0, bar));
            if (anchor == null) return null;
            var rest = key.Substring(bar + 1);
            if (rest.Length == 0) return anchor.GetComponent(type) ?? anchor.GetComponentInChildren(type, true);

            var parts = rest.Split(',');
            if (parts.Length != 3 ||
                !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ||
                !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z)) return null;
            var want = new Vector3(x, y, z);

            Component best = null;
            float bestDist = MatchDistance * MatchDistance;
            foreach (var c in anchor.GetComponentsInChildren(type, true))
            {
                if (c.gameObject == anchor) continue;
                float d = (anchor.transform.InverseTransformPoint(c.transform.position) - want).sqrMagnitude;
                if (d <= bestDist) { bestDist = d; best = c; }
            }
            return best;
        }
    }
}
