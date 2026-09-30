using System.Collections.Generic;
using System.IO;
using System.Linq;
using SubnauticaMP.Shared;
using UnityEngine;

namespace SubnauticaMP
{
    // The Multiplayer screen opened from the main menu: your worlds, servers you've been on, host, add.
    public sealed partial class Session
    {
        enum MpTab { Servers, Host, Add }

        bool _mpMenuOpen;
        MpTab _mpTab;
        Rect _mpWindow;
        Vector2 _mpScroll;
        string _newWorldName = "My World";
        int _newWorldMode;
        string _addName = "", _addAddress = "", _addPassword = "";
        List<ServerList.Entry> _servers = new List<ServerList.Entry>();
        List<(string name, WorldState world)> _myWorlds = new List<(string, WorldState)>();

        // A server said it needs a password: open "Add server" with its address so they can type it.
        void AskForPassword(string address, string reason)
        {
            if (string.IsNullOrEmpty(address) || Game.InWorld) return;
            var known = ServerList.Find(address);
            _menuUi.SetStatus(reason + " Type the password and click Save & join.");
            if (_menuUi.OpenAdd(address, known != null && known.Name != known.Address ? known.Name : "")) return;
            OpenMultiplayerMenu();
            _mpTab = MpTab.Add;
            _addAddress = address;
            _addPassword = "";
            _addName = known != null && known.Name != known.Address ? known.Name : "";
            AddChat(reason + " Type it in and press SAVE & JOIN.");
        }

        void OpenMultiplayerMenu()
        {
            if (_menuUi.Open()) return; // the one made from the game's own menu
            _mpMenuOpen = true;
            _mpTab = MpTab.Servers;
            RefreshMenuLists();
        }

        void RefreshMenuLists()
        {
            _servers = ServerList.Load();
            _myWorlds.Clear();
            try
            {
                if (Directory.Exists(WorldsFolder))
                    foreach (var file in Directory.GetFiles(WorldsFolder, "*.dat").OrderByDescending(File.GetLastWriteTimeUtc))
                    {
                        WorldState w = null;
                        try { w = WorldState.LoadFromFile(file); } catch { }
                        if (w != null) _myWorlds.Add((Path.GetFileNameWithoutExtension(file), w));
                    }
            }
            catch { }
        }

        // F8: the game-style page when we have it, the simple window otherwise.
        void ToggleMultiplayerWindow()
        {
            if (_menuOpen) { _menuOpen = false; return; }
            if (Game.InWorld && ShowPausePage(false)) return;
            if (!Game.InWorld && Game.MainMenu != null && _menuUi.Open()) return;
            _menuOpen = true;
        }

        void DrawMainMenuUi()
        {
            if (Game.MainMenu == null || Game.InWorld) return;

            // backup button if we couldn't copy the game's own one
            if (!MainMenuButton.Injected && !_mpMenuOpen &&
                GUI.Button(new Rect(30, Screen.height - 90, 240, 50), "MULTIPLAYER", SnSkin.BigButton))
                OpenMultiplayerMenu();

            if (!_mpMenuOpen) return;
            float w = Mathf.Min(660f, Screen.width - 40f), h = Mathf.Min(620f, Screen.height - 40f);
            _mpWindow = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            var old = GUI.color;
            GUI.color = new Color(0, 0, 0, 0.6f); // dim the menu behind us
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = old;
            GUILayout.Window(0x5B4D51, _mpWindow, DrawMpWindow, "");
        }

        void DrawMpWindow(int id)
        {
            GUILayout.Label("MULTIPLAYER", SnSkin.Title, GUILayout.Height(44));

            GUILayout.BeginHorizontal();
            GUILayout.Label("Your name", GUILayout.Width(100));
            Plugin.PlayerName.Value = GUILayout.TextField(Plugin.PlayerName.Value, Protocol.MaxNameLength);
            GUILayout.EndHorizontal();
            GUILayout.Space(6);

            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(_mpTab == MpTab.Servers, "SERVERS", SnSkin.Tab)) _mpTab = MpTab.Servers;
            if (GUILayout.Toggle(_mpTab == MpTab.Host, "HOST A WORLD", SnSkin.Tab)) _mpTab = MpTab.Host;
            if (GUILayout.Toggle(_mpTab == MpTab.Add, "ADD SERVER", SnSkin.Tab)) _mpTab = MpTab.Add;
            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            bool busy = _client.State != ClientState.Disconnected;
            if (busy) GUILayout.Label(_client.State == ClientState.Connecting ? "Connecting..." : "Connected! Loading the world...", SnSkin.Header);
            GUI.enabled = !busy;

            switch (_mpTab)
            {
                case MpTab.Servers: DrawServersTab(); break;
                case MpTab.Host: DrawHostTab(); break;
                case MpTab.Add: DrawAddTab(); break;
            }
            GUI.enabled = true;

            GUILayout.FlexibleSpace();
            // last few messages (connection errors etc.)
            foreach (var line in _chat.Skip(Mathf.Max(0, _chat.Count - 2))) GUILayout.Label(line.text, SnSkin.Small);
            if (GUILayout.Button("BACK", GUILayout.Height(36))) _mpMenuOpen = false;
        }

        string _confirmDelete;

