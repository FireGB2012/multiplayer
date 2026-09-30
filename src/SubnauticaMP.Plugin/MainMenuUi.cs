using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SubnauticaMP.Shared;
using UnityEngine;

namespace SubnauticaMP
{
    // The Multiplayer screen in the main menu, built out of copies of the game's own menu:
    //  - the list is a copy of the "Load game" panel, each world/server row is a real save-slot row
    //  - "Host a world" is a copy of the "New game" panel (same Survival/Freedom/Hardcore/Creative buttons)
    //  - text boxes are copies of the main menu's email box
    // If anything about the game's menu isn't where we expect, the old window is used instead.
    internal sealed class MainMenuUi
    {
        public const string ListGroup = "SNMP_Multiplayer", HostGroup = "SNMP_Host", AddGroup = "SNMP_Add", LobbyGroup = "SNMP_Lobby";

        // pieces kept for later (the in-game menu and overlays use them too)
        public static GameObject InputPrototype, ButtonPrototype, TextPrototype;

        static bool _dumped;

        readonly Session _s;
        Component _menuSeen, _rightSide;
        bool _failed, _built;
        Transform _content;
        GameObject _tileTemplate, _rowTemplate, _scrollBar;
        Component _listHeader, _worldName, _hostPassword, _addAddress, _addName, _addPassword;
        string _status = "";
        GameObject _lobbyButtonTemplate;
        Transform _lobbyList;
        Component _lobbyName, _lobbyHeader;
        string _lobbyShown;
        float _lobbyRefreshAt;
        string _armedDelete;
        float _armedUntil;

        public MainMenuUi(Session s) { _s = s; }

        public bool Ready => _built && _rightSide != null;

        public void Update()
        {
            var menu = Game.MainMenu;
            if (menu == null) { _built = false; return; }
            if (menu != _menuSeen) { _menuSeen = menu; _failed = false; _built = false; }
            if (!_built && !_failed)
            {
                try { Build(); _built = true; Plugin.Log.LogInfo("Multiplayer menu built from the game's own menu"); }
                catch (Exception e)
                {
                    _failed = true;
                    Plugin.Log.LogWarning("Couldn't build the multiplayer menu from the game's menu, using the simple window: " + e);
                }
            }
            if (_built && _s.InMenuLobby && Time.unscaledTime >= _lobbyRefreshAt) RefreshLobby();
            if (_headerResetAt > 0 && Time.unscaledTime > _headerResetAt) { _headerResetAt = 0; UiKit.SetText(_listHeader, "Multiplayer"); }
            if (_armedDelete != null && Time.unscaledTime > _armedUntil) { _armedDelete = null; Refresh(); }
        }

