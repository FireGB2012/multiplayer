using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using SubnauticaMP.Shared;
using UnityEngine;

namespace SubnauticaMP
{
    // F8 window, chat, name tags and the lobby screen, drawn in Subnautica's own style (see SnSkin).
    public sealed partial class Session
    {
        const float ChatShowSeconds = 10f;
        const int MaxChatLines = 8;

        readonly List<(string text, float time)> _chat = new List<(string, float)>();
        readonly ConcurrentQueue<string> _pendingChat = new ConcurrentQueue<string>(); // from background threads

        bool _menuOpen;
        Rect _window = new Rect(40, 40, 440, 560);
        string _chatInput = "";
        Vector2 _chatScroll, _playersScroll;
        int _confirmBan;
        GUIStyle _tagStyle, _tagSmall, _chatStyle;

        bool _gameMessagesWork;

        public void AddChat(string line)
        {
            Plugin.Log.LogInfo(line);
            if (Game.InWorld && Game.ErrorMessage != null)
            {
                try { Game.Call(Game.ErrorMessage, null, "AddMessage", line); _gameMessagesWork = true; }
                catch { _gameMessagesWork = false; }
            }
            else if (!Game.InWorld) _menuUi?.SetStatus(line);
            _chat.Add((line, Time.unscaledTime));
            if (_chat.Count > 50) _chat.RemoveAt(0);
            _chatScroll.y = float.MaxValue;
        }

        void DrainPendingChat()
        {
            while (_pendingChat.TryDequeue(out var line)) AddChat(line);
        }

        readonly System.Diagnostics.Stopwatch _guiTimer = new System.Diagnostics.Stopwatch();

        // Something on screen that takes clicks / typing (needs Unity's layout pass and input events).
        bool GuiInteractive => _menuOpen || _mpMenuOpen || (Wheel != null && Wheel.IsOpen) || Lobby.Holding ||
                               (!Game.InWorld && Game.MainMenu != null);

        // Unity calls OnGUI several times a frame (a layout pass + one per mouse/key event). In normal play only the
        // chat lines / stats are drawn, which only need the paint pass: everything else is skipped (this was most of
        // the mod's frame time).
        void OnGUI()
        {
            bool interactive = GuiInteractive;
            useGUILayout = interactive;
            if (!interactive && Event.current.type != EventType.Repaint) return;
            // nothing on screen at all (the usual case while playing): don't even start
            if (!interactive && !Loading && !PerfMonitor.Overlay && (_gameMessagesWork || !HasRecentChat)) return;
            _guiTimer.Restart();
            try { DrawGui(); }
            finally { PerfMonitor.Record("ui (all)", _guiTimer.Elapsed.TotalMilliseconds); }
        }

        readonly System.Diagnostics.Stopwatch _partTimer = new System.Diagnostics.Stopwatch();
        void TimeUi(string part, System.Action draw)
        {
            _partTimer.Restart();
            try { draw(); }
            finally { PerfMonitor.RecordDetail("ui: " + part, _partTimer.Elapsed.TotalMilliseconds); }
        }

        void DrawGui()
        {
            GUI.depth = -1000; // above the game's own UI
            DrainPendingChat();
            // switching GUI.skin makes Unity rebuild its style tables (twice per call): only for the real menus/windows
            bool skinned = GuiInteractive || Loading;
            var old = GUI.skin;
            if (skinned) GUI.skin = SnSkin.Skin;
            try
            {
                if (Game.MainMenu != null && !Game.InWorld) TimeUi("main menu", DrawMainMenuUi);
                // the lobby/loading screens and chat use the game's own UI when it could be built;
                // these simple versions are only the backup. Teammates show as HUD markers (game pings).
                if (Lobby.Holding) { if (!OverlayReady) TimeUi("lobby", DrawLobby); }
                else if (Loading) { if (!OverlayReady) TimeUi("loading", DrawLoading); }
                if (!_gameMessagesWork && !_menuOpen && HasRecentChat) TimeUi("chat lines", DrawChatOverlay);
                if (_menuOpen) TimeUi("F8 window", () => _window = GUILayout.Window(0x5B4D50, _window, DrawWindow, ""));
                if (Wheel != null && Wheel.IsOpen) TimeUi("emote wheel", Wheel.OnGUI);
                if (PerfMonitor.Overlay) TimeUi("F9 stats", () => PerfMonitor.OnGUI(this));
            }
            finally { if (skinned) GUI.skin = old; }
        }

