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
        GameObject _pausePage, _pauseButtonTemplate, _pauseHeader;
        bool _pauseEmotes; // showing the emote picker instead of the multiplayer page
        int _emotePage;
        const int EmotesPerPage = 7;
        static Emotes.Info[] _pickList;
        static Emotes.Info[] PickList => _pickList ?? (_pickList = Emotes.All.Where(Emotes.Pickable).ToArray());
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
            if (PausePageShowing) UpdateEmoteKeys();
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

            var header = page.transform.Find("Header");
            _pauseHeader = header != null ? header.gameObject : null;
            if (_pauseHeader != null) UiKit.SetText(_pauseHeader, "Multiplayer");

            // chat box: a copy of the main menu's text box, or any text box the game has loaded
            try
            {
                GameObject input = null;
                foreach (var source in new[] { MainMenuUi.InputPrototype, UiKit.SceneInput() })
                {
                    if (source == null) continue;
                    input = UnityEngine.Object.Instantiate(source, UiKit.Holder, false);
                    if (UiKit.FindInput(input) != null) break;
                    Plugin.Log.LogWarning("Chat box copy has no input: " + UiKit.Describe(input));
                    UnityEngine.Object.DestroyImmediate(input);
                    input = null;
                }
                if (input != null)
                {
                    UiKit.KeepInputOnly(input);
                    UiKit.Place(input, _pauseList);
                    input.name = "SNMP_Chat";
                    input.transform.SetAsFirstSibling();
                    UiKit.GiveHeight(input, 55f);
                    _chatField = UiKit.FindInput(input);
                    UiKit.SetupInput(_chatField, "Type a message and press Enter", false, Protocol.MaxChatLength);
                    if (Game.Get(UiKit.TmpInput, _chatField, "onSubmit") is UnityEvent<string> submit)
                        submit.AddListener(_ => SendChatFromBox());
                }
            }
            catch (Exception e)
            {
                _chatField = null;
                Plugin.Log.LogWarning("No chat box in the pause menu: " + e.GetBaseException().Message);
            }

            UiKit.Place(page, _pauseMenu.transform, false);
            _pausePage = page;
        }

        void SendChatFromBox()
        {
            var text = UiKit.GetInput(_chatField).Trim();
            if (text.Length == 0 || !Joined) return;
            UiKit.SetInput(_chatField, "");
            if (SendChatText(text)) { ClosePauseMenu(); return; } // an emote: go watch... well, everyone else watches
            UiKit.Focus(_chatField);
        }

        // Chat box text: "/e wave" and friends play an emote, anything else goes to the server.
        // Returns true if it was an emote.
        internal bool SendChatText(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0 || !Joined) return false;
            if (Emotes.TryParseCommand(text, out var emote, out var listOnly))
            {
                if (listOnly) { AddChat("Emotes: " + Emotes.List() + ". Type /e <emote> or press " + Plugin.EmoteKey.Value + "."); return false; }
                if (emote == Emote.None) Emoting.Stop();
                else Emoting.Play(emote);
                return true;
            }
            Send(new ChatPacket { Text = text });
            return false;
        }

        // F8 / Enter / the pause menu button. Returns false if the page isn't available (use the old window).
        bool ShowPausePage(bool focusChat, bool emotes = false)
        {
            if (!PauseUiReady) return false;
            if (!_pauseMenu.gameObject.activeInHierarchy) Game.Call(Game.IngameMenu, _pauseMenu, "Open");
            if (!_pauseMenu.gameObject.activeInHierarchy) return false; // the game won't open it right now (cutscene...)
            _pauseSelected = 0;
            _banArmed = false;
            SetEmoteMode(emotes);
            Game.Call(Game.IngameMenu, _pauseMenu, "ChangeSubscreen", PauseScreen);
            if (focusChat && !emotes) UiKit.Focus(_chatField);
            return true;
        }

        void SetEmoteMode(bool emotes)
        {
            _pauseEmotes = emotes;
            _emotePage = 0;
            if (_pauseHeader != null) UiKit.SetText(_pauseHeader, emotes ? "Emotes" : "Multiplayer");
            var chat = _pauseList != null ? _pauseList.Find("SNMP_Chat") : null;
            if (chat != null) chat.gameObject.SetActive(!emotes);
            _pauseShown = null;
            RefreshPausePage();
        }

        // G: open the emote picker, or close it again.
        void ToggleEmotes()
        {
            if (PausePageShowing && _pauseEmotes) { ClosePauseMenu(); return; }
            if (PausePageShowing) { SetEmoteMode(true); return; }
            if (Cursor.lockState != CursorLockMode.Locked || (_pauseMenu != null && _pauseMenu.gameObject.activeInHierarchy)) return; // PDA, inventory, other menus
            if (!ShowPausePage(false, emotes: true))
                AddChat("Emotes: " + Emotes.List() + ". Type /e <emote> in chat.");
        }

        void PickEmote(Emote emote)
        {
            if (emote == Emote.None) Emoting.Stop();
            else Emoting.Play(emote);
            _pauseEmotes = false;
            // a moment later, so the number key you pressed doesn't also switch your quickslot
            RunLater(0.05f, ClosePauseMenu);
        }

        void ClosePauseMenu()
        {
            if (_pauseMenu == null) return;
            _pauseEmotes = false;
            Game.Call(Game.IngameMenu, _pauseMenu, "Close");
        }

        // 1-7 pick an emote while the picker is open.
        void UpdateEmoteKeys()
        {
            if (!_pauseEmotes || !PausePageShowing || UiKit.Typing()) return;
            for (int i = 0; i < EmotesPerPage; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i))
                {
                    int index = _emotePage * EmotesPerPage + i;
                    if (index < PickList.Length) PickEmote(PickList[index].Emote);
                    return;
                }
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
            if (_pauseEmotes && Joined)
            {
                int pages = (PickList.Length + EmotesPerPage - 1) / EmotesPerPage;
                _emotePage = Mathf.Clamp(_emotePage, 0, pages - 1);
                for (int i = 0; i < EmotesPerPage; i++)
                {
                    int index = _emotePage * EmotesPerPage + i;
                    if (index >= PickList.Length) break;
                    var info = PickList[index];
                    PauseButton($"{i + 1}.  {info.Label}", () => PickEmote(info.Emote));
                }
                if (pages > 1) PauseButton(_emotePage + 1 < pages ? "More emotes  >" : "<  First emotes", () =>
                {
                    _emotePage = (_emotePage + 1) % pages;
                    RefreshPausePage();
                });
                if (Emoting.Local != Emote.None) PauseButton("Stop emote", () => PickEmote(Emote.None));
                PauseButton("Back", () => SetEmoteMode(false));
                return;
            }

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
            PauseButton($"Emotes  ({Plugin.EmoteKey.Value})", () => SetEmoteMode(true));
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
