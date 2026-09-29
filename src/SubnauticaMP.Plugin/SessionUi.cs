using System.Collections.Concurrent;
using System.Collections.Generic;
using SubnauticaMP.Shared;
using UnityEngine;

namespace SubnauticaMP
{
    // F8 window, chat overlay and name tags (plain Unity IMGUI so it works in any menu).
    public sealed partial class Session
    {
        const float ChatShowSeconds = 10f;
        const int MaxChatLines = 8;

        readonly List<(string text, float time)> _chat = new List<(string, float)>();
        readonly ConcurrentQueue<string> _pendingChat = new ConcurrentQueue<string>(); // from background threads

        bool _menuOpen;
        Rect _window = new Rect(40, 40, 380, 380);
        string _chatInput = "";
        Vector2 _chatScroll;
        GUIStyle _tagStyle, _codeStyle, _bigStyle, _midStyle;

        public void AddChat(string line)
        {
            Plugin.Log.LogInfo(line);
            _chat.Add((line, Time.unscaledTime));
            if (_chat.Count > 50) _chat.RemoveAt(0);
            _chatScroll.y = float.MaxValue;
        }

        void DrainPendingChat()
        {
            while (_pendingChat.TryDequeue(out var line)) AddChat(line);
        }

        void OnGUI()
        {
            GUI.depth = -1000; // above the game's own UI
            DrainPendingChat();
            DrawMainMenuUi();
            if (Lobby.Holding) DrawLobby();
            else DrawNameTags();
            DrawChatOverlay();
            if (_menuOpen) _window = GUILayout.Window(0x5B4D50, _window, DrawWindow, "Subnautica Multiplayer  (F8)");
        }

        void DrawNameTags()
        {
            if (_remotes.Count == 0) return;
            var cam = Game.Camera;
            if (cam == null) return;
            if (_tagStyle == null)
            {
                _tagStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                _tagStyle.normal.textColor = Color.cyan;
            }

            foreach (var r in _remotes.Values)
            {
                if (r == null || !r.gameObject.activeSelf) continue;
                var screen = cam.WorldToScreenPoint(r.transform.position + Vector3.up * 1.3f);
                if (screen.z <= 0f) continue; // behind us
                float dist = Vector3.Distance(cam.transform.position, r.transform.position);
                GUI.Label(new Rect(screen.x - 130, Screen.height - screen.y - 12, 260, 24), $"{r.PlayerName} ({dist:0}m)", _tagStyle);
                if (r.HasVitals)
                    GUI.Label(new Rect(screen.x - 130, Screen.height - screen.y + 8, 260, 24), $"HP {r.Health}   Food {r.Food}   Water {r.Water}", _tagStyle);
            }
        }

        // Black "waiting for players" screen shown instead of the intro until the host starts.
        void DrawLobby()
        {
            if (_bigStyle == null)
            {
                _bigStyle = new GUIStyle(GUI.skin.label) { fontSize = 42, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                _bigStyle.normal.textColor = new Color(0.37f, 0.83f, 0.88f);
                _midStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, alignment = TextAnchor.MiddleCenter, wordWrap = true };
                _midStyle.normal.textColor = Color.white;
            }

            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.blackTexture);

            float w = Mathf.Min(900f, Screen.width - 40f);
            float x = (Screen.width - w) / 2f;
            float y = Screen.height * 0.28f;

            GUI.Label(new Rect(x, y, w, 60), "WAITING FOR PLAYERS", _bigStyle);
            y += 80;

            var names = new List<string> { Plugin.PlayerName.Value };
            foreach (var r in _remotes.Values) if (r != null) names.Add(r.PlayerName);
            GUI.Label(new Rect(x, y, w, 32), $"{names.Count} {(names.Count == 1 ? "player" : "players")} in the server", _midStyle);
            y += 34;
            GUI.Label(new Rect(x, y, w, 60), string.Join("   ", names.ToArray()), _midStyle);
            y += 70;
            GUI.Label(new Rect(x, y, w, 30), $"{_gameMode} mode", _midStyle);
            y += 50;

            if (IsHost)
            {
                GUI.Label(new Rect(x, y, w, 30), "Everyone in? Press ENTER to start", _midStyle);
                y += 44;
                if (GUI.Button(new Rect(Screen.width / 2f - 110, y, 220, 50), "START")) StartForEveryone();
                if (_joinCode != null)
                    GUI.Label(new Rect(x, y + 80, w, 30), "Join code: " + _joinCode, _midStyle);
            }
            else
            {
                GUI.Label(new Rect(x, y, w, 30), $"Waiting for {HostName} to start the game...", _midStyle);
            }
        }

        void DrawChatOverlay()
        {
            if (_menuOpen) return;
            float now = Time.unscaledTime;
            int shown = 0;
            float y = Screen.height - 200;
            for (int i = _chat.Count - 1; i >= 0 && shown < MaxChatLines; i--)
            {
                if (now - _chat[i].time > ChatShowSeconds) break;
                GUI.Label(new Rect(20, y - shown * 20, 700, 22), _chat[i].text);
                shown++;
            }
        }

        void DrawWindow(int id)
        {
            var state = _client.State;
            GUILayout.Label("Status: " + (state == ClientState.Connected
                ? $"Connected ({_remotes.Count} other player(s))" + (_hostedServer != null ? " - hosting" : "")
                : state.ToString()));

            if (_joinCode != null)
            {
                if (_codeStyle == null) _codeStyle = new GUIStyle(GUI.skin.textField) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
                GUILayout.Label("Join code for friends:");
                GUILayout.TextField(_joinCode, _codeStyle); // selectable so it can be copied
            }

            GUI.enabled = state == ClientState.Disconnected;
            Row("Name", () => Plugin.PlayerName.Value = GUILayout.TextField(Plugin.PlayerName.Value, Protocol.MaxNameLength));
            Row("Join code / IP", () => Plugin.ServerAddress.Value = GUILayout.TextField(Plugin.ServerAddress.Value));
            Row("Port", () =>
            {
                if (int.TryParse(GUILayout.TextField(Plugin.Port.Value.ToString()), out var port) && port > 0 && port < 65536)
                    Plugin.Port.Value = port;
            });

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Host")) Host();
            if (GUILayout.Button("Join")) JoinFromUi();
            GUI.enabled = true;
            if (GUILayout.Button("Leave")) Leave();
            GUILayout.EndHorizontal();

            if (!Game.InWorld && state == ClientState.Disconnected)
                GUILayout.Label("Tip: load a save first, then host/join.");

            GUILayout.Space(6);
            _chatScroll = GUILayout.BeginScrollView(_chatScroll, GUILayout.Height(140));
            foreach (var line in _chat) GUILayout.Label(line.text);
            GUILayout.EndScrollView();

            GUI.enabled = state == ClientState.Connected;
            GUILayout.BeginHorizontal();
            bool enter = Event.current.type == EventType.KeyDown &&
                         (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
            _chatInput = GUILayout.TextField(_chatInput, Protocol.MaxChatLength);
            if ((GUILayout.Button("Send", GUILayout.Width(60)) || enter) && _chatInput.Trim().Length > 0)
            {
                Send(new ChatPacket { Text = _chatInput });
                _chatInput = "";
            }
            GUILayout.EndHorizontal();
            GUI.enabled = true;

            GUI.DragWindow();
        }

        static void Row(string label, System.Action field)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(100));
            field();
            GUILayout.EndHorizontal();
        }
    }
}
