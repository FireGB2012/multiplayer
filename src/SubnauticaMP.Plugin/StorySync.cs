using System;
using System.Collections.Generic;
using System.Linq;
using SubnauticaMP.Shared;

namespace SubnauticaMP
{
    // Story events: radio messages, the Sunbeam, Precursor progress... are all "story goals" in the game.
    // When one fires for someone it fires for everyone, and the Aurora uses one shared explosion time.
    internal sealed class StorySync
    {
        readonly Session _s;
        readonly List<StoryGoalPacket> _goals = new List<StoryGoalPacket>();
        readonly HashSet<string> _known = new HashSet<string>();
        AuroraPacket _aurora;
        bool _applied;

        public StorySync(Session s) { _s = s; }

        public void Reset()
        {
            _goals.Clear();
            _known.Clear();
            _aurora = null;
            _applied = false;
        }

        public void OnWelcome(WorldState w)
        {
            Reset();
            foreach (var g in w.StoryGoals) Remember(g);
            _aurora = w.Aurora;
        }

        bool Remember(StoryGoalPacket g)
        {
            if (!_known.Add(g.Key)) return false;
            _goals.Add(g);
            return true;
        }

        public void Update()
        {
            if (!Game.InWorld) { _applied = false; return; }
            if (!_s.InWorldAndSettled || _applied) return;
            _applied = true;

            // Aurora: copy the shared timing, or be the one who shares it
            if (_aurora != null) Try(() => Game.ApplyAurora(_aurora));
            else if (_s.World.SeedsWorld)
            {
                var mine = Game.ReadAurora();
                if (mine != null) { _aurora = mine; _s.Send(mine); }
            }

            // catch up on everything that already happened in this world
            foreach (var g in _goals.ToList()) Apply(g);
        }

        // Harmony hook on StoryGoal.Execute.
        public void OnLocalGoal(string key, int goalType)
        {
            if (!_applied || string.IsNullOrEmpty(key)) return;
            var g = new StoryGoalPacket { Key = key, GoalType = goalType };
            if (Remember(g)) _s.Send(g);
        }

        public void OnGoal(StoryGoalPacket g)
        {
            if (string.IsNullOrEmpty(g.Key) || !Remember(g) || !_applied) return;
            Apply(g);
        }

        public void OnAurora(AuroraPacket a)
        {
            _aurora = a;
            if (_applied) Try(() => Game.ApplyAurora(a));
        }

        static void Apply(StoryGoalPacket g)
        {
            Patches.ApplyingRemote = true;
            try { Try(() => Game.RunGoal(g.Key, g.GoalType)); }
            finally { Patches.ApplyingRemote = false; }
        }

        static void Try(Action a)
        {
            try { a(); }
            catch (Exception e) { Game.WarnOnce("story:" + e.GetBaseException().Message, "Story sync: " + e.GetBaseException().Message); }
        }
    }
}
