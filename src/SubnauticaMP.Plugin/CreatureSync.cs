using System;
using System.Collections.Generic;
using System.Linq;
using SubnauticaMP.Shared;
using UnityEngine;

namespace SubnauticaMP
{
    // Creatures, Nitrox-style:
    //  - everyone rolls the same thing for each entity slot (the server keeps the first roll), and the things
    //    spawned there get the same id on every PC ("s:<slot position>#<n>")
    //  - each creature near players is run by one player (its owner); everyone else sees a puppet that follows
    //  - a creature chasing someone gets handed to that player, so the real attack code hits the real player
    internal sealed class CreatureSync
    {
        public const string IdPrefix = "s:";
        const float ClaimRange = 80f, ReleaseRange = 120f, StreamRange = 150f;
        const float ScanSeconds = 1f, StreamSeconds = 0.125f, SlotFlushSeconds = 0.5f, PendingSeconds = 5f;

        sealed class Tracked
        {
            public GameObject Go;
            public Component Creature;
            public bool Puppet;
            public bool HasTarget;
            public Vector3 TargetPos;
            public Quaternion TargetRot;
            public float Aggression;
        }

        readonly Session _s;
        readonly Dictionary<string, SpawnSlot> _book = new Dictionary<string, SpawnSlot>();
        readonly List<SpawnSlot> _newSlots = new List<SpawnSlot>();
        readonly Dictionary<string, int> _owners = new Dictionary<string, int>();
        readonly Dictionary<string, Tracked> _tracked = new Dictionary<string, Tracked>();
        readonly HashSet<int> _puppets = new HashSet<int>(); // GameObject instance ids, checked from hooks
        readonly HashSet<string> _pending = new HashSet<string>(); // asked the server, no answer yet
        readonly HashSet<string> _dead = new HashSet<string>();
        float _scanTimer = 0.5f, _streamTimer, _slotTimer = 0.2f, _pendingTimer;
        object _ecoType; // the local player's EcoTarget type, copied onto remote divers

        public CreatureSync(Session s) { _s = s; }

        public void Reset()
        {
            foreach (var t in _tracked.Values) if (t.Puppet && t.Go != null) SetPuppet(t, false);
            _book.Clear();
            _newSlots.Clear();
            _owners.Clear();
            _tracked.Clear();
            _puppets.Clear();
            _pending.Clear();
            _dead.Clear();
        }

        public void OnWelcome(WorldState w)
        {
            Reset();
            foreach (var kv in w.SpawnBook) _book[kv.Key] = kv.Value;
        }

        // ---------- same spawns for everyone (called from the hooks) ----------

        public static string SlotKey(Vector3 p) =>
            IdPrefix + Round(p.x) + "," + Round(p.y) + "," + Round(p.z);

        static string Round(float v) => (Mathf.Round(v * 10f) / 10f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

        // Returns the shared roll for this slot, or remembers ours as the first one.
        public bool TryGetSlot(string key, out SpawnSlot slot) => _book.TryGetValue(key, out slot);

        public void RecordSlot(string key, string classId, int count)
        {
            var slot = new SpawnSlot { Key = key, ClassId = classId ?? "", Count = count };
            _book[key] = slot;
            _newSlots.Add(slot);
        }

        public void OnSlots(SpawnSlotsPacket p)
        {
            foreach (var slot in p.Slots) _book[slot.Key] = slot;
        }

        // ---------- ownership ----------

        public bool IsPuppet(GameObject go) => go != null && _puppets.Contains(go.GetInstanceID());

        int OwnerOf(string id) => _owners.TryGetValue(id, out var o) ? o : 0;

        public void OnOwner(CreatureOwnerPacket p)
        {
            foreach (var id in p.Ids)
            {
                _pending.Remove(id);
                if (p.OwnerId == 0) _owners.Remove(id);
                else _owners[id] = p.OwnerId;
                if (_tracked.TryGetValue(id, out var t)) Refresh(t, id);
            }
        }

        void Refresh(Tracked t, string id)
        {
            int owner = OwnerOf(id);
            bool puppet = owner != 0 && owner != _s.LocalId && !_dead.Contains(id);
            if (puppet != t.Puppet) SetPuppet(t, puppet);
        }

        void SetPuppet(Tracked t, bool puppet)
        {
            t.Puppet = puppet;
            t.HasTarget = false;
            if (t.Go == null) return;
            int key = t.Go.GetInstanceID();
            if (puppet) _puppets.Add(key); else _puppets.Remove(key);
            Game.SetRemoteDriven(t.Go, puppet);
        }

        // ---------- per frame ----------

        public void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (!_s.InWorldAndSettled) return;

            _slotTimer += dt;
            if (_slotTimer >= SlotFlushSeconds)
            {
                _slotTimer = 0f;
                FlushSlots();
            }

            _pendingTimer += dt;
            if (_pendingTimer >= PendingSeconds)
            {
                _pendingTimer = 0f;
                _pending.Clear(); // lost answers: ask again
            }

            _scanTimer += dt;
            if (_scanTimer >= ScanSeconds)
            {
                _scanTimer = 0f;
                Scan();
                TagRemoteDivers();
            }

            _streamTimer += dt;
            if (_streamTimer >= StreamSeconds)
            {
                _streamTimer = 0f;
                Stream();
            }

            // puppets glide to where their owner says they are
            float k = 1f - Mathf.Exp(-8f * Time.deltaTime);
            foreach (var t in _tracked.Values)
            {
                if (!t.Puppet || !t.HasTarget || t.Go == null) continue;
                var tr = t.Go.transform;
                tr.position = Vector3.Lerp(tr.position, t.TargetPos, k);
                tr.rotation = Quaternion.Slerp(tr.rotation, t.TargetRot, k);
            }
        }

