using System;
using System.Collections.Generic;
using System.Linq;
using SubnauticaMP.Shared;
using UnityEngine;

namespace SubnauticaMP
{
    // Lockers and every other storage (lifepod, Cyclops lockers, vehicle storage...).
    // When you put something in or take it out, your game sends the full contents; everyone else
    // empties their copy of that locker and refills it with exactly the same items.
    internal sealed class ContainerSync
    {
        const float QuietSeconds = 0.4f;
        const float TouchRadius = 25f; // you can only open lockers you're standing next to

        readonly Session _s;
        readonly Dictionary<string, List<byte[]>> _known = new Dictionary<string, List<byte[]>>();
        readonly Dictionary<string, (object container, float time)> _dirty = new Dictionary<string, (object, float)>();
        readonly Queue<(string id, List<byte[]> items)> _pending = new Queue<(string, List<byte[]>)>();
        bool _applied, _applying;

        public ContainerSync(Session s) { _s = s; }

        public int Waiting => _pending.Count + (_applying ? 1 : 0);
        public int Total => _known.Count;

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
            foreach (var kv in w.Containers) _known[kv.Key] = kv.Value;
        }

        // Harmony hook on ItemsContainer add/remove.
        public void OnLocalChange(object container)
        {
            if (!_applied || _applying || Patches.ApplyingRemote || !_s.InWorldAndSettled || _s.Structures.Busy) return;
            var tr = Game.ContainerRoot(container);
            var player = Game.LocalPlayer;
            if (tr == null || player == null) return;
            if (Game.Player != null && tr.GetComponentInParent(Game.Player) != null) return; // your own inventory
            if (Vector3.Distance(tr.position, player.transform.position) > TouchRadius) return;
            if (_s.Structures.WasJustApplied(tr)) return;
            var id = Game.ContainerId(container);
            if (string.IsNullOrEmpty(id)) return;
            _dirty[id] = (container, Time.unscaledTime);
        }

        public void Update()
        {
            if (!Game.InWorld) { _applied = false; return; }
            if (!_s.InWorldAndSettled) return;

            if (!_applied)
            {
                _applied = true;
                foreach (var kv in _known) _pending.Enqueue((kv.Key, kv.Value));
            }

            // give bases a moment to load before filling their lockers
            if (_pending.Count > 0 && !_applying && _s.Structures.Idle) ApplyNext();

            foreach (var kv in _dirty.ToList())
            {
                if (Time.unscaledTime - kv.Value.time < QuietSeconds) continue;
                _dirty.Remove(kv.Key);
                var tr = Game.ContainerRoot(kv.Value.container);
                if (tr == null || _s.Structures.WasJustApplied(tr)) continue;
                Send(kv.Key, kv.Value.container);
            }
        }

        void Send(string id, object container)
        {
            var items = new List<byte[]>();
            foreach (var p in Game.ContainerItems(container))
            {
                try { items.Add(Game.Serialize(p.gameObject)); }
                catch (Exception e) { Game.WarnOnce("itemser", "Couldn't save an item to send it: " + e.GetBaseException().Message); }
            }
            _known[id] = items;
            _s.Send(new ContainerPacket { Id = id, Items = items });
        }

        public void OnContainer(ContainerPacket p)
        {
            if (string.IsNullOrEmpty(p.Id)) return;
            _known[p.Id] = p.Items;
            if (_applied) _pending.Enqueue((p.Id, p.Items));
        }

        void ApplyNext()
        {
            var (id, items) = _pending.Dequeue();
            var go = Game.FindById(id);
            var container = go != null ? Game.ContainerOf(go) : null;
            if (container == null) return; // not loaded here (yet); it comes with its base snapshot
            _s.StartCoroutine(Fill(container, items));
        }

        System.Collections.IEnumerator Fill(object container, List<byte[]> items)
        {
            _applying = true;
            Patches.ApplyingRemote = true;
            try { Game.EmptyContainer(container); }
            finally { Patches.ApplyingRemote = false; }

            foreach (var data in items)
            {
                GameObject item = null;
                yield return Game.Deserialize(data, g => item = g);
                if (item == null) continue;
                Patches.ApplyingRemote = true;
                try
                {
                    if (!Game.AddToContainer(container, item)) UnityEngine.Object.Destroy(item);
                }
                finally { Patches.ApplyingRemote = false; }
            }
            _applying = false;
        }
    }
}
