using System;
using System.Collections.Generic;
using System.Linq;
using SubnauticaMP.Shared;
using UnityEngine;

namespace SubnauticaMP
{
    // Items put down in the world (so you can hand stuff to each other, place beacons...) and doors/hatches.
    internal sealed class ItemSync
    {
        const float DoorRadius = 25f;

        readonly Session _s;
        readonly Dictionary<string, byte[]> _dropped = new Dictionary<string, byte[]>();
        readonly Dictionary<string, (bool open, float duration)> _doors = new Dictionary<string, (bool, float)>();
        readonly Queue<(string id, byte[] data)> _pending = new Queue<(string, byte[])>();
        bool _applied, _spawning;

        public ItemSync(Session s) { _s = s; }

        public void Reset()
        {
            _dropped.Clear();
            _doors.Clear();
            _pending.Clear();
            _applied = false;
        }

        public void OnWelcome(WorldState w)
        {
            Reset();
            foreach (var kv in w.DroppedItems) _dropped[kv.Key] = kv.Value;
            foreach (var kv in w.Doors) _doors[kv.Key] = (kv.Value, 0f);
        }

        public void Update()
        {
            if (!Game.InWorld) { _applied = false; return; }
            if (!_s.InWorldAndSettled) return;
            if (!_applied)
            {
                _applied = true;
                foreach (var kv in _dropped) _pending.Enqueue((kv.Key, kv.Value));
                foreach (var kv in _doors.ToList()) ApplyDoor(kv.Key, kv.Value.open, 0f);
            }
            if (_pending.Count > 0 && !_spawning)
            {
                var (id, data) = _pending.Dequeue();
                Spawn(id, data);
            }
        }

        // ---------- dropped items ----------

        public void OnLocalDrop(GameObject item)
        {
            if (!_applied) return;
            var id = Game.GetId(item);
            if (string.IsNullOrEmpty(id)) return;
            byte[] data;
            try { data = Game.Serialize(item); }
            catch (Exception e)
            {
                Game.WarnOnce("dropser", "Couldn't save a dropped item: " + e.GetBaseException().Message);
                return;
            }
            _dropped[id] = data;
            _s.World.ForgetRemoved(id); // don't let our own "picked up" memory delete it
            _s.Send(new ItemDroppedPacket { Id = id, Data = data });
        }

        public void OnDropped(ItemDroppedPacket p)
        {
            if (string.IsNullOrEmpty(p.Id)) return;
            _dropped[p.Id] = p.Data;
            _s.World.ForgetRemoved(p.Id);
            if (_applied) _pending.Enqueue((p.Id, p.Data));
        }

        public void OnPickedUp(string id) => _dropped.Remove(id);

        void Spawn(string id, byte[] data)
        {
            var existing = Game.FindById(id);
            if (existing != null)
            {
                if (Game.IsStored(existing)) return; // it's in someone's pocket here; leave it
                Patches.ApplyingRemote = true;
                try { UnityEngine.Object.DestroyImmediate(existing); }
                finally { Patches.ApplyingRemote = false; }
            }

            _spawning = true;
            _s.StartCoroutine(Game.Deserialize(data, go =>
            {
                _spawning = false;
                if (go == null) return;
                go.transform.SetParent(null, true);
                go.SetActive(true);
                Game.Register(go);
            }));
        }

        // ---------- doors ----------

        public void OnLocalDoor(Component openable, bool open, float duration)
        {
            if (!_applied || openable == null) return;
            var player = Game.LocalPlayer;
            if (player == null || Vector3.Distance(player.transform.position, openable.transform.position) > DoorRadius) return;
            var id = Game.GetId(openable.gameObject);
            if (string.IsNullOrEmpty(id)) return;
            if (_doors.TryGetValue(id, out var had) && had.open == open) return;
            _doors[id] = (open, duration);
            _s.Send(new DoorPacket { Id = id, Open = open, Duration = duration });
        }

        public void OnDoor(DoorPacket p)
        {
            if (string.IsNullOrEmpty(p.Id)) return;
            _doors[p.Id] = (p.Open, p.Duration);
            if (_applied) ApplyDoor(p.Id, p.Open, p.Duration);
        }

        void ApplyDoor(string id, bool open, float duration)
        {
            var go = Game.FindById(id);
            var openable = go != null && Game.Openable != null ? go.GetComponent(Game.Openable) : null;
            if (openable == null) return;
            Patches.ApplyingRemote = true;
            try { Game.Call(Game.Openable, openable, "PlayOpenAnimation", open, duration); }
            catch (Exception e) { Game.WarnOnce("door", "Couldn't move a door: " + e.GetBaseException().Message); }
            finally { Patches.ApplyingRemote = false; }
        }
    }
}