        void MakeOverlayStyles()
        {
            if (_tagStyle != null) return;
            _tagStyle = new GUIStyle(SnSkin.Skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 16, wordWrap = false };
            _tagStyle.normal.textColor = SnSkin.Cyan;
            _tagSmall = new GUIStyle(_tagStyle) { fontSize = 13, fontStyle = FontStyle.Normal };
            _tagSmall.normal.textColor = SnSkin.Text;
            _chatStyle = new GUIStyle(SnSkin.Skin.label) { fontSize = 15, wordWrap = false };
            _chatStyle.normal.textColor = SnSkin.Text;
        }

        // Black "waiting for players" screen shown instead of the intro until the host starts.
        void DrawLobby()
        {
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.blackTexture);

            float w = Mathf.Min(900f, Screen.width - 40f);
            float x = (Screen.width - w) / 2f;
            float y = Screen.height * 0.26f;

            GUI.Label(new Rect(x, y, w, 60), "WAITING FOR PLAYERS", SnSkin.BigText);
            y += 84;

            var names = new List<string> { Plugin.PlayerName.Value };
            foreach (var r in _remotes.Values) if (r != null) names.Add(r.PlayerName);
            GUI.Label(new Rect(x, y, w, 32), $"{names.Count} {(names.Count == 1 ? "player" : "players")} in the server", SnSkin.MidText);
            y += 36;
            GUI.Label(new Rect(x, y, w, 60), string.Join("     ", names.ToArray()), SnSkin.MidText);
            y += 64;
            GUI.Label(new Rect(x, y, w, 30), $"{_gameMode.ToUpperInvariant()} MODE", SnSkin.Centered(SnSkin.Header));
            y += 50;

            if (IsHost)
            {
                GUI.Label(new Rect(x, y, w, 30), "Everyone in? Press ENTER to start", SnSkin.MidText);
                y += 44;
                if (GUI.Button(new Rect(Screen.width / 2f - 130, y, 260, 52), "START", SnSkin.BigButton)) StartForEveryone();
                if (_joinCode != null)
                    GUI.Label(new Rect(x, y + 80, w, 30), "Join code: " + _joinCode, SnSkin.MidText);
            }
            else
            {
                GUI.Label(new Rect(x, y, w, 30), $"Waiting for {HostName} to start the game...", SnSkin.MidText);
            }
        }

        bool HasRecentChat => _chat.Count > 0 && Time.unscaledTime - _chat[_chat.Count - 1].time <= ChatShowSeconds;

        void DrawChatOverlay()
        {
            if (_menuOpen) return;
            MakeOverlayStyles();
            float now = Time.unscaledTime;
            int shown = 0;
            float y = Screen.height - 200;
            for (int i = _chat.Count - 1; i >= 0 && shown < MaxChatLines; i--)
            {
                if (now - _chat[i].time > ChatShowSeconds) break;
                SnSkin.OutlinedLabel(new Rect(22, y - shown * 22, 900, 24), _chat[i].text, _chatStyle);
                shown++;
            }
        }