        void FlushSlots()
        {
            while (_newSlots.Count > 0)
            {
                int n = Math.Min(_newSlots.Count, SpawnSlotsPacket.MaxSlots);
                _s.Send(new SpawnSlotsPacket { Slots = _newSlots.GetRange(0, n) });
                _newSlots.RemoveRange(0, n);
            }
        }

        void Scan()
        {
            if (Game.Creature == null) return;
            var me = Game.LocalPlayer;
            if (me == null) return;
            var myPos = me.transform.position;
            int myId = _s.LocalId;

            var seen = new HashSet<string>();
            foreach (var c in SceneIndex.All(Game.Creature))
            {
                var id = Game.GetId(c.gameObject);
                if (id == null || !id.StartsWith(IdPrefix) || _dead.Contains(id)) continue;
                seen.Add(id);
                if (!_tracked.TryGetValue(id, out var t) || t.Go != c.gameObject)
                {
                    if (t != null && t.Puppet) SetPuppet(t, false);
                    t = _tracked[id] = new Tracked { Go = c.gameObject, Creature = c };
                }
                Refresh(t, id);
            }

            // gone (unloaded / destroyed): let go of ours
            var release = new List<string>();
            foreach (var id in _tracked.Keys.ToList())
            {
                if (seen.Contains(id)) continue;
                var t = _tracked[id];
                if (t.Go != null) _puppets.Remove(t.Go.GetInstanceID());
                _tracked.Remove(id);
                if (OwnerOf(id) == myId) release.Add(id);
            }

            var claim = new List<string>();
            var handoffs = new Dictionary<int, List<string>>();
            foreach (var kv in _tracked)
            {
                var id = kv.Key;
                var t = kv.Value;
                if (_pending.Contains(id)) continue;
                int owner = OwnerOf(id);
                float dist = Vector3.Distance(myPos, t.Go.transform.position);
                if (owner == 0 && dist <= ClaimRange) claim.Add(id);
                else if (owner == myId)
                {
                    var victim = ChasedRemote(t);
                    if (victim != null)
                    {
                        if (!handoffs.TryGetValue(victim.Id, out var list)) handoffs[victim.Id] = list = new List<string>();
                        list.Add(id);
                    }
                    else if (dist > ReleaseRange) release.Add(id);
                }
            }

            Ask(claim, myId);
            Ask(release, 0);
            foreach (var h in handoffs) Ask(h.Value, h.Key);
        }

        void Ask(List<string> ids, int owner)
        {
            for (int i = 0; i < ids.Count; i += CreatureOwnerPacket.MaxIds)
            {
                var chunk = ids.Skip(i).Take(CreatureOwnerPacket.MaxIds).ToList();
                foreach (var id in chunk) _pending.Add(id);
                _s.Send(new CreatureOwnerPacket { OwnerId = owner, Ids = chunk });
            }
        }

        // The remote diver this creature is going after, if any.
        RemotePlayer ChasedRemote(Tracked t)
        {
            if (Game.LastTarget == null) return null;
            var lt = t.Go.GetComponent(Game.LastTarget);
            if (lt == null) return null;
            var target = Game.TryGet(Game.LastTarget, lt, "target") as GameObject;
            if (target == null) return null;
            var remote = target.GetComponentInParent<RemotePlayer>();
            return remote != null && remote.Exposed && Vector3.Distance(target.transform.position, t.Go.transform.position) < 40f ? remote : null;
        }

        void Stream()
        {
            var remotes = _s.Remotes.Where(r => r != null && r.gameObject.activeSelf).Select(r => r.transform.position).ToList();
            if (remotes.Count == 0) return;
            int myId = _s.LocalId;
            var p = new CreatureStatesPacket();
            foreach (var kv in _tracked)
            {
                var t = kv.Value;
                if (t.Go == null || OwnerOf(kv.Key) != myId) continue;
                var pos = t.Go.transform.position;
                if (!remotes.Any(r => Vector3.Distance(r, pos) <= StreamRange)) continue;
                var rot = t.Go.transform.rotation;
                p.States.Add(new CreatureState
                {
                    Id = kv.Key,
                    Position = new Vec3(pos.x, pos.y, pos.z),
                    Rotation = new Quat(rot.x, rot.y, rot.z, rot.w),
                    Aggression = TraitValue(t.Creature),
                });
                if (p.States.Count >= CreatureStatesPacket.MaxStates) { _s.Send(p); p = new CreatureStatesPacket(); }
            }
            if (p.States.Count > 0) _s.Send(p);
        }

