using System.Collections.Generic;
using System.Linq;
using SubnauticaMP.Shared;
using UnityEngine;

namespace SubnauticaMP
{
    // Beds: the night only gets skipped when everyone is in bed at the same time.
    // Until then you lie there with the sleep screen and the clock running normally.
    internal sealed class SleepSync
    {
        const float GiveUpSeconds = 20f;
        const float SkipRealSeconds = 5f; // same fade the game uses

        readonly Session _s;
        readonly HashSet<int> _asleep = new HashSet<int>();
        bool _waiting;
        float _waitStart;

        public SleepSync(Session s) { _s = s; }

        public bool LocalAsleep => _waiting || Skipping;
        public bool Skipping { get; private set; }

        public void Reset()
        {
            _asleep.Clear();
            _waiting = false;
            Skipping = false;
        }

        // Hook: DayNightCycle.SkipTime (the bed asks for the night to be skipped). Return false = we handle it.
        public bool OnLocalSkip(Component dayNight, float amount)
        {
            if (Patches.ApplyingRemote || !_s.Joined) return true;
            // hold the bed in "sleeping" without speeding up the clock
            var now = Game.GetTime() ?? 0;
            Game.Set(Game.DayNightCycle, dayNight, "skipTimeMode", true);
            Game.Set(Game.DayNightCycle, dayNight, "skipModeEndTime", now + 1e7);
            _waiting = true;
            _waitStart = Time.unscaledTime;
            _s.Send(new SleepPacket { Asleep = true, Amount = amount });
            int total = _s.Remotes.Count() + 1;
            if (total > 1) _s.AddChat($"Waiting for everyone to get in bed ({_asleep.Count(id => id != _s.LocalId) + 1}/{total})...");
            return false;
        }

        public void Update()
        {
            var dayNight = Game.DayNight;
            if (dayNight == null) return;
            bool skipMode = Game.TryGet(Game.DayNightCycle, dayNight, "IsInSkipTimeMode") is bool b && b;

            if (_waiting)
            {
                if (!skipMode) GotUp(); // left the bed some other way
                else if (Time.unscaledTime - _waitStart > GiveUpSeconds)
                {
                    _s.AddChat("Not everyone went to bed. You can only sleep through the night together.");
                    Patches.ApplyingRemote = true;
                    try { Game.Call(Game.DayNightCycle, dayNight, "StopSkipTimeMode"); } // the bed stands you back up
                    finally { Patches.ApplyingRemote = false; }
                    GotUp();
                }
            }
            if (Skipping && !skipMode) Skipping = false;
        }

        void GotUp()
        {
            _waiting = false;
            _s.Send(new SleepPacket { Asleep = false });
        }

        public void OnSleep(SleepPacket p)
        {
            if (p.Skip)
            {
                _asleep.Clear();
                if (!_waiting) return; // not in bed: the clock just jumps with the next time sync
                _waiting = false;
                Skipping = true;
                var dayNight = Game.DayNight;
                if (dayNight == null) return;
                Patches.ApplyingRemote = true;
                try
                {
                    Game.Set(Game.DayNightCycle, dayNight, "skipTimeMode", false);
                    Game.Call(Game.DayNightCycle, dayNight, "SkipTime", p.Amount, SkipRealSeconds);
                }
                finally { Patches.ApplyingRemote = false; }
                _s.AddChat("Everyone's asleep. Good night!");
                return;
            }

            if (p.Asleep) _asleep.Add(p.Id); else _asleep.Remove(p.Id);
            if (p.Id != _s.LocalId && p.Asleep)
                _s.AddChat($"{_s.NameOf(p.Id)} went to bed ({_asleep.Count}/{_s.Remotes.Count() + 1}). Sleep too to skip the night.");
        }
    }
}