        void DrawWindow(int id)
        {
            var state = _client.State;
            GUILayout.BeginHorizontal();
            GUILayout.Label("MULTIPLAYER", SnSkin.Title, GUILayout.Height(40));
            if (GUILayout.Button("X", SnSkin.SmallButton, GUILayout.Width(34), GUILayout.Height(30))) _menuOpen = false;
            GUILayout.EndHorizontal();

            GUILayout.Label(state == ClientState.Connected
                ? $"Connected  ·  {_remotes.Count + 1} player(s)" + (_hostedServer != null ? "  ·  you're hosting" : "")
                : state == ClientState.Connecting ? "Connecting..." : "Not connected", SnSkin.Small);

            if (_joinCode != null)
            {
                GUILayout.Label("JOIN CODE FOR FRIENDS", SnSkin.Header);
                GUILayout.TextField(_joinCode, GUILayout.Height(34)); // selectable so it can be copied
            }

            if (state == ClientState.Disconnected)
            {
                GUILayout.Label("CONNECT", SnSkin.Header);
                Row("Name", () => Plugin.PlayerName.Value = GUILayout.TextField(Plugin.PlayerName.Value, Protocol.MaxNameLength));
                Row("Join code / IP", () => Plugin.ServerAddress.Value = GUILayout.TextField(Plugin.ServerAddress.Value));
                Row("Port", () =>
                {
                    if (int.TryParse(GUILayout.TextField(Plugin.Port.Value.ToString()), out var port) && port > 0 && port < 65536)
                        Plugin.Port.Value = port;
                });
                Row("Host password", () => Plugin.HostPassword.Value = GUILayout.PasswordField(Plugin.HostPassword.Value ?? "", '*', 40));
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("HOST")) Host();
                if (GUILayout.Button("JOIN")) JoinFromUi();
                GUILayout.EndHorizontal();
                if (!Game.InWorld) GUILayout.Label("Tip: use the Multiplayer button in the main menu instead.", SnSkin.Small);
            }
            else
            {
                DrawPlayers();
                if (GUILayout.Button("LEAVE")) Leave();
            }

            GUILayout.Label("CHAT", SnSkin.Header);
            _chatScroll = GUILayout.BeginScrollView(_chatScroll, GUI.skin.box, GUILayout.Height(130));
            foreach (var line in _chat) GUILayout.Label(line.text, SnSkin.Small);
            GUILayout.EndScrollView();

            GUI.enabled = state == ClientState.Connected;
            GUILayout.BeginHorizontal();
            bool enter = Event.current.type == EventType.KeyDown &&
                         (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
            _chatInput = GUILayout.TextField(_chatInput, Protocol.MaxChatLength);
            if ((GUILayout.Button("SEND", GUILayout.Width(70)) || enter) && _chatInput.Trim().Length > 0)
            {
                SendChatText(_chatInput);
                _chatInput = "";
            }
            GUILayout.EndHorizontal();
            GUI.enabled = true;

            GUI.DragWindow();
        }

        void DrawPlayers()
        {
            GUILayout.Label("PLAYERS", SnSkin.Header);
            _playersScroll = GUILayout.BeginScrollView(_playersScroll, GUILayout.MaxHeight(170));
            PlayerRow(LocalId, Plugin.PlayerName.Value + "  (you)");
            foreach (var r in _remotes.Values.Where(r => r != null).OrderBy(r => r.Id).ToList())
                PlayerRow(r.Id, r.PlayerName);
            GUILayout.EndScrollView();
        }

        void PlayerRow(int id, string name)
        {
            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label(name + (id == _hostId ? "   ·   host" : ""));
            if (IsHost && id != LocalId)
            {
                if (GUILayout.Button("KICK", SnSkin.SmallButton, GUILayout.Width(60)))
                    Send(new KickPacket { TargetId = id });
                if (GUILayout.Button(_confirmBan == id ? "SURE?" : "BAN", SnSkin.DangerButton, GUILayout.Width(66)))
                {
                    if (_confirmBan == id) { Send(new KickPacket { TargetId = id, Ban = true }); _confirmBan = 0; }
                    else _confirmBan = id;
                }
            }
            GUILayout.EndHorizontal();
        }

        static void Row(string label, System.Action field)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(120));
            field();
            GUILayout.EndHorizontal();
        }
    }
}
