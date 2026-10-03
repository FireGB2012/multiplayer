using UnityEngine;

namespace SubnauticaMP
{
    // Subnautica has no autosave: quit without hitting Save and your inventory, position and anything only your game
    // keeps are gone (bases, unlocks and story come back from the server). This saves for you every few minutes with
    // the game's own Save, when the game itself would allow it (not during the intro, cutscenes, the rocket...).
    internal sealed class AutoSave
    {
        const float RetrySeconds = 30f;

        readonly Session _s;
        float _next = -1f;

        public AutoSave(Session s) { _s = s; }

        public void Update()
        {
            float every = Plugin.AutosaveMinutes.Value * 60f;
            if (every <= 0f || !_s.Joined || !Game.InWorld || _s.Loading || Lobby.Holding || Game.IngameMenu == null)
            {
                _next = -1f;
                return;
            }
            if (_next < 0f) { _next = Time.unscaledTime + every; return; } // first save a while after you arrive
            if (Time.unscaledTime < _next) return;

            var menu = Game.Get(Game.IngameMenu, null, "main") as Component;
            bool busy = menu == null || menu.gameObject.activeInHierarchy   // you're in the pause menu right now
                        || _s.Wheel.IsOpen || _s.Pushing.Knocked || UiKit.Typing()
                        || (Game.TryGet(Game.IngameMenu, menu, "GetAllowSaving") is bool allowed && !allowed);
            if (busy) { _next = Time.unscaledTime + RetrySeconds; return; }

            _next = Time.unscaledTime + every;
            Game.TryDo("autosave", () =>
            {
                Game.Call(Game.IngameMenu, menu, "SaveGame");
                _s.AddChat("Autosaving...");
            });
        }
    }
}
