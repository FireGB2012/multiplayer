using UnityEngine;

namespace SubnauticaMP
{
    // A HUD marker where a teammate died, so you can go grab their stuff. Disappears after a while.
    internal static class DeathBeacon
    {
        const float Lifetime = 120f;

        public static void Place(string name, Vector3 position)
        {
            var go = new GameObject("DeathBeacon_" + name);
            go.transform.position = position;
            Game.AddPing(go, name + " died here");
            Object.Destroy(go, Lifetime);
        }
    }
}
