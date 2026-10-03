using System.Diagnostics;
using UnityEngine;

namespace SubnauticaMP
{
    // After the save loads, keep a loading screen up while everyone else's bases, lockers, dropped items,
    // vehicles and the diver model get put into the world, so all that heavy lifting happens behind it
    // instead of while you're trying to play. Your oxygen / food / water don't drain meanwhile.
    public sealed partial class Session
    {
        const float MaxLoadingSeconds = 90f;
        const float MinLoadingSeconds = 1f;

        bool _loadingDone, _statsFrozen;
        float _loadingTime, _readyTime;
        int _loadTotal;

        public bool Loading => Joined && Game.InWorld && !_loadingDone && !Lobby.Holding;

        int LoadWaiting => Structures.Waiting + Containers.Waiting + Items.Waiting;

        void UpdateLoading()
        {
            if (!Joined || !Game.InWorld)
            {
                _loadingDone = false;
                _loadingTime = _readyTime = 0f;
                _loadTotal = 0;
                UnfreezeStats();
                return;
            }
            if (_loadingDone) return;
            // behind the loading / lobby screen: build the emote wheel's heavy parts now, not on its first open (was a 1 s freeze)
            if (InWorldAndSettled) SafeRun("emote prewarm", () => Wheel.Prewarm());
            if (Lobby.Holding) { UnfreezeStats(); return; } // brand-new world: nothing to load, the lobby screen is up

            _loadingTime += Time.unscaledDeltaTime;
            FreezeStats();
            if (InWorldAndSettled) SafeRun("diver model", () => DiverModel.Prepare());

            int waiting = LoadWaiting;
            _loadTotal = Mathf.Max(_loadTotal, waiting);
            bool ready = InWorldAndSettled && waiting == 0 && !Vehicles.Settling && _client.QueuedCount == 0;
            _readyTime = ready ? _readyTime + Time.unscaledDeltaTime : 0f;

            if (_readyTime >= MinLoadingSeconds || _loadingTime >= MaxLoadingSeconds)
            {
                _loadingDone = true;
                UnfreezeStats();
                Plugin.Log.LogInfo($"Multiplayer world ready after {_loadingTime:0.0}s");
            }
        }

        void FreezeStats()
        {
            if (_statsFrozen) return;
            var p = Game.LocalPlayer;
            if (p == null) return;
            Game.TryDo("freeze stats", () => Game.Call(Game.Player, p, "FreezeStats"));
            _statsFrozen = true;
        }

        void UnfreezeStats()
        {
            if (!_statsFrozen) return;
            _statsFrozen = false;
            var p = Game.LocalPlayer;
            if (p != null) Game.TryDo("unfreeze stats", () => Game.Call(Game.Player, p, "UnfreezeStats"));
        }

        // How long we may spend on network packets this frame: lots while the loading screen hides it.
        float PacketBudgetSeconds => Loading ? 0.03f : 0.006f;

        void PumpPacketsWithBudget()
        {
            var sw = Stopwatch.StartNew();
            float budget = PacketBudgetSeconds;
            while (sw.Elapsed.TotalSeconds < budget && _client.TryDequeue(out var packet))
            {
                try { Handle(packet); }
                catch (System.Exception e) { Plugin.Log.LogError($"Handling {packet.Type} failed: {e}"); }
            }
        }

        float LoadProgress => !InWorldAndSettled ? 0.05f
            : _loadTotal == 0 ? (Vehicles.Settling ? 0.8f : 0.95f)
            : Mathf.Lerp(0.1f, 0.95f, 1f - LoadWaiting / (float)_loadTotal);

        string LoadStep => !InWorldAndSettled ? "Waiting for the world to finish loading..."
            : Structures.Waiting > 0 ? $"Building bases ({Structures.Waiting} left)..."
            : Containers.Waiting > 0 ? $"Filling lockers ({Containers.Waiting} left)..."
            : Items.Waiting > 0 ? $"Placing items ({Items.Waiting} left)..."
            : Vehicles.Settling ? "Bringing in vehicles..."
            : "Almost there...";

        void DrawLoading()
        {
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.blackTexture);
            float w = Mathf.Min(700f, Screen.width - 40f);
            float x = (Screen.width - w) / 2f;
            float y = Screen.height * 0.36f;

            GUI.Label(new Rect(x, y, w, 50), "LOADING MULTIPLAYER WORLD", SnSkin.Title);
            y += 70;

            float progress = LoadProgress;
            GUI.Box(new Rect(x, y, w, 18), GUIContent.none);
            var old = GUI.color;
            GUI.color = SnSkin.Cyan;
            GUI.DrawTexture(new Rect(x + 3, y + 3, (w - 6) * Mathf.Clamp01(progress), 12), Texture2D.whiteTexture);
            GUI.color = old;
            y += 34;

            GUI.Label(new Rect(x, y, w, 30), LoadStep, SnSkin.MidText);
            y += 40;
            GUI.Label(new Rect(x, y, w, 30), $"{_remotes.Count + 1} player(s) in this world", SnSkin.Centered(SnSkin.Small));
        }
    }
}
