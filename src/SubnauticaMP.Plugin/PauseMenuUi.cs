using System;
using System.Linq;
using SubnauticaMP.Shared;
using UnityEngine;
using UnityEngine.Events;

namespace SubnauticaMP
{
    // In game: a "Multiplayer" button in the game's own pause menu, opening a page made from a copy of the
    // pause menu itself: chat box, join code, players (host can kick / ban), leave. F8 / Enter open it too.
    public sealed partial class Session
    {
        const string PauseScreen = "SNMP_Multiplayer";

        Component _pauseMenu, _chatField;
        GameObject _pausePage, _pauseButtonTemplate;
        Transform _pauseList;
        bool _pauseFailed, _pauseDumped, _banArmed;
        int _pauseSelected;
        float _pauseRefreshAt;

        bool PauseUiReady => _pausePage != null && _pauseMenu != null;
        bool PausePageShowing => _pausePage != null && _pausePage.activeInHierarchy;

        void UpdatePauseUi()
        {
            if (!Game.InWorld || Game.IngameMenu == null) { _pauseMenu = null; _pausePage = null; return; }
            var menu = Game.Get(Game.IngameMenu, null, "main") as Component
                       ?? Resources.FindObjectsOfTypeAll(Game.IngameMenu).OfType<Component>().FirstOrDefault(c => c != null && c.gameObject.scene.IsValid());
            if (menu == null) return;
            if (menu != _pauseMenu) { _pauseMenu = menu; _pausePage = null; _pauseFailed = false; }
            if (_pausePage == null && !_pauseFailed)
            {
                try { BuildPauseUi(); Plugin.Log.LogInfo("Multiplayer page added to the pause menu"); }
                catch (Exception e)
                {
                    _pauseFailed = true;
                    _pausePage = null;
                    Plugin.Log.LogWarning("Couldn't add multiplayer to the pause menu, F8 uses the simple window: " + e);
                }
            }
            if (PausePageShowing && Time.unscaledTime >= _pauseRefreshAt) RefreshPausePage();
        }

        void BuildPauseUi()
        {
            if (!_pauseDumped) { _pauseDumped = true; UiKit.Dump(_pauseMenu.transform, "pause menu", 5); }
            var main = Game.Get(Game.IngameMenu, _pauseMenu, "mainPanel") as GameObject ?? _pauseMenu.transform.Find("Main")?.gameObject;
            if (main == null) throw new Exception("pause menu main panel missing");
            var buttons = main.GetComponentsInChildren(UiKit.Button, true);
            if (buttons.Length == 0) throw new Exception("no buttons in the pause menu");
            var first = buttons.FirstOrDefault(b => UiKit.ClickTarget(b).Contains("Close")) ?? buttons[0];

            // our button, right under "Resume"
            var oldButton = first.transform.parent.Find("SNMP_ButtonMultiplayer");
            if (oldButton != null) UnityEngine.Object.DestroyImmediate(oldButton.gameObject);
            var mp = UnityEngine.Object.Instantiate(first.gameObject, first.transform.parent);
            mp.name = "SNMP_ButtonMultiplayer";
            mp.transform.SetSiblingIndex(first.transform.GetSiblingIndex() + 1);
            UiKit.SetText(mp, "Multiplayer");
            UiKit.OnClick(mp, () => ShowPausePage(false));

            // our page: a copy of the main pause panel with its buttons swapped for ours
            var oldPage = _pauseMenu.transform.Find(PauseScreen);
            if (oldPage != null) UnityEngine.Object.DestroyImmediate(oldPage.gameObject);
            var page = UiKit.Copy(main, PauseScreen);
            var copied = page.GetComponentsInChildren(UiKit.Button, true);
            _pauseButtonTemplate = UiKit.Copy(copied[0].gameObject, "SNMP_PauseButton");
            UiKit.StopTranslating(_pauseButtonTemplate);
            _pauseList = copied[0].transform.parent;
            foreach (var b in copied) UnityEngine.Object.DestroyImmediate(b.gameObject);

            if (MainMenuUi.InputPrototype != null)
            {
                var input = UiKit.Place(UnityEngine.Object.Instantiate(MainMenuUi.InputPrototype, UiKit.Holder, false), _pauseList);
                input.name = "SNMP_Chat";
                _chatField = UiKit.FindInput(input);
                UiKit.SetupInput(_chatField, "Type a message and press Enter", false, Protocol.MaxChatLength);
                if (Game.Get(UiKit.TmpInput, _chatField, "onSubmit") is UnityEvent<string> submit)
                    submit.AddListener(_ => SendChatFromBox());
            }

            UiKit.Place(page, _pauseMenu.transform, false);
            _pausePage = page;
        }

