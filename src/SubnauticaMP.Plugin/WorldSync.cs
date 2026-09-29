using System;
using System.Collections.Generic;
using System.Linq;
using SubnauticaMP.Shared;
using UnityEngine;

namespace SubnauticaMP
{
    // Blueprints, scans, databank entries, picked-up/broken items and the time of day.
    internal sealed class WorldSync
    {
        const float SweepSeconds = 2f;
        const double TimeTolerance = 2.0; // seconds of drift before we snap the clock

        readonly Session _s;
        readonly HashSet<string>[] _known = { new HashSet<string>(), new HashSet<string>(), new HashSet<string>() };
        readonly HashSet<string> _removed = new HashSet<string>();
        bool _worldIsNew;   // we're the first one into this server's world: our save seeds it
        bool _seeded;
        bool _applied;      // current save has been brought up to date
        double? _serverTime;
        float _sweepTimer;

        public WorldSync(Session s) { _s = s; }

        public void Reset()
        {
            foreach (var set in _known) set.Clear();
            _removed.Clear();
            _applied = false;
            _seeded = false;
            _serverTime = null;
        }

        public void OnWelcome(WorldState w)
        {
            Reset();
            _known[(int)UnlockKind.Blueprint].UnionWith(w.Blueprints);
            _known[(int)UnlockKind.Analyzed].UnionWith(w.Analyzed);
            _known[(int)UnlockKind.Databank].UnionWith(w.Databank);
            _removed.UnionWith(w.RemovedEntities);
            _worldIsNew = !w.HasTime;
            if (w.HasTime) _serverTime = w.TimePassed;
        }

        // Returns true if the server doesn't know this yet (so it's worth sending).
        public bool RememberUnlock(UnlockKind kind, string key) => _known[(int)kind].Add(key);

        public void Update()
        {
            if (!Game.InWorld) { _applied = false; return; }
            if (!_s.InWorldAndSettled) return;

            if (!_applied)
            {
                _applied = true;
                ApplyAll();
            }

            _sweepTimer += Time.unscaledDeltaTime;
            if (_sweepTimer >= SweepSeconds)
            {
                _sweepTimer = 0f;
                Sweep();
            }
        }

        void ApplyAll()
        {
            int count = 0;
            WithRemote(() =>
            {
                foreach (var k in _known[(int)UnlockKind.Blueprint].ToList()) if (Try(() => Game.AddBlueprint(k))) count++;
                foreach (var k in _known[(int)UnlockKind.Analyzed].ToList()) Try(() => Game.AddAnalyzed(k));
                foreach (var k in _known[(int)UnlockKind.Databank].ToList()) Try(() => Game.AddDatabank(k));
            });

            if (_worldIsNew && !_seeded)
            {
                // First one in: share what our save already knows, and our clock.
                _seeded = true;
                var t = Game.GetTime();
                if (t.HasValue) _s.Send(new TimeSyncPacket { TimePassed = t.Value });
                foreach (var k in Game.KnownBlueprints()) _s.SendUnlock(UnlockKind.Blueprint, k);
                foreach (var k in Game.AnalyzedTech()) _s.SendUnlock(UnlockKind.Analyzed, k);
                foreach (var k in Game.DatabankKeys()) _s.SendUnlock(UnlockKind.Databank, k);
                _s.AddChat("You started this world: your blueprints and time are now shared.");
            }
            else if (_serverTime.HasValue)
            {
                Game.SetTime(_serverTime.Value);
            }

            Sweep();
            _s.AddChat($"World synced: {count} blueprints, {_removed.Count} items already taken.");
        }

        public void OnUnlock(UnlockPacket p)
        {
            if (string.IsNullOrEmpty(p.Key) || !_known[(int)p.Kind].Add(p.Key) || !_applied) return;
            WithRemote(() =>
            {
                switch (p.Kind)
                {
                    case UnlockKind.Blueprint: Try(() => Game.AddBlueprint(p.Key)); break;
                    case UnlockKind.Analyzed: Try(() => Game.AddAnalyzed(p.Key)); break;
                    case UnlockKind.Databank: Try(() => Game.AddDatabank(p.Key)); break;
                }
            });
            if (p.Kind == UnlockKind.Blueprint) _s.AddChat("New blueprint from a teammate: " + p.Key);
        }

        public void OnEntityRemoved(string id)
        {
            if (string.IsNullOrEmpty(id) || !_removed.Add(id) || !_applied) return;
            WithRemote(() => RemoveIfLying(id));
        }

        public void OnTime(double time)
        {
            _serverTime = time;
            if (!_applied) return;
            var local = Game.GetTime();
            if (local.HasValue && Math.Abs(local.Value - time) > TimeTolerance) Game.SetTime(time);
        }

        void Sweep()
        {
            if (_removed.Count == 0) return;
            WithRemote(() =>
            {
                foreach (var id in _removed) RemoveIfLying(id);
            });
        }

        // Only delete things lying in the world, never something already in an inventory or locker.
        static void RemoveIfLying(string id)
        {
            var go = Game.FindById(id);
            if (go == null || !Game.IsPickupOrBreakable(go) || Game.IsStored(go)) return;
            UnityEngine.Object.Destroy(go);
        }

        static void WithRemote(Action a)
        {
            Patches.ApplyingRemote = true;
            try { a(); }
            finally { Patches.ApplyingRemote = false; }
        }

        static bool Try(Action a)
        {
            try { a(); return true; }
            catch (Exception e)
            {
                Game.WarnOnce("apply:" + e.GetBaseException().Message, "Sync apply failed: " + e.GetBaseException().Message);
                return false;
            }
        }
    }
}
