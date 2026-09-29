using System;
using System.Collections.Generic;
using System.Linq;
using SubnauticaMP.Shared;
using UnityEngine;

namespace SubnauticaMP
{
    // Life inside bases and Cyclopses: fabricator animations, fires, hull damage / leaks and welding,
    // fruit picked off plants, and fish born in alien containment.
    internal sealed class BaseLifeSync
    {
        const float FireReportSeconds = 1f;

        readonly Session _s;
        readonly Dictionary<int, Component> _remoteCrafts = new Dictionary<int, Component>(); // CrafterLogic instance id -> logic
        readonly Dictionary<string, string> _lastFires = new Dictionary<string, string>();
        float _fireTimer = 0.25f;

        public BaseLifeSync(Session s) { _s = s; }

        public void Reset()
        {
            _remoteCrafts.Clear();
            _lastFires.Clear();
        }

        public void Update()
        {
            if (!_s.InWorldAndSettled) return;

            // someone else's crafting finished here: the item is theirs, just clear the machine
            foreach (var kv in _remoteCrafts.ToList())
            {
                var logic = kv.Value;
                if (logic == null) { _remoteCrafts.Remove(kv.Key); continue; }
                if (Game.TryGet(logic.GetType(), logic, "progress") is float progress && progress >= 1f) EndRemoteCraft(logic);
            }

            _fireTimer += Time.unscaledDeltaTime;
            if (_fireTimer >= FireReportSeconds)
            {
                _fireTimer = 0f;
                ReportFires();
            }
        }

        // ---------- fabricators ----------

        public void OnLocalCraft(Component logic, object techType, float duration)
        {
            var key = Anchor.KeyOf(logic.gameObject);
            if (key == null || techType == null) return;
            _s.Send(new CraftPacket { CrafterId = key, TechType = techType.ToString(), Duration = duration });
        }

        public void OnCraft(CraftPacket p)
        {
            var logic = Anchor.Find(p.CrafterId, Game.CrafterLogic);
            var tech = Game.ParseTechType(p.TechType);
            if (logic == null || tech == null) return;
            if (Game.TryGet(logic.GetType(), logic, "inProgress") is bool busy && busy) return; // we're using it ourselves
            Patches.ApplyingRemote = true;
            try
            {
                if (Game.Call(logic.GetType(), logic, "Craft", tech, p.Duration) is bool ok && ok)
                {
                    _remoteCrafts[logic.GetInstanceID()] = logic;
                    SetCrafterState(logic, true); // arms + light show
                }
            }
            finally { Patches.ApplyingRemote = false; }
        }

        // Hook: CrafterLogic.TryPickup. Returns false (block) for someone else's item.
        public bool AllowPickup(Component logic)
        {
            if (logic == null || !_remoteCrafts.ContainsKey(logic.GetInstanceID())) return true;
            EndRemoteCraft(logic);
            return false;
        }

        void EndRemoteCraft(Component logic)
        {
            _remoteCrafts.Remove(logic.GetInstanceID());
            Patches.ApplyingRemote = true;
            try
            {
                Game.Call(logic.GetType(), logic, "ResetCrafter");
                SetCrafterState(logic, false);
            }
            finally { Patches.ApplyingRemote = false; }
        }

        static void SetCrafterState(Component logic, bool crafting)
        {
            if (Game.Crafter == null) return;
            var crafter = logic.GetComponent(Game.Crafter) ?? logic.GetComponentInParent(Game.Crafter) ?? logic.GetComponentInChildren(Game.Crafter);
            if (crafter != null) Game.Set(crafter.GetType(), crafter, "state", crafting);
        }

        // ---------- Cyclops fires ----------

        // The Cyclops' driver runs its fires (or the host when nobody drives it).
        public bool RunsFires(Component subFire)
        {
            if (!_s.Joined || subFire == null) return true;
            var id = Game.GetId(subFire.gameObject) ?? (Game.SubRoot != null ? Game.GetId(subFire.GetComponentInParent(Game.SubRoot)?.gameObject) : null);
            int owner = _s.Vehicles.OwnerOf(id);
            return owner == _s.LocalId || (owner == 0 && _s.IsHost);
        }

        static string SubIdOf(Component subFire) =>
            Game.GetId(subFire.gameObject) ?? (Game.SubRoot != null ? Game.GetId(subFire.GetComponentInParent(Game.SubRoot)?.gameObject) : null);

        static IEnumerable<Transform> FireNodes(Component subFire)
        {
            var root = Game.TryGet(Game.SubFire, subFire, "fireSpawnsRoot") as Transform;
            if (root == null) yield break;
            foreach (Transform room in root)
                foreach (Transform node in room)
                    yield return node;
        }

        static Component FireAt(Transform node) => Game.Fire != null ? node.GetComponentInChildren(Game.Fire) : null;

        static bool Burning(Component fire) =>
            fire != null && !(Game.TryGet(Game.Fire, fire, "isExtinguished") is bool done && done);

