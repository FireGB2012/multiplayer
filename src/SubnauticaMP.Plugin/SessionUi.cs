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
        GUIStyle _tagStyle, _codeStyle;

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
            DrainPendingChat();
            DrawNameTags();
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
                GUI.Label(new Rect(screen.x - 100, Screen.height - screen.y - 12, 200, 24), $"{r.PlayerName} ({dist:0}m)", _tagStyle);
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
