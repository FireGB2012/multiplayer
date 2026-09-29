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
        string _addName = "", _addAddress = "";
        List<ServerList.Entry> _servers = new List<ServerList.Entry>();
        List<(string name, WorldState world)> _myWorlds = new List<(string, WorldState)>();
        GUIStyle _mpTitle, _mpBig;

        void OpenMultiplayerMenu()
        {
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

        void DrawMainMenuUi()
        {
            if (Game.MainMenu == null || Game.InWorld) return;

            // backup button if we couldn't copy the game's own one
            if (!MainMenuButton.Injected && !_mpMenuOpen &&
                GUI.Button(new Rect(30, Screen.height - 90, 220, 50), "MULTIPLAYER"))
                OpenMultiplayerMenu();

            if (!_mpMenuOpen) return;
            float w = Mathf.Min(620f, Screen.width - 40f), h = Mathf.Min(560f, Screen.height - 40f);
            _mpWindow = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            var old = GUI.color;
            GUI.color = new Color(0, 0, 0, 0.6f); // dim the menu behind us
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = old;
            GUILayout.Window(0x5B4D51, _mpWindow, DrawMpWindow, "");
        }

        void DrawMpWindow(int id)
        {
            if (_mpTitle == null)
            {
                _mpTitle = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                _mpTitle.normal.textColor = new Color(0.37f, 0.83f, 0.88f);
                _mpBig = new GUIStyle(GUI.skin.button) { fontSize = 16, fontStyle = FontStyle.Bold, fixedHeight = 40 };
            }

            GUILayout.Label("MULTIPLAYER", _mpTitle, GUILayout.Height(40));

            GUILayout.BeginHorizontal();
            GUILayout.Label("Your name", GUILayout.Width(80));
            Plugin.PlayerName.Value = GUILayout.TextField(Plugin.PlayerName.Value, Protocol.MaxNameLength);
            GUILayout.EndHorizontal();
            GUILayout.Space(6);

            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(_mpTab == MpTab.Servers, "Servers", GUI.skin.button)) _mpTab = MpTab.Servers;
            if (GUILayout.Toggle(_mpTab == MpTab.Host, "Host a world", GUI.skin.button)) _mpTab = MpTab.Host;
            if (GUILayout.Toggle(_mpTab == MpTab.Add, "Add server", GUI.skin.button)) _mpTab = MpTab.Add;
            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            bool busy = _client.State != ClientState.Disconnected;
            if (busy) GUILayout.Label(_client.State == ClientState.Connecting ? "Connecting..." : "Connected! Loading the world...");
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
            foreach (var line in _chat.Skip(Mathf.Max(0, _chat.Count - 2))) GUILayout.Label(line.text);
            if (GUILayout.Button("Back", GUILayout.Height(30))) _mpMenuOpen = false;
        }

        void DrawServersTab()
        {
            _mpScroll = GUILayout.BeginScrollView(_mpScroll);

            GUILayout.Label("YOUR WORLDS");
            if (_myWorlds.Count == 0) GUILayout.Label("   None yet. Make one in 'Host a world'.");
            foreach (var (name, world) in _myWorlds.ToList())
            {
                GUILayout.BeginHorizontal(GUI.skin.box);
                GUILayout.Label($"{name}   ·   {world.GameMode}" + (world.Started ? "" : "   ·   not started"));
                if (GUILayout.Button("Host", GUILayout.Width(80))) HostFromMenu(name, world.GameMode);
                if (GUILayout.Button("Delete", GUILayout.Width(70)))
                {
                    try { File.Delete(Path.Combine(WorldsFolder, name + ".dat")); } catch { }
                    RefreshMenuLists();
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(12);
            GUILayout.Label("SERVERS YOU'VE PLAYED ON");
            if (_servers.Count == 0) GUILayout.Label("   None yet. Got a join code from a friend? Use 'Add server'.");
            foreach (var s in _servers.ToList())
            {
                GUILayout.BeginHorizontal(GUI.skin.box);
                GUILayout.Label(s.Name == s.Address ? s.Address : $"{s.Name}   ({s.Address})");
                if (GUILayout.Button("Join", GUILayout.Width(80))) JoinFromMenu(s.Address);
                if (GUILayout.Button("Delete", GUILayout.Width(70)))
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
            GUILayout.Label("World name");
            _newWorldName = GUILayout.TextField(_newWorldName, 40);
            GUILayout.Space(6);
            GUILayout.Label("Game mode");
            _newWorldMode = GUILayout.SelectionGrid(_newWorldMode, GameModes.All, GameModes.All.Length);
            GUILayout.Label(GameModes.Describe(GameModes.All[_newWorldMode]));
            GUILayout.Space(10);

            bool exists = _myWorlds.Any(w => string.Equals(w.name, _newWorldName.Trim(), System.StringComparison.OrdinalIgnoreCase));
            if (exists) GUILayout.Label("You already have a world with that name. Hosting it continues that world.");
            GUILayout.Label("Friends join with the code that shows up once you're in. Everyone waits on a black screen until you press ENTER.");
            GUILayout.Space(6);
            if (GUILayout.Button(exists ? "CONTINUE WORLD" : "CREATE & HOST", _mpBig) && _newWorldName.Trim().Length > 0)
                HostFromMenu(_newWorldName.Trim(), GameModes.All[_newWorldMode]);
        }

        void DrawAddTab()
        {
            GUILayout.Label("Join code or IP (from your friend)");
            _addAddress = GUILayout.TextField(_addAddress, 60);
            GUILayout.Label("Name (optional)");
            _addName = GUILayout.TextField(_addName, 40);
            GUILayout.Space(10);

            GUILayout.BeginHorizontal();
            bool valid = JoinCode.TryParseAddress(_addAddress, Protocol.DefaultPort, out _, out _);
            if (GUILayout.Button("SAVE", _mpBig) && valid)
            {
                ServerList.Remember(_addAddress.Trim(), _addName);
                _addAddress = _addName = "";
                RefreshMenuLists();
                _mpTab = MpTab.Servers;
            }
            if (GUILayout.Button("SAVE & JOIN", _mpBig) && valid)
            {
                ServerList.Remember(_addAddress.Trim(), _addName);
                JoinFromMenu(_addAddress.Trim());
                _addAddress = _addName = "";
                RefreshMenuLists();
            }
            GUILayout.EndHorizontal();
            if (_addAddress.Trim().Length > 0 && !valid) GUILayout.Label("That doesn't look like a join code (like KQ7MX-3HD2P) or an IP.");
        }
    }
}
