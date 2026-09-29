using System;
using System.Collections.Generic;
using System.Linq;
using SubnauticaMP.Shared;
using UnityEngine;

namespace SubnauticaMP
{
    // Base building. Whoever builds or deconstructs something sends a snapshot of the whole base
    // (or the standalone object), made with the game's own save serializer. Everyone else swaps
    // their copy for it. Furniture and lockers inside come along with the base.
    internal sealed class StructureSync
    {
        const float QuietSeconds = 1.5f;   // wait until they stop hammering the build tool
        const float NearbyRadius = 60f;

        sealed class Dirty
        {
            public GameObject Root;
            public string Id;
            public float LastTouch;
        }

        readonly Session _s;
        readonly Dictionary<string, byte[]> _known = new Dictionary<string, byte[]>(); // server's latest, by id
        readonly Dictionary<GameObject, Dirty> _dirty = new Dictionary<GameObject, Dirty>();
        readonly HashSet<string> _applying = new HashSet<string>();
        readonly Queue<(string id, byte[] data)> _pending = new Queue<(string, byte[])>();
        readonly Dictionary<GameObject, float> _recentlyApplied = new Dictionary<GameObject, float>();
        Dictionary<string, int> _nearbyBaseSizes = new Dictionary<string, int>();
        float _lastBuildActivity = -100f;
        bool _applied;

        public StructureSync(Session s) { _s = s; }

        public void Reset()
        {
            _known.Clear();
            _dirty.Clear();
            _pending.Clear();
            _applied = false;
        }

        public void OnWelcome(WorldState w)
        {
            Reset();
            foreach (var kv in w.Structures) _known[kv.Key] = kv.Value;
        }

        // Harmony hooks call this while the local player builds / deconstructs.
        public void OnLocalBuild(GameObject piece)
        {
            if (!_applied) return;
            var root = Game.StructureRoot(piece);
            if (root == null) return;
            _lastBuildActivity = Time.unscaledTime;
            if (!_dirty.TryGetValue(root, out var d))
                _dirty[root] = d = new Dirty { Root = root, Id = Game.GetId(root) };
            d.LastTouch = Time.unscaledTime;
        }

        public bool Idle => _applying.Count == 0 && _pending.Count == 0;
        public bool Busy => _applying.Count > 0;

        public bool WasJustApplied(Transform t)
        {
            foreach (var kv in _recentlyApplied)
                if (kv.Key != null && Time.unscaledTime < kv.Value && t.IsChildOf(kv.Key.transform)) return true;
            return false;
        }

        public void Update()
        {
            if (!Game.InWorld) { _applied = false; return; }
            if (!_s.InWorldAndSettled) return;

            if (!_applied)
            {
                _applied = true;
                foreach (var kv in _known) _pending.Enqueue((kv.Key, kv.Value));
                _nearbyBaseSizes = MeasureNearbyBases();

                // first one into a new world from an existing save: share the bases you already have
                if (_s.World.SeedsWorld && _known.Count == 0)
                    foreach (var b in Game.FindBases()) Send(b);
            }

            // apply one incoming snapshot at a time
            if (_pending.Count > 0 && _applying.Count == 0)
            {
                var (id, data) = _pending.Dequeue();
                Apply(id, data);
            }

            FlushDirty();
        }

        void FlushDirty()
        {
            if (_dirty.Count == 0) return;
            float now = Time.unscaledTime;
            foreach (var d in _dirty.Values.ToList())
            {
                if (now - d.LastTouch < QuietSeconds) continue;
                _dirty.Remove(d.Root);

                if (d.Root == null)
                {
                    // fully deconstructed. A brand-new base may have replaced a ghost: check nearby bases too.
                    if (!string.IsNullOrEmpty(d.Id) && _known.ContainsKey(d.Id)) SendRemoved(d.Id);
                    SendChangedNearbyBases();
                    continue;
                }
                Send(d.Root);
                SendChangedNearbyBases();
            }
        }