        // Messages while you're in the menu (connecting, wrong password...).
        // Problems (can't connect, wrong password...) show for a few seconds in the panel's title.
        public void SetStatus(string text)
        {
            _status = text ?? "";
            bool problem = _status.IndexOf("Couldn't", StringComparison.OrdinalIgnoreCase) >= 0 || _status.StartsWith("Server said no") ||
                           _status.StartsWith("Disconnected") || _status.IndexOf("doesn't look", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           _status.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0 || _status.IndexOf("first", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!problem || _listHeader == null) return;
            UiKit.SetText(_listHeader, "Multiplayer  ·  " + _status);
            _headerResetAt = Time.unscaledTime + 8f;
        }

        float _headerResetAt;

        public bool Open(string group = ListGroup)
        {
            if (!Ready) return false;
            if (group == ListGroup) Refresh();
            Game.Call(Game.MainMenuRightSide, _rightSide, "OpenGroup", group);
            return true;
        }

        public bool OpenAdd(string address, string name)
        {
            if (!Ready) return false;
            UiKit.SetInput(_addAddress, address);
            UiKit.SetInput(_addName, name);
            UiKit.SetInput(_addPassword, "");
            Open(AddGroup);
            UiKit.Focus(_addPassword);
            return true;
        }

        // ---------- building ----------

        void Build()
        {
            _rightSide = Game.Get(Game.MainMenuRightSide, null, "main") as Component;
            if (_rightSide == null) throw new Exception("MainMenuRightSide.main missing");
            var rs = _rightSide.transform;
            if (!_dumped) { _dumped = true; UiKit.Dump(rs, "main menu right side", 5); }

            var saved = rs.Find("SavedGames") ?? throw new Exception("SavedGames panel missing");
            var newGame = rs.Find("NewGame") ?? throw new Exception("NewGame panel missing");
            var home = rs.Find("Home");

            // pieces for later
            var emailField = FindEmailField(home) ?? FindEmailField(rs);
            if (emailField != null && InputPrototype == null) InputPrototype = PrepareInputPrototype(emailField);
            var play = GameObject.Find("Menu canvas")?.transform.Find("Panel/MainMenu/PrimaryOptions/MenuButtons/ButtonPlay");
            if (play != null && ButtonPrototype == null)
            {
                ButtonPrototype = UiKit.Copy(play.gameObject, "SNMP_ButtonPrototype");
                UiKit.KeepOnlyUi(ButtonPrototype); // it lives on after the menu is gone
            }
            var header = Header(saved.gameObject, null);
            if (header != null && TextPrototype == null) TextPrototype = PrepareTextPrototype(header.gameObject);

            BuildList(rs, saved);
            BuildHost(rs, newGame);
            BuildAdd(rs, newGame);
            BuildLobby(rs, newGame);
        }

        static GameObject FindEmailField(Transform root)
        {
            if (root == null) return null;
            if (Game.MainMenuEmailHandler != null)
            {
                var handler = root.GetComponentInChildren(Game.MainMenuEmailHandler, true);
                if (handler != null && Game.Get(Game.MainMenuEmailHandler, handler, "inputfield") is GameObject go && go != null) return go;
            }
            var field = UiKit.FindInput(root.gameObject);
            return field != null ? field.gameObject : null;
        }

        static GameObject PrepareInputPrototype(GameObject field)
        {
            var copy = UiKit.Copy(field, "SNMP_InputPrototype");
            UiKit.StopTranslating(copy);
            UiKit.SetupInput(UiKit.FindInput(copy), "", false, 60);
            return copy;
        }

        static GameObject PrepareTextPrototype(GameObject text)
        {
            var copy = UiKit.Copy(text, "SNMP_TextPrototype");
            UiKit.KeepOnlyUi(copy);
            foreach (Transform child in copy.transform.Cast<Transform>().ToList()) UnityEngine.Object.DestroyImmediate(child.gameObject);
            return copy;
        }

        // The panel's title text: a child called Header, else the first text that isn't inside the list.
        static Component Header(GameObject group, Transform exclude)
        {
            var named = UiKit.FindDeep(group.transform, "Header");
            if (named != null && UiKit.FirstText(named.gameObject) is Component t) return t;
            foreach (var c in group.GetComponentsInChildren(UiKit.TmpText, true))
                if (exclude == null || !c.transform.IsChildOf(exclude)) return c;
            return null;
        }

        void Register(GameObject group)
        {
            var comp = Game.MainMenuGroup != null ? group.GetComponent(Game.MainMenuGroup) : null;
            if (comp != null && Game.Get(Game.MainMenuRightSide, _rightSide, "groups") is IList groups && !groups.Contains(comp)) groups.Add(comp);
        }

        void BuildList(Transform rs, Transform saved)
        {
            var old = rs.Find(ListGroup);
            if (old != null) UnityEngine.Object.Destroy(old.gameObject);

            var list = UiKit.Copy(saved.gameObject, ListGroup);
            var panel = Game.MainMenuLoadPanel != null ? list.GetComponentInChildren(Game.MainMenuLoadPanel, true) : null;
            if (panel == null) throw new Exception("MainMenuLoadPanel missing in the copy");
            var area = Game.Get(Game.MainMenuLoadPanel, panel, "savedGameArea") as GameObject;
            _rowTemplate = Game.Get(Game.MainMenuLoadPanel, panel, "saveInstance") as GameObject;
            _scrollBar = Game.Get(Game.MainMenuLoadPanel, panel, "scrollBar") as GameObject;
            UnityEngine.Object.DestroyImmediate(panel); // it would fill the list with single-player saves
            if (area == null) throw new Exception("save list area missing");
            _content = area.transform;

            var tile = _content.Find("NewGame");
            if (tile == null) throw new Exception("'New game' tile missing");
            _tileTemplate = UiKit.Copy(tile.gameObject, "SNMP_Tile");
            UiKit.StopTranslating(_tileTemplate);
            foreach (Transform child in _content.Cast<Transform>().ToList()) UnityEngine.Object.DestroyImmediate(child.gameObject);

            _listHeader = Header(list, _content);
            UiKit.SetText(_listHeader, "Multiplayer");
            UiKit.Place(list, rs, false);
            Register(list);
        }

        void BuildHost(Transform rs, Transform newGame)
        {
            var old = rs.Find(HostGroup);
            if (old != null) UnityEngine.Object.Destroy(old.gameObject);
            var host = UiKit.Copy(newGame.gameObject, HostGroup);
            UiKit.SetText(Header(host, null), "Host a world");

            GameObject first = null;
            foreach (var b in host.GetComponentsInChildren(UiKit.Button, true))
            {
                var target = UiKit.ClickTarget(b);
                string mode = target.EndsWith("Survival") ? GameModes.Survival
                    : target.EndsWith("Freedom") ? GameModes.Freedom
                    : target.EndsWith("Hardcore") ? GameModes.Hardcore
                    : target.EndsWith("Creative") ? GameModes.Creative : null;
                if (mode != null)
                {
                    if (first == null) first = b.gameObject;
                    UiKit.OnClick(b, () => HostWith(mode));
                }
                else if (target.Contains("Back") || target.Contains("Home")) UiKit.OnClick(b, () => Open(ListGroup));
            }
            if (first == null) throw new Exception("no game mode buttons in the New game panel");

            _worldName = AddInput(first, "World name", false, 40);
            UiKit.SetInput(_worldName, "My World");
            _hostPassword = AddInput(first, "Password (optional, friends need it to join)", true, 40);
            UiKit.SetInput(_hostPassword, Plugin.HostPassword.Value ?? "");
            UiKit.Place(host, rs, false);
            Register(host);
        }

        void BuildAdd(Transform rs, Transform newGame)
        {
            var old = rs.Find(AddGroup);
            if (old != null) UnityEngine.Object.Destroy(old.gameObject);
            var add = UiKit.Copy(newGame.gameObject, AddGroup);
            UiKit.SetText(Header(add, null), "Add a server");

            var modeButtons = new List<Component>();
            foreach (var b in add.GetComponentsInChildren(UiKit.Button, true))
            {
                var target = UiKit.ClickTarget(b);
                if (target.StartsWith("OnButton") && (target.EndsWith("Survival") || target.EndsWith("Freedom") || target.EndsWith("Hardcore") || target.EndsWith("Creative")))
                    modeButtons.Add(b);
                else if (target.Contains("Back") || target.Contains("Home")) UiKit.OnClick(b, () => Open(ListGroup));
            }
            if (modeButtons.Count < 2) throw new Exception("not enough buttons in the New game panel");

            var join = modeButtons[0].gameObject;
            var save = modeButtons[1].gameObject;
            for (int i = 2; i < modeButtons.Count; i++) UnityEngine.Object.DestroyImmediate(modeButtons[i].gameObject);
            SetButtonLabel(join, "Save & join");
            SetButtonLabel(save, "Save");
            UiKit.OnClick(join, () => SaveServer(true));
            UiKit.OnClick(save, () => SaveServer(false));

            _addAddress = AddInput(join, "Join code or IP (like KQ7MX-3HD2P)", false, 60);
            _addName = AddInput(join, "Name (optional)", false, 40);
            _addPassword = AddInput(join, "Password (if the server has one)", true, 40);
            UiKit.Place(add, rs, false);
            Register(add);
        }

        // The party lobby: your name, suit color, who's here, and the host's Start button.
        void BuildLobby(Transform rs, Transform newGame)
        {
            var old = rs.Find(LobbyGroup);
            if (old != null) UnityEngine.Object.Destroy(old.gameObject);
            var lobby = UiKit.Copy(newGame.gameObject, LobbyGroup);
            _lobbyHeader = Header(lobby, null);
            UiKit.SetText(_lobbyHeader, "Party lobby");

            GameObject first = null;
            foreach (var b in lobby.GetComponentsInChildren(UiKit.Button, true).ToList())
            {
                var target = UiKit.ClickTarget(b);
                if (target.StartsWith("OnButton")) { if (first == null) first = b.gameObject; else UnityEngine.Object.DestroyImmediate(b.gameObject); }
                else if (target.Contains("Back") || target.Contains("Home")) UiKit.OnClick(b, () => _s.LeaveServer());
            }
            if (first == null) throw new Exception("no buttons in the New game panel");
            _lobbyList = first.transform.parent;
            _lobbyButtonTemplate = UiKit.Copy(first, "SNMP_LobbyButton");
            SetButtonLabel(_lobbyButtonTemplate, "");
            UnityEngine.Object.DestroyImmediate(first);

            // name box stays; the rows under it are rebuilt when something changes
            var nameGo = UiKit.Place(UnityEngine.Object.Instantiate(InputPrototype, UiKit.Holder, false), _lobbyList);
            nameGo.name = "SNMP_LobbyName";
            UiKit.GiveHeight(nameGo, 60f);
            _lobbyName = UiKit.FindInput(nameGo);
            UiKit.SetupInput(_lobbyName, "Your diver's name", false, Protocol.MaxNameLength);
            if (Game.Get(UiKit.TmpInput, _lobbyName, "onEndEdit") is UnityEngine.Events.UnityEvent<string> ended)
                ended.AddListener(name => { if (name.Trim().Length > 0 && name.Trim() != Plugin.PlayerName.Value) _s.SendProfile(name.Trim(), Plugin.DiverColor.Value); });

            UiKit.Place(lobby, rs, false);
            Register(lobby);
        }

        public bool OpenLobby()
        {
            if (!Ready || _lobbyList == null) return false;
            UiKit.SetInput(_lobbyName, Plugin.PlayerName.Value);
            _lobbyShown = null;
            RefreshLobby();
            Game.Call(Game.MainMenuRightSide, _rightSide, "OpenGroup", LobbyGroup);
            return true;
        }

        void RefreshLobby()
        {
            _lobbyRefreshAt = Time.unscaledTime + 0.5f;
            if (_lobbyList == null) return;
            var rows = new List<(string text, Action click)>();
            int myColor = Plugin.DiverColor.Value;
            rows.Add(($"Suit color:  <color=#{DiverColors.Hex(myColor)}>■■■</color>  {DiverColors.NameOf(myColor)}   (click to change)",
                () => _s.SendProfile(Plugin.PlayerName.Value, DiverColors.Next(Plugin.DiverColor.Value))));

            int hostId = _s.HostPlayerId;
            rows.Add(($"<color=#{DiverColors.Hex(myColor)}>■</color>  {Plugin.PlayerName.Value}  (you)" + (_s.IsHost ? "  ·  host" : ""), null));
            foreach (var r in _s.RemotePlayers.Where(r => r != null).OrderBy(r => r.Id))
                rows.Add(($"<color=#{DiverColors.Hex(_s.ColorOf(r.Id))}>■</color>  {r.PlayerName}" + (r.Id == hostId ? "  ·  host" : ""), null));

            if (_s.IsHost && _s.JoinCodeText != null)
                rows.Add(($"Join code: {_s.JoinCodeText}   (click to copy)", () => GUIUtility.systemCopyBuffer = _s.JoinCodeText));
            if (_s.IsHost) rows.Add(("Start the game", _s.StartFromLobby));
            else rows.Add(("Waiting for " + _s.HostName + " to start...", null));
            rows.Add(("Leave", () => { _s.LeaveServer(); Open(ListGroup); }));

            var key = string.Join("\n", rows.Select(r => r.text + (r.click == null ? "" : "*")).ToArray()) + _status;
            if (key == _lobbyShown) return;
            _lobbyShown = key;
            UiKit.SetText(_lobbyHeader, $"Party lobby  ·  {_s.GameModeName}  ·  {_s.RemotePlayers.Count(r => r != null) + 1} players");

            foreach (Transform child in _lobbyList.Cast<Transform>().ToList())
                if (child.name != "SNMP_LobbyName") UnityEngine.Object.Destroy(child.gameObject);
            foreach (var (text, click) in rows)
            {
                var b = UiKit.Place(UnityEngine.Object.Instantiate(_lobbyButtonTemplate, UiKit.Holder, false), _lobbyList);
                b.name = "SNMP_LobbyRow";
                UiKit.SetText(b, text);
                UiKit.OnClick(b, click ?? (() => { }));
                if (click == null) UiKit.SetInteractable(b, false);
            }
        }

        // A mode button may have a title and a description: use the first text for the title, hide the rest.
        static void SetButtonLabel(GameObject button, string text)
        {
            var texts = button.GetComponentsInChildren(UiKit.TmpText, true);
            for (int i = 0; i < texts.Length; i++)
            {
                if (i == 0) UiKit.SetText(texts[i], text);
                else texts[i].gameObject.SetActive(false);
            }
        }

        // A copy of the game's text box, put just above `before`.
        Component AddInput(GameObject before, string placeholder, bool password, int max)
        {
            if (InputPrototype == null) throw new Exception("no text box to copy");
            var go = UiKit.Place(UnityEngine.Object.Instantiate(InputPrototype, UiKit.Holder, false), before.transform.parent);
            go.name = "SNMP_Input";
            go.transform.SetSiblingIndex(before.transform.GetSiblingIndex());
            UiKit.GiveHeight(go, 60f);
            var field = UiKit.FindInput(go);
            if (field == null) Plugin.Log.LogWarning("Text box copy has no input: " + UiKit.Describe(go));
            UiKit.SetupInput(field, placeholder, password, max);
            UiKit.SetInput(field, "");
            return field;
        }

        // ---------- the list ----------

        public void Refresh()
        {
            if (_content == null) return;
            foreach (Transform child in _content.Cast<Transform>().ToList()) UnityEngine.Object.Destroy(child.gameObject);

            AddTile("Host a world", () => Open(HostGroup));
            AddTile("Add a server", () => { UiKit.SetInput(_addAddress, ""); UiKit.SetInput(_addName, ""); UiKit.SetInput(_addPassword, ""); Open(AddGroup); });

            int rows = 0;
            var local = LocalServer();
            if (local != null)
            {
                AddRow("Launcher server: " + local.Value.world, "Running on this PC · click to join", "Server",
                    () => _s.JoinFromMenu("127.0.0.1:" + local.Value.port), () => { });
                rows++;
            }
            foreach (var (name, world) in MyWorlds())
            {
                var key = "world:" + name;
                AddRow(name, _armedDelete == key ? "Click the bin again to delete this world" : "Your world" + (world.Started ? "" : " · not started yet") + " · click to host",
                    world.GameMode, () => _s.HostFromMenu(name, world.GameMode),
                    () => Delete(key, () => { try { File.Delete(Path.Combine(Session.WorldsFolder, name + ".dat")); } catch { } }));
                rows++;
            }
            foreach (var server in ServerList.Load())
            {
                var key = "server:" + server.Address;
                var title = server.Name == server.Address ? server.Address : server.Name;
                var sub = _armedDelete == key ? "Click the bin again to remove this server"
                    : (server.Name == server.Address ? "" : server.Address + " · ") + (string.IsNullOrEmpty(server.Password) ? "click to join" : "password saved · click to join");
                AddRow(title, sub, "Server", () => _s.JoinFromMenu(server.Address), () => Delete(key, () => ServerList.Remove(server.Address)));
                rows++;
            }
            if (_scrollBar != null) _scrollBar.SetActive(rows >= 4);
        }

        void Delete(string key, Action remove)
        {
            if (_armedDelete != key)
            {
                _armedDelete = key;
                _armedUntil = Time.unscaledTime + 4f;
                Refresh();
                return;
            }
            _armedDelete = null;
            remove();
            Refresh();
        }

        // The launcher leaves this file while its server runs (deleted when it stops).
        static (int port, string world)? LocalServer()
        {
            try
            {
                var path = Path.Combine(Plugin.Folder, "local_server.txt");
                if (!File.Exists(path) || (DateTime.UtcNow - File.GetLastWriteTimeUtc(path)).TotalHours > 24) return null;
                int port = 0;
                string world = "World";
                foreach (var line in File.ReadAllLines(path))
                {
                    if (line.StartsWith("port=")) int.TryParse(line.Substring(5), out port);
                    else if (line.StartsWith("world=")) world = line.Substring(6);
                }
                return port > 0 ? (port, world) : ((int, string)?)null;
            }
            catch { return null; }
        }

        static List<(string name, WorldState world)> MyWorlds()
        {
            var list = new List<(string, WorldState)>();
            try
            {
                if (!Directory.Exists(Session.WorldsFolder)) return list;
                foreach (var file in Directory.GetFiles(Session.WorldsFolder, "*.dat").OrderByDescending(File.GetLastWriteTimeUtc))
                {
                    try { list.Add((Path.GetFileNameWithoutExtension(file), WorldState.LoadFromFile(file))); } catch { }
                }
            }
            catch { }
            return list;
        }

        GameObject AddTile(string text, Action click)
        {
            var tile = UiKit.Place(UnityEngine.Object.Instantiate(_tileTemplate, UiKit.Holder, false), _content);
            tile.name = "SNMP_Tile";
            UiKit.SetText(tile, text);
            if (click != null) UiKit.OnClick(tile, click);
            else
            {
                UiKit.OnClick(tile, () => { });
                UiKit.SetInteractable(tile, false);
            }
            return tile;
        }

        // A real save-slot row: big title, a line under it, the mode, a load button and a bin.
        void AddRow(string title, string sub, string mode, Action open, Action delete)
        {
            if (_rowTemplate == null) { AddTile(title + "\n" + sub, open); return; }
            var row = UnityEngine.Object.Instantiate(_rowTemplate, UiKit.Holder, false);
            row.name = "SNMP_Row";
            var lb = Game.MainMenuLoadButton != null ? row.GetComponent(Game.MainMenuLoadButton) : null;
            if (lb == null) { UnityEngine.Object.Destroy(row); AddTile(title + "\n" + sub, open); return; }

            var modeText = Game.Get(Game.MainMenuLoadButton, lb, "saveGameModeText") as Component;
            var timeText = Game.Get(Game.MainMenuLoadButton, lb, "saveGameTimeText") as Component;
            var lengthText = Game.Get(Game.MainMenuLoadButton, lb, "saveGameLengthText") as Component;
            var loadButton = Game.Get(Game.MainMenuLoadButton, lb, "loadButton") as GameObject;
            var deleteButton = Game.Get(Game.MainMenuLoadButton, lb, "deleteButton") as GameObject;
            var icons = Game.Get(Game.MainMenuLoadButton, lb, "saveIcons") as GameObject;
            var confirm = Game.Get(Game.MainMenuLoadButton, lb, "delete") as GameObject;
            UnityEngine.Object.DestroyImmediate(lb); // it expects a real save behind it
            UiKit.StopTranslating(row);

            UiKit.SetText(timeText, title);
            UiKit.SetText(lengthText, sub);
            UiKit.SetText(modeText, mode);
            if (icons != null) icons.SetActive(false);
            if (confirm != null) confirm.SetActive(false);

            foreach (var b in row.GetComponentsInChildren(UiKit.Button, true))
            {
                bool isDelete = deleteButton != null && b.transform.IsChildOf(deleteButton.transform);
                UiKit.OnClick(b, isDelete ? delete : open);
            }
            if (loadButton != null) UiKit.OnClick(loadButton, open);
            if (deleteButton != null) { deleteButton.SetActive(true); UiKit.OnClick(deleteButton, delete); }
            UiKit.Place(row, _content);
        }

        // ---------- actions ----------

        void HostWith(string mode)
        {
            var name = UiKit.GetInput(_worldName).Trim();
            if (name.Length == 0) { SetStatus("Give your world a name first."); Open(ListGroup); return; }
            Plugin.HostPassword.Value = UiKit.GetInput(_hostPassword);
            // an existing world keeps the mode it was made with
            var existing = MyWorlds().FirstOrDefault(w => string.Equals(w.name, name, StringComparison.OrdinalIgnoreCase));
            _s.HostFromMenu(existing.world != null ? existing.name : name, existing.world != null ? existing.world.GameMode : mode);
            Open(ListGroup);
        }

        void SaveServer(bool join)
        {
            var address = UiKit.GetInput(_addAddress).Trim();
            if (!JoinCode.TryParseAddress(address, Protocol.DefaultPort, out _, out _))
            {
                SetStatus("That doesn't look like a join code (like KQ7MX-3HD2P) or an IP.");
                Open(ListGroup);
                return;
            }
            ServerList.Remember(address, UiKit.GetInput(_addName), UiKit.GetInput(_addPassword));
            if (join) _s.JoinFromMenu(address);
            Open(ListGroup);
        }
    }
}
