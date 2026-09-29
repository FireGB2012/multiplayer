using System.Collections.Generic;
using SubnauticaMP.Shared;
using UnityEngine;

namespace SubnauticaMP
{
    // Runs the client (and optional embedded server), sends our position, moves everyone else.
    public sealed class Multiplayer : MonoBehaviour
    {
        const float SendInterval = 1f / 20f; // 20 updates per second
        const float ChatShowSeconds = 10f;
        const int MaxChatLines = 8;

        readonly NetClient _client = new NetClient();
        readonly Dictionary<int, RemotePlayer> _remotes = new Dictionary<int, RemotePlayer>();
        readonly Dictionary<int, string> _names = new Dictionary<int, string>();
        readonly List<(string text, float time)> _chat = new List<(string, float)>();
        NetServer _hostedServer;
        float _sendTimer;
        ClientState _lastState;

        // UI state
        bool _menuOpen;
        Rect _window = new Rect(40, 40, 360, 330);
        string _chatInput = "";
        Vector2 _chatScroll;
        GUIStyle _tagStyle;

        void Update()
        {
            if (Input.GetKeyDown(Plugin.MenuKey.Value)) _menuOpen = !_menuOpen;

            PumpPackets();
            WatchConnection();

            if (_client.State == ClientState.Connected)
            {
                _sendTimer += Time.unscaledDeltaTime;
                if (_sendTimer >= SendInterval)
                {
                    _sendTimer = 0f;
                    SendLocalState();
                }
            }
        }