        void SendChatFromBox()
        {
            var text = UiKit.GetInput(_chatField).Trim();
            if (text.Length == 0 || !Joined) return;
            Send(new ChatPacket { Text = text });
            UiKit.SetInput(_chatField, "");
            UiKit.Focus(_chatField);
        }

        // F8 / Enter / the pause menu button. Returns false if the page isn't available (use the old window).
        bool ShowPausePage(bool focusChat)
        {
            if (!PauseUiReady) return false;
            if (!_pauseMenu.gameObject.activeInHierarchy) Game.Call(Game.IngameMenu, _pauseMenu, "Open");
            if (!_pauseMenu.gameObject.activeInHierarchy) return false; // the game won't open it right now (cutscene...)
            _pauseSelected = 0;
            _banArmed = false;
            _pauseShown = null;
            RefreshPausePage();
            Game.Call(Game.IngameMenu, _pauseMenu, "ChangeSubscreen", PauseScreen);
            if (focusChat) UiKit.Focus(_chatField);
            return true;
        }

        void BackToPauseMain() => Game.Call(Game.IngameMenu, _pauseMenu, "ChangeSubscreen", "Main");

        void RefreshPausePage()
        {
            if (_pauseList == null) return;
            _pauseRefreshAt = Time.unscaledTime + 1f;
            _pauseRows.Clear();
            try { FillPauseRows(); }
            finally { CommitPauseRows(); }
        }

        void FillPauseRows()
        {
            if (!Joined)
            {
                PauseButton("Not connected. Host or join from the main menu.", null);
                PauseButton("Back", BackToPauseMain);
                return;
            }

            if (_pauseSelected != 0 && _remotes.TryGetValue(_pauseSelected, out var who) && who != null)
            {
                int id = _pauseSelected;
                PauseButton($"Kick {who.PlayerName}", () => { Send(new KickPacket { TargetId = id }); _pauseSelected = 0; RefreshPausePage(); });
                PauseButton(_banArmed ? $"Really ban {who.PlayerName}? Click again" : $"Ban {who.PlayerName}", () =>
                {
                    if (!_banArmed) { _banArmed = true; RefreshPausePage(); return; }
                    Send(new KickPacket { TargetId = id, Ban = true });
                    _pauseSelected = 0;
                    _banArmed = false;
                    RefreshPausePage();
                });
                PauseButton("Back to players", () => { _pauseSelected = 0; _banArmed = false; RefreshPausePage(); });
                return;
            }
            _pauseSelected = 0;

            if (_chatField != null) PauseButton("Send message", SendChatFromBox);
            if (_joinCode != null) PauseButton($"Join code: {_joinCode}  (click to copy)", () => { GUIUtility.systemCopyBuffer = _joinCode; AddChat("Join code copied: " + _joinCode); });

            PauseButton($"{Plugin.PlayerName.Value} (you)" + (IsHost ? "  ·  host" : ""), null);
            foreach (var r in _remotes.Values.Where(r => r != null).OrderBy(r => r.Id).ToList())
            {
                var label = r.PlayerName + (r.Id == _hostId ? "  ·  host" : "") +
                            (r.HasVitals ? $"  ·  HP {r.Health}  Food {r.Food}  Water {r.Water}" : "") + (r.Sleeping ? "  ·  asleep" : "");
                int id = r.Id;
                PauseButton(label, IsHost ? () => { _pauseSelected = id; _banArmed = false; RefreshPausePage(); } : (Action)null);
            }
            PauseButton("Leave server", () => { Leave(); BackToPauseMain(); });
            PauseButton("Back", BackToPauseMain);
        }

        readonly System.Collections.Generic.List<(string text, Action click)> _pauseRows = new System.Collections.Generic.List<(string, Action)>();
        string _pauseShown;

        void PauseButton(string text, Action click) => _pauseRows.Add((text, click));

        // Builds the buttons collected by RefreshPausePage, but only if the page actually changed
        // (rebuilding every second would make buttons flicker under the mouse).
        void CommitPauseRows()
        {
            var key = string.Join("\n", _pauseRows.Select(r => r.text + (r.click == null ? "" : "*")).ToArray());
            var rows = _pauseRows.ToList();
            _pauseRows.Clear();
            if (key == _pauseShown) return;
            _pauseShown = key;
            foreach (Transform child in _pauseList.Cast<Transform>().ToList())
                if (child.name != "SNMP_Chat") UnityEngine.Object.Destroy(child.gameObject);
            foreach (var (text, click) in rows) MakePauseButton(text, click);
        }

        void MakePauseButton(string text, Action click)
        {
            var b = UiKit.Place(UnityEngine.Object.Instantiate(_pauseButtonTemplate, UiKit.Holder, false), _pauseList);
            b.name = "SNMP_Button";
            UiKit.SetText(b, text);
            UiKit.OnClick(b, click ?? (() => { }));
            if (click == null) UiKit.SetInteractable(b, false);
        }
    }
}