        void Send(GameObject root)
        {
            var id = Game.GetId(root);
            if (string.IsNullOrEmpty(id)) return;
            byte[] data;
            try { data = Game.Serialize(root); }
            catch (Exception e)
            {
                Game.WarnOnce("serialize", "Couldn't save a base to send it: " + e.GetBaseException().Message);
                return;
            }
            if (data.Length == 0 || data.Length > 7 * 1024 * 1024) return;
            _known[id] = data;
            _s.Send(new StructurePacket { Id = id, Data = data });
            Plugin.Log.LogInfo($"Sent base/structure {id} ({data.Length / 1024} KB)");
        }

        void SendRemoved(string id)
        {
            _known.Remove(id);
            _s.Send(new StructurePacket { Id = id, Data = new byte[0] });
        }

        // Finishing a new base's first piece swaps the ghost for a real Base object with a new id.
        // Catch that by comparing nearby bases' sizes before and after.
        void SendChangedNearbyBases()
        {
            var now = MeasureNearbyBases();
            foreach (var kv in now)
            {
                if (_nearbyBaseSizes.TryGetValue(kv.Key, out var before) && before == kv.Value) continue;
                if (Time.unscaledTime - _lastBuildActivity > 10f) continue;
                var go = Game.FindById(kv.Key);
                if (go != null) Send(go);
            }
            _nearbyBaseSizes = now;
        }

        Dictionary<string, int> MeasureNearbyBases()
        {
            var sizes = new Dictionary<string, int>();
            var player = Game.LocalPlayer;
            if (player == null) return sizes;
            foreach (var b in Game.FindBases())
            {
                if (Vector3.Distance(b.transform.position, player.transform.position) > NearbyRadius) continue;
                var id = Game.GetId(b);
                if (id != null) sizes[id] = b.GetComponentsInChildren<Transform>(true).Length;
            }
            return sizes;
        }

        // ---------- incoming ----------

        public void OnStructure(StructurePacket p)
        {
            if (string.IsNullOrEmpty(p.Id)) return;
            if (p.Data == null || p.Data.Length == 0) _known.Remove(p.Id);
            else _known[p.Id] = p.Data;
            if (_applied) _pending.Enqueue((p.Id, p.Data));
        }

        void Apply(string id, byte[] data)
        {
            var player = Game.LocalPlayer;
            var oldSub = player != null ? Game.PlayerSub(player) : null;
            var existing = Game.FindById(id);
            bool playerWasInside = existing != null && oldSub != null && oldSub.gameObject == existing;

            if (existing != null)
            {
                Patches.ApplyingRemote = true;
                try { UnityEngine.Object.DestroyImmediate(existing); }
                finally { Patches.ApplyingRemote = false; }
            }
            if (data == null || data.Length == 0) return; // deconstructed

            _applying.Add(id);
            _s.StartCoroutine(Game.Deserialize(data, go =>
            {
                _applying.Remove(id);
                if (go == null)
                {
                    Game.WarnOnce("deserialize", "Couldn't load a teammate's base");
                    return;
                }
                go.transform.SetParent(null, true);
                go.SetActive(true);
                Game.Register(go);
                if (_recentlyApplied.Count > 50)
                    foreach (var k in _recentlyApplied.Where(kv => kv.Key == null || kv.Value < Time.unscaledTime).Select(kv => kv.Key).ToList())
                        _recentlyApplied.Remove(k);
                _recentlyApplied[go] = Time.unscaledTime + 3f;

                // we were standing in the old copy: move us into the new one
                if (playerWasInside && Game.SubRoot != null && go.GetComponent(Game.SubRoot) is Component sub)
                    Game.TryDo("setsub", () => Game.Call(Game.Player, Game.LocalPlayer, "SetCurrentSub", sub));
                if (Game.Base != null && go.GetComponent(Game.Base) != null && Game.GetId(go) is string bid)
                    _nearbyBaseSizes[bid] = go.GetComponentsInChildren<Transform>(true).Length;
            }));
        }
    }
}