        void LateUpdate()
        {
            // The game re-locks the cursor every frame; keep it free while our window is up.
            if (_menuOpen)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        void OnDestroy()
        {
            _client.Disconnect();
            _hostedServer?.Stop();
        }

        void OnApplicationQuit() => OnDestroy();

        // ---------- networking ----------

        void PumpPackets()
        {
            while (_client.TryDequeue(out var packet))
            {
                switch (packet)
                {
                    case WelcomePacket welcome:
                        foreach (var p in welcome.Players) AddRemote(p.Id, p.Name);
                        AddChat($"Connected! {welcome.Players.Count} other player(s) here.");
                        break;

                    case PlayerJoinedPacket joined:
                        AddRemote(joined.Id, joined.Name);
                        AddChat($"{joined.Name} joined");
                        break;

                    case PlayerLeftPacket left:
                        AddChat($"{NameOf(left.Id)} left");
                        RemoveRemote(left.Id);
                        break;

                    case PlayerStatePacket state:
                        if (_remotes.TryGetValue(state.Id, out var remote)) remote.SetTarget(state);
                        break;

                    case ChatPacket chat:
                        AddChat($"{NameOf(chat.SenderId)}: {chat.Text}");
                        break;

                    case RejectedPacket rejected:
                        AddChat("Server said no: " + rejected.Reason);
                        break;
                }
            }
        }

        void WatchConnection()
        {
            var state = _client.State;
            if (state == _lastState) return;
            if (state == ClientState.Disconnected && _lastState != ClientState.Disconnected)
            {
                AddChat("Disconnected: " + _client.LastError);
                ClearRemotes();
            }
            _lastState = state;
        }

        void SendLocalState()
        {
            var player = Player.main;
            if (player == null) return; // still in the main menu / loading

            var t = player.transform;
            var rot = t.rotation;
            var cam = MainCamera.camera;
            if (cam != null) rot = Quaternion.Euler(0f, cam.transform.eulerAngles.y, 0f); // face where they look

            var flags = PlayerFlags.None;
            if (player.IsUnderwater()) flags |= PlayerFlags.Underwater;
            if (player.IsInSub()) flags |= PlayerFlags.InBase;
            if (player.GetVehicle() != null) flags |= PlayerFlags.InVehicle;

            _client.Send(new PlayerStatePacket
            {
                Position = new Vec3(t.position.x, t.position.y, t.position.z),
                Rotation = new Quat(rot.x, rot.y, rot.z, rot.w),
                Flags = flags,
            });
        }

        void Host()
        {
            if (_hostedServer == null)
            {
                _hostedServer = new NetServer();
                _hostedServer.Log += msg => Plugin.Log.LogInfo("[server] " + msg);
                try
                {
                    _hostedServer.Start(Plugin.Port.Value);
                }
                catch (System.Exception e)
                {
                    AddChat("Couldn't host: " + e.Message);
                    _hostedServer = null;
                    return;
                }
                AddChat($"Hosting on port {Plugin.Port.Value}. Friends join your IP.");
            }
            _client.Connect("127.0.0.1", Plugin.Port.Value, Plugin.PlayerName.Value);
        }

        void Join()
        {
            AddChat($"Joining {Plugin.ServerAddress.Value}:{Plugin.Port.Value}...");
            _client.Connect(Plugin.ServerAddress.Value.Trim(), Plugin.Port.Value, Plugin.PlayerName.Value);
        }

        void Leave()
        {
            _client.Disconnect();
            if (_hostedServer != null)
            {
                _hostedServer.Stop();
                _hostedServer = null;
                AddChat("Stopped hosting.");
            }
            ClearRemotes();
        }

        // ---------- remote players ----------

        void AddRemote(int id, string name)
        {
            _names[id] = name;
            if (id == _client.LocalId || _remotes.ContainsKey(id)) return;
            _remotes[id] = RemotePlayer.Create(id, name);
        }

        void RemoveRemote(int id)
        {
            if (_remotes.TryGetValue(id, out var remote)) Destroy(remote.gameObject);
            _remotes.Remove(id);
            _names.Remove(id);
        }

        void ClearRemotes()
        {
            foreach (var r in _remotes.Values) if (r != null) Destroy(r.gameObject);
            _remotes.Clear();
            _names.Clear();
        }

        string NameOf(int id) => id == _client.LocalId ? Plugin.PlayerName.Value
            : _names.TryGetValue(id, out var n) ? n : "Player " + id;

        void AddChat(string line)
        {
            Plugin.Log.LogInfo(line);
            _chat.Add((line, Time.unscaledTime));
            if (_chat.Count > 50) _chat.RemoveAt(0);
        }

        // ---------- UI ----------

        void OnGUI()
        {
            DrawNameTags();
            DrawChatOverlay();
            if (_menuOpen) _window = GUILayout.Window(0x5B4D50, _window, DrawWindow, "Subnautica Multiplayer");
        }

        void DrawNameTags()
        {
            var cam = MainCamera.camera != null ? MainCamera.camera : Camera.main;
            if (cam == null || _remotes.Count == 0) return;
            if (_tagStyle == null)
            {
                _tagStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                _tagStyle.normal.textColor = Color.cyan;
            }

            foreach (var r in _remotes.Values)
            {
                if (r == null) continue;
                var world = r.transform.position + Vector3.up * 1.3f;
                var screen = cam.WorldToScreenPoint(world);
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
                GUI.Label(new Rect(20, y - shown * 20, 600, 22), _chat[i].text);
                shown++;
            }
        }

        void DrawWindow(int id)
        {
            var state = _client.State;
            GUILayout.Label("Status: " + (state == ClientState.Connected
                ? $"Connected ({_remotes.Count} other player(s))" + (_hostedServer != null ? " - hosting" : "")
                : state.ToString()));

            GUI.enabled = state == ClientState.Disconnected;
            GUILayout.BeginHorizontal();
            GUILayout.Label("Name", GUILayout.Width(60));
            Plugin.PlayerName.Value = GUILayout.TextField(Plugin.PlayerName.Value, Protocol.MaxNameLength);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Server IP", GUILayout.Width(60));
            Plugin.ServerAddress.Value = GUILayout.TextField(Plugin.ServerAddress.Value);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Port", GUILayout.Width(60));
            if (int.TryParse(GUILayout.TextField(Plugin.Port.Value.ToString()), out var port) && port > 0 && port < 65536)
                Plugin.Port.Value = port;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Host")) Host();
            if (GUILayout.Button("Join")) Join();
            GUI.enabled = true;
            if (GUILayout.Button("Leave")) Leave();
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            _chatScroll = GUILayout.BeginScrollView(_chatScroll, GUILayout.Height(120));
            foreach (var line in _chat) GUILayout.Label(line.text);
            GUILayout.EndScrollView();

            GUI.enabled = state == ClientState.Connected;
            GUILayout.BeginHorizontal();
            _chatInput = GUILayout.TextField(_chatInput, Protocol.MaxChatLength);
            bool enter = Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return;
            if ((GUILayout.Button("Send", GUILayout.Width(60)) || enter) && _chatInput.Trim().Length > 0)
            {
                _client.Send(new ChatPacket { Text = _chatInput });
                _chatInput = "";
            }
            GUILayout.EndHorizontal();
            GUI.enabled = true;

            GUI.DragWindow();
        }
    }
}