        void DrawServersTab()
        {
            _mpScroll = GUILayout.BeginScrollView(_mpScroll);

            GUILayout.Label("YOUR WORLDS", SnSkin.Header);
            if (_myWorlds.Count == 0) GUILayout.Label("None yet. Make one in 'Host a world'.", SnSkin.Small);
            foreach (var (name, world) in _myWorlds.ToList())
            {
                GUILayout.BeginHorizontal(GUI.skin.box);
                GUILayout.Label($"{name}   ·   {world.GameMode}" + (world.Started ? "" : "   ·   not started"));
                if (GUILayout.Button("HOST", SnSkin.SmallButton, GUILayout.Width(80))) HostFromMenu(name, world.GameMode);
                if (GUILayout.Button(_confirmDelete == name ? "SURE?" : "DELETE", SnSkin.DangerButton, GUILayout.Width(76)))
                {
                    if (_confirmDelete != name) _confirmDelete = name;
                    else
                    {
                        try { File.Delete(Path.Combine(WorldsFolder, name + ".dat")); } catch { }
                        _confirmDelete = null;
                        RefreshMenuLists();
                    }
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(8);
            GUILayout.Label("SERVERS YOU'VE PLAYED ON", SnSkin.Header);
            if (_servers.Count == 0) GUILayout.Label("None yet. Got a join code from a friend? Use 'Add server'.", SnSkin.Small);
            foreach (var s in _servers.ToList())
            {
                GUILayout.BeginHorizontal(GUI.skin.box);
                GUILayout.Label((s.Name == s.Address ? s.Address : $"{s.Name}   ({s.Address})") + (string.IsNullOrEmpty(s.Password) ? "" : "   ·   password saved"));
                if (GUILayout.Button("JOIN", SnSkin.SmallButton, GUILayout.Width(80))) JoinFromMenu(s.Address);
                if (GUILayout.Button("REMOVE", SnSkin.DangerButton, GUILayout.Width(80)))
                {
                    ServerList.Remove(s.Address);
                    RefreshMenuLists();
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }

        void DrawHostTab()
        {
            GUILayout.Label("WORLD NAME", SnSkin.Header);
            _newWorldName = GUILayout.TextField(_newWorldName, 40);
            GUILayout.Label("GAME MODE", SnSkin.Header);
            _newWorldMode = GUILayout.SelectionGrid(_newWorldMode, GameModes.All, GameModes.All.Length, SnSkin.Tab);
            GUILayout.Label(GameModes.Describe(GameModes.All[_newWorldMode]), SnSkin.Small);
            GUILayout.Label("PASSWORD (OPTIONAL)", SnSkin.Header);
            Plugin.HostPassword.Value = GUILayout.PasswordField(Plugin.HostPassword.Value ?? "", '*', 40);
            GUILayout.Label(string.IsNullOrEmpty(Plugin.HostPassword.Value) ? "Anyone with the join code can come in." : "Friends need the join code AND this password.", SnSkin.Small);
            GUILayout.Space(8);

            bool exists = _myWorlds.Any(w => string.Equals(w.name, _newWorldName.Trim(), System.StringComparison.OrdinalIgnoreCase));
            if (exists) GUILayout.Label("You already have a world with that name. Hosting it continues that world.", SnSkin.Small);
            GUILayout.Label("Friends join with the code that shows up once you're in. Everyone waits on a black screen until you press ENTER.", SnSkin.Small);
            GUILayout.Space(6);
            if (GUILayout.Button(exists ? "CONTINUE WORLD" : "CREATE & HOST", SnSkin.BigButton) && _newWorldName.Trim().Length > 0)
                HostFromMenu(_newWorldName.Trim(), GameModes.All[_newWorldMode]);
        }

        void DrawAddTab()
        {
            GUILayout.Label("JOIN CODE OR IP (FROM YOUR FRIEND)", SnSkin.Header);
            _addAddress = GUILayout.TextField(_addAddress, 60);
            GUILayout.Label("NAME (OPTIONAL)", SnSkin.Header);
            _addName = GUILayout.TextField(_addName, 40);
            GUILayout.Label("PASSWORD (IF THE SERVER HAS ONE)", SnSkin.Header);
            _addPassword = GUILayout.PasswordField(_addPassword, '*', 40);
            GUILayout.Space(10);

            GUILayout.BeginHorizontal();
            bool valid = JoinCode.TryParseAddress(_addAddress, Protocol.DefaultPort, out _, out _);
            if (GUILayout.Button("SAVE", SnSkin.BigButton) && valid)
            {
                ServerList.Remember(_addAddress.Trim(), _addName, _addPassword);
                _addAddress = _addName = _addPassword = "";
                RefreshMenuLists();
                _mpTab = MpTab.Servers;
            }
            if (GUILayout.Button("SAVE & JOIN", SnSkin.BigButton) && valid)
            {
                ServerList.Remember(_addAddress.Trim(), _addName, _addPassword);
                JoinFromMenu(_addAddress.Trim());
                _addAddress = _addName = _addPassword = "";
                RefreshMenuLists();
            }
            GUILayout.EndHorizontal();
            if (_addAddress.Trim().Length > 0 && !valid) GUILayout.Label("That doesn't look like a join code (like KQ7MX-3HD2P) or an IP.", SnSkin.Small);
        }
    }
}
