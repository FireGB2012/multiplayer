using System;
using UnityEngine;

namespace SubnauticaMP
{
    // The black "waiting for players" and "loading" screens, drawn with the game's own font and button.
    public sealed partial class Session
    {
        Canvas _overlay;
        Component _ovTitle, _ovLine1, _ovLine2, _ovLine3, _ovLine4;
        GameObject _ovStart;
        bool _overlayFailed;

        bool OverlayReady => _overlay != null;

        void EnsureOverlay()
        {
            if (_overlay != null || _overlayFailed || MainMenuUi.TextPrototype == null) return;
            try
            {
                _overlay = UiKit.MakeCanvas("SubnauticaMP_Overlay", 32000);
                var bg = UiKit.SolidImage(_overlay.transform, "Black", Color.black);
                UiKit.Stretch(bg);
                _ovTitle = OverlayText(bg, 52, 200);
                _ovLine1 = OverlayText(bg, 28, 110);
                _ovLine2 = OverlayText(bg, 24, 55);
                _ovLine3 = OverlayText(bg, 24, 0);
                _ovLine4 = OverlayText(bg, 22, -230);
                if (MainMenuUi.ButtonPrototype != null)
                {
                    _ovStart = UiKit.Place(UnityEngine.Object.Instantiate(MainMenuUi.ButtonPrototype, UiKit.Holder, false), bg.transform);
                    _ovStart.name = "Start";
                    UiKit.Pin(_ovStart, new Vector2(0.5f, 0.5f), new Vector2(0, -120), new Vector2(360, 70));
                    UiKit.SetText(_ovStart, "Start");
                    UiKit.OnClick(_ovStart, StartForEveryone);
                }
                _overlay.gameObject.SetActive(false);
            }
            catch (Exception e)
            {
                _overlayFailed = true;
                if (_overlay != null) Destroy(_overlay.gameObject);
                _overlay = null;
                Plugin.Log.LogWarning("Couldn't build the lobby/loading screen from game parts, using the simple one: " + e);
            }
        }

        Component OverlayText(GameObject parent, float size, float y)
        {
            var go = UiKit.Place(UnityEngine.Object.Instantiate(MainMenuUi.TextPrototype, UiKit.Holder, false), parent.transform);
            go.name = "Text";
            UiKit.Pin(go, new Vector2(0.5f, 0.5f), new Vector2(0, y), new Vector2(1600, size * 1.6f));
            var tmp = UiKit.FirstText(go);
            if (tmp != null)
            {
                Game.Set(UiKit.TmpText, tmp, "fontSize", size);
                Game.Set(UiKit.TmpText, tmp, "enableAutoSizing", false);
                var align = UiKit.TmpText.Assembly.GetType("TMPro.TextAlignmentOptions");
                if (align != null) Game.Set(UiKit.TmpText, tmp, "alignment", Enum.Parse(align, "Center"));
            }
            return tmp;
        }

        void UpdateOverlay()
        {
            bool lobby = Lobby.Holding && Joined;
            bool loading = !lobby && Loading;
            if (!lobby && !loading)
            {
                if (_overlay != null && _overlay.gameObject.activeSelf) _overlay.gameObject.SetActive(false);
                return;
            }
            EnsureOverlay();
            if (_overlay == null) return;
            if (!_overlay.gameObject.activeSelf) _overlay.gameObject.SetActive(true);

            if (lobby)
            {
                var names = Plugin.PlayerName.Value;
                foreach (var r in _remotes.Values) if (r != null) names += "     " + r.PlayerName;
                int count = _remotes.Count + 1;
                SetOv(_ovTitle, "WAITING FOR PLAYERS");
                SetOv(_ovLine1, $"{count} {(count == 1 ? "player" : "players")} in the server");
                SetOv(_ovLine2, names);
                SetOv(_ovLine3, IsHost ? $"{_gameMode} mode  ·  Everyone in? Press ENTER or click Start" : $"{_gameMode} mode  ·  Waiting for {HostName} to start the game...");
                SetOv(_ovLine4, IsHost && _joinCode != null ? "Join code: " + _joinCode : "");
                if (_ovStart != null && _ovStart.activeSelf != IsHost) _ovStart.SetActive(IsHost);
            }
            else
            {
                SetOv(_ovTitle, "LOADING MULTIPLAYER WORLD");
                SetOv(_ovLine1, $"{Mathf.RoundToInt(LoadProgress * 100)}%");
                SetOv(_ovLine2, LoadStep);
                SetOv(_ovLine3, $"{_remotes.Count + 1} player(s) in this world");
                SetOv(_ovLine4, "");
                if (_ovStart != null && _ovStart.activeSelf) _ovStart.SetActive(false);
            }
        }

        static void SetOv(Component text, string value)
        {
            if (text == null) return;
            if (Game.Get(UiKit.TmpText, text, "text") as string != value) Game.Set(UiKit.TmpText, text, "text", value);
        }
    }
}