        static float TraitValue(Component creature)
        {
            var trait = Game.TryGet(Game.Creature, creature, "Aggression");
            return trait != null && Game.TryGet(trait.GetType(), trait, "Value") is float f ? f : 0f;
        }

        public void OnStates(CreatureStatesPacket p)
        {
            foreach (var st in p.States)
            {
                if (!_tracked.TryGetValue(st.Id, out var t) || !t.Puppet || t.Go == null) continue;
                t.TargetPos = new Vector3(st.Position.X, st.Position.Y, st.Position.Z);
                t.TargetRot = new Quaternion(st.Rotation.X, st.Rotation.Y, st.Rotation.Z, st.Rotation.W);
                if (!t.HasTarget && Vector3.Distance(t.Go.transform.position, t.TargetPos) > 20f)
                    t.Go.transform.SetPositionAndRotation(t.TargetPos, t.TargetRot);
                t.HasTarget = true;
                if (Math.Abs(st.Aggression - t.Aggression) > 0.05f)
                {
                    t.Aggression = st.Aggression;
                    var trait = Game.TryGet(Game.Creature, t.Creature, "Aggression");
                    if (trait != null) Game.Set(trait.GetType(), trait, "Value", st.Aggression);
                }
            }
        }

        // Creatures only notice things with an EcoTarget: give remote divers the same one the local player has.
        void TagRemoteDivers()
        {
            if (Game.EcoTarget == null) return;
            if (_ecoType == null)
            {
                var me = Game.LocalPlayer;
                var mine = me != null ? me.GetComponent(Game.EcoTarget) : null;
                if (mine == null) return;
                _ecoType = Game.Get(Game.EcoTarget, mine, "type");
            }
            foreach (var r in _s.Remotes)
            {
                if (r == null) continue;
                var eco = r.GetComponent(Game.EcoTarget) as Behaviour;
                if (eco == null)
                {
                    eco = r.gameObject.AddComponent(Game.EcoTarget) as Behaviour;
                    if (eco == null) continue;
                    Game.Call(Game.EcoTarget, eco, "SetTargetType", _ecoType);
                }
                if (eco.enabled != r.Exposed) eco.enabled = r.Exposed;
            }
        }

        // ---------- damage + death ----------

        // A hit on someone else's creature: send it to them instead. Returns true when handled.
        public bool ForwardDamage(GameObject go, float damage, int type, Vector3 pos)
        {
            if (!IsPuppet(go)) return false;
            var id = Game.GetId(go);
            if (id == null) return false;
            _s.Send(new CreatureDamagePacket { Id = id, Damage = damage, DamageType = type, Position = new Vec3(pos.x, pos.y, pos.z) });
            return true;
        }

        public void OnDamage(CreatureDamagePacket p)
        {
            var go = Game.FindById(p.Id);
            var live = go != null && Game.LiveMixin != null ? go.GetComponent(Game.LiveMixin) : null;
            if (live == null || IsPuppet(go)) return;
            var m = Game.FindMethod(Game.LiveMixin, "TakeDamage", typeof(float));
            if (m == null) return;
            var args = new object[m.GetParameters().Length];
            args[0] = p.Damage;
            if (args.Length > 1) args[1] = new Vector3(p.Position.X, p.Position.Y, p.Position.Z);
            if (args.Length > 2) args[2] = Enum.ToObject(m.GetParameters()[2].ParameterType, p.DamageType);
            m.Invoke(live, args); // not "remote": if it dies here, everyone hears about it
        }

        // A synced creature died on this PC.
        public void OnLocalDeath(GameObject go)
        {
            var id = Game.GetId(go);
            if (id == null || !id.StartsWith(IdPrefix) || go.GetComponent(Game.Creature) == null || !_dead.Add(id)) return;
            if (_tracked.TryGetValue(id, out var t) && t.Puppet) SetPuppet(t, false);
            _tracked.Remove(id);
            _s.Send(new CreatureDiedPacket { Id = id });
        }

        public void OnDied(string id)
        {
            if (string.IsNullOrEmpty(id) || !_dead.Add(id)) return;
            _owners.Remove(id);
            if (_tracked.TryGetValue(id, out var t) && t.Puppet) SetPuppet(t, false);
            _tracked.Remove(id);
            var go = Game.FindById(id);
            var live = go != null && Game.LiveMixin != null ? go.GetComponent(Game.LiveMixin) : null;
            if (live == null) return;
            if (Game.Call(Game.LiveMixin, live, "IsAlive") is bool alive && !alive) return;
            Patches.ApplyingRemote = true;
            try { Game.Call(Game.LiveMixin, live, "Kill"); }
            finally { Patches.ApplyingRemote = false; }
        }
    }
}