        void ReportFires()
        {
            if (Game.SubFire == null) return;
            foreach (var subFire in SceneIndex.All(Game.SubFire))
            {
                if (!RunsFires(subFire)) continue;
                var subId = SubIdOf(subFire);
                if (subId == null) continue;
                var nodes = FireNodes(subFire).Where(n => Burning(FireAt(n))).Select(n => Anchor.KeyOf(n.gameObject)).Where(k => k != null).ToList();
                var summary = string.Join(";", nodes.ToArray());
                if (_lastFires.TryGetValue(subId, out var last) && last == summary) continue;
                _lastFires[subId] = summary;
                _s.Send(new FiresPacket { SubId = subId, Nodes = nodes });
            }
        }

        public void OnFires(FiresPacket p)
        {
            var sub = Game.FindById(p.SubId);
            if (sub == null || Game.SubFire == null) return;
            var subFire = sub.GetComponentInChildren(Game.SubFire);
            if (subFire == null || RunsFires(subFire)) return;
            var burning = new HashSet<string>(p.Nodes);

            Patches.ApplyingRemote = true;
            try
            {
                foreach (var node in FireNodes(subFire))
                {
                    var key = Anchor.KeyOf(node.gameObject);
                    var fire = FireAt(node);
                    bool should = key != null && burning.Contains(key);
                    if (should && fire == null)
                    {
                        var spawner = Game.PrefabSpawnBase != null ? node.GetComponent(Game.PrefabSpawnBase) : null;
                        var subRoot = Game.SubRoot != null ? sub.GetComponent(Game.SubRoot) : null;
                        if (spawner != null)
                            Game.Call(Game.PrefabSpawnBase, spawner, "SpawnManual", new Action<GameObject>(go =>
                            {
                                var f = Game.Fire != null ? go.GetComponentInChildren(Game.Fire) : null;
                                if (f != null && subRoot != null) Game.Set(Game.Fire, f, "fireSubRoot", subRoot);
                            }));
                    }
                    else if (!should && Burning(fire))
                    {
                        Game.Call(Game.Fire, fire, "Douse", 10000f);
                    }
                }
            }
            finally { Patches.ApplyingRemote = false; }
        }

        public void OnLocalDouse(Component fire, float amount)
        {
            var key = fire.transform.parent != null ? Anchor.KeyOf(fire.transform.parent.gameObject) : null;
            if (key != null) _s.Send(new FireDousePacket { Node = key, Amount = amount });
        }

        public void OnDouse(FireDousePacket p)
        {
            var node = Anchor.Find(p.Node, typeof(Transform));
            var fire = node != null ? FireAt((Transform)node) : null;
            if (!Burning(fire)) return;
            Patches.ApplyingRemote = true;
            try { Game.Call(Game.Fire, fire, "Douse", p.Amount); }
            finally { Patches.ApplyingRemote = false; }
        }

        // ---------- hull damage / leaks / welding ----------

        // Only bases and Cyclopses; crushing and fire damage happen on every PC by themselves.
        public static bool IsHull(Component live) =>
            live != null && ((Game.Base != null && live.GetComponentInParent(Game.Base) != null) ||
                             (Game.SubRoot != null && live.GetComponentInParent(Game.SubRoot) is Component sub && Game.IsCyclops(sub)));

        public void OnLocalHullChange(Component live, float delta)
        {
            if (Mathf.Abs(delta) < 0.01f) return;
            var key = Anchor.KeyOf(live.gameObject);
            if (key != null) _s.Send(new HullHealthPacket { Key = key, Delta = delta });
        }

        public void OnHull(HullHealthPacket p)
        {
            var live = Anchor.Find(p.Key, Game.LiveMixin);
            if (live == null) return;
            Patches.ApplyingRemote = true;
            try
            {
                if (p.Delta < 0) Game.Call(Game.LiveMixin, live, "TakeDamage", -p.Delta);
                else Game.Call(Game.LiveMixin, live, "AddHealth", p.Delta);
            }
            finally { Patches.ApplyingRemote = false; }
        }

        // ---------- fruit ----------

        public void OnLocalPicked(Component pick)
        {
            var key = Anchor.KeyOf(pick.gameObject);
            if (key != null) _s.Send(new PickedPacket { Key = key });
        }

        public void OnPicked(PickedPacket p)
        {
            var pick = Anchor.Find(p.Key, Game.PickPrefab);
            if (pick == null || (Game.TryGet(Game.PickPrefab, pick, "GetPickedState") is bool picked && picked)) return;
            Patches.ApplyingRemote = true;
            try { Game.Call(Game.PickPrefab, pick, "SetPickedUp"); }
            finally { Patches.ApplyingRemote = false; }
        }

        // ---------- alien containment ----------

        // A fish / egg arriving from someone else: if it's inside a containment tank, put it in properly.
        public static void PutInWaterPark(GameObject item)
        {
            if (Game.WaterPark == null || Game.Pickupable == null || item == null) return;
            var pickupable = item.GetComponent(Game.Pickupable);
            if (pickupable == null) return;
            foreach (var park in SceneIndex.All(Game.WaterPark))
            {
                if (!(Game.Call(Game.WaterPark, park, "IsPointInside", item.transform.position) is bool inside) || !inside) continue;
                Patches.ApplyingRemote = true;
                try { Game.Call(Game.WaterPark, park, "AddItem", pickupable); }
                finally { Patches.ApplyingRemote = false; }
                return;
            }
        }
    }
}
