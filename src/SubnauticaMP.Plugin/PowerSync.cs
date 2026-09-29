using System.Collections.Generic;
using SubnauticaMP.Shared;
using UnityEngine;

namespace SubnauticaMP
{
    // Base power (solar panels, thermal plants, bioreactors, nuclear reactors, and the batteries they fill).
    // One player "runs" each power source: only their game makes power for it and they report the level.
    // Everyone else shows that level and reports what they used, so it comes off the real total.
    internal sealed class PowerSync
    {
        const float ReportSeconds = 2f;
        const float WriterStaleSeconds = 8f;

        readonly Session _s;
        readonly Dictionary<string, (int id, float seen)> _writers = new Dictionary<string, (int, float)>();
        readonly Dictionary<string, float> _applied = new Dictionary<string, float>(); // what we last set / reported
        readonly Dictionary<string, float> _saved = new Dictionary<string, float>();   // from the world file
        float _timer = 1.3f; // offset so the periodic jobs don't all land on the same frame

        public PowerSync(Session s) { _s = s; }

        public void Reset()
        {
            _writers.Clear();
            _applied.Clear();
            _saved.Clear();
        }

        public void OnWelcome(WorldState w)
        {
            Reset();
            foreach (var kv in w.Power) _saved[kv.Key] = kv.Value;
        }

        bool SomeoneElseRuns(string key) =>
            key != null && _writers.TryGetValue(key, out var w) && w.id != _s.LocalId && Time.unscaledTime - w.seen < WriterStaleSeconds;

        // Hook: generators skip making power for sources someone else runs.
        public bool ShouldGenerate(Component generator)
        {
            if (Game.PowerSource == null || generator == null) return true;
            var ps = generator.GetComponent(Game.PowerSource) ?? generator.GetComponentInChildren(Game.PowerSource);
            return ps == null || !SomeoneElseRuns(Anchor.KeyOf(ps.gameObject));
        }

        public void Update()
        {
            if (!_s.InWorldAndSettled || Game.PowerSource == null) return;
            _timer += Time.unscaledDeltaTime;
            if (_timer < ReportSeconds) return;
            _timer = 0f;

            foreach (var ps in SceneIndex.All(Game.PowerSource))
            {
                var key = Anchor.KeyOf(ps.gameObject);
                if (key == null || !(Game.Get(Game.PowerSource, ps, "power") is float power)) continue;

                // first time we see it: start from the shared level
                if (!_applied.ContainsKey(key) && _saved.TryGetValue(key, out var saved))
                {
                    SetPower(ps, saved);
                    power = saved;
                }

                if (SomeoneElseRuns(key))
                {
                    if (_applied.TryGetValue(key, out var had) && had - power > 0.5f)
                        _s.Send(new PowerPacket { Id = key, Drain = had - power });
                    _applied[key] = power;
                }
                else
                {
                    _applied[key] = power;
                    _s.Send(new PowerPacket { Id = key, Power = power });
                }
            }
        }

        public void OnPower(PowerPacket p)
        {
            var ps = Anchor.Find(p.Id, Game.PowerSource);
            if (p.Drain > 0)
            {
                // someone used power from a source we run
                if (ps != null && Game.Get(Game.PowerSource, ps, "power") is float power)
                    SetPower(ps, Mathf.Max(0f, power - p.Drain));
                return;
            }
            _writers[p.Id] = (p.WriterId, Time.unscaledTime);
            _saved[p.Id] = p.Power;
            if (p.WriterId == _s.LocalId || ps == null) return;
            SetPower(ps, p.Power);
            _applied[p.Id] = p.Power;
        }

        static void SetPower(Component ps, float value)
        {
            Patches.ApplyingRemote = true;
            try { Game.Call(Game.PowerSource, ps, "SetPower", value); }
            finally { Patches.ApplyingRemote = false; }
        }
    }
}
