using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using SubnauticaMP.Shared;
using UnityEngine;

namespace SubnauticaMP
{
    // The multiplayer session: owns the connection (and the server when you host in-game),
    // routes packets to the sync systems, and draws the F8 window + chat + name tags.
    public sealed partial class Session : MonoBehaviour
    {
        public static Session Instance { get; private set; }

        const float SendInterval = 1f / 20f;
        const float SettleSeconds = 4f; // wait this long after a save loads before syncing

        readonly NetClient _client = new NetClient();
        readonly Dictionary<int, RemotePlayer> _remotes = new Dictionary<int, RemotePlayer>();
        readonly Dictionary<int, string> _names = new Dictionary<int, string>();
        internal WorldSync World;
        internal VehicleSync Vehicles;

        NetServer _hostedServer;
        int _hostedPort;
        string _joinCode;
        float _sendTimer;
        float _inWorldTimer;
        ClientState _lastState;
        LaunchInfo _autoJoin;
        bool _autoStart;        // came from the launcher: start/load the game ourselves once connected
        bool _newGameStarted;   // we clicked New Game for this world, so set its game mode after loading
        bool _modeApplied;
        bool _slotRecorded;
        int _hostId;
        string _worldId;
        string _gameMode = GameModes.Survival;

        public bool Joined => _client.State == ClientState.Connected;
        public bool Connecting => _client.State == ClientState.Connecting;
        public int LocalId => _client.LocalId;
        public bool InWorldAndSettled => _inWorldTimer >= SettleSeconds;
        public bool WorldStarted { get; private set; }
        public bool InLobby => Joined && !WorldStarted;
        public bool IsHost => Joined && _hostId == LocalId;
        public string HostName => NameOf(_hostId);

        void Awake()
        {
            Instance = this;
            World = new WorldSync(this);
            Vehicles = new VehicleSync(this);

            var launchPath = Path.Combine(Plugin.Folder, LaunchInfo.FileName);
            _autoJoin = LaunchInfo.TryLoad(launchPath);
            try { File.Delete(launchPath); } catch { }
            if (_autoJoin != null)
            {
                if (!string.IsNullOrEmpty(_autoJoin.PlayerName)) Plugin.PlayerName.Value = Protocol.CleanName(_autoJoin.PlayerName);
                AddChat($"Launcher: joining {_autoJoin.Host}:{_autoJoin.Port}...");
            }
        }

        void Update()
        {
            if (Input.GetKeyDown(Plugin.MenuKey.Value)) _menuOpen = !_menuOpen;
            if (Game.MainMenu != null) MainMenuButton.Update(OpenMultiplayerMenu);
            else _mpMenuOpen = false;

            _inWorldTimer = Game.InWorld ? _inWorldTimer + Time.unscaledDeltaTime : 0f;

            // From the launcher: connect as soon as the main menu is up, then the server tells us what to load.
            if (_autoJoin != null && _client.State == ClientState.Disconnected && (Game.MainMenu != null || Game.InWorld))
            {
                var a = _autoJoin;
                _autoJoin = null;
                _autoStart = !Game.InWorld;
                _lastAddress = a.Host == "127.0.0.1" ? null : a.Host + ":" + a.Port;
                Connect(a.Host, a.Port);
            }

            PumpPackets();
            WatchConnection();
            Lobby.Update();
            if (!Joined) return;

            if (Lobby.Holding && IsHost &&
                (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space)))
                StartForEveryone();

            if (!InWorldAndSettled) _slotRecorded = false;
            else
            {
                if (!_slotRecorded)
                {
                    _slotRecorded = true;
                    SafeRun("save slot", () => WorldSlots.Set(_worldId, Game.CurrentSaveSlot()));
                }
                if (_newGameStarted && !_modeApplied)
                {
                    _modeApplied = true;
                    SafeRun("gamemode", () => Game.SetGameMode(_gameMode));
                }
            }

            SafeRun("world", World.Update);
            SafeRun("vehicles", Vehicles.Update);

            _sendTimer += Time.unscaledDeltaTime;
            if (_sendTimer >= SendInterval)
            {
                _sendTimer = 0f;
                SafeRun("player", SendLocalState);
            }
        }

        void LateUpdate()
        {
            if (Joined) SafeRun("vehicles", Vehicles.LateUpdate);
            if (_menuOpen || Lobby.Holding)
            {
                // the game re-locks the cursor every frame; keep it free while our window is up
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        // One broken feature shouldn't take the whole mod down with it.
        readonly HashSet<string> _failedOnce = new HashSet<string>();
        void SafeRun(string what, Action a)
        {
            try { a(); }
            catch (Exception e)
            {
                if (_failedOnce.Add(what)) Plugin.Log.LogError($"[{what}] {e}");
            }
        }

        void OnDestroy() => Shutdown();
        void OnApplicationQuit() => Shutdown();

        void Shutdown()
        {
            _client.Disconnect();
            StopHosting();
        }

        // ---------- sending ----------

        public void Send(Packet p) => _client.Send(p);

        public void SendUnlock(UnlockKind kind, string key)
        {
            if (string.IsNullOrEmpty(key) || !World.RememberUnlock(kind, key)) return;
            Send(new UnlockPacket { Kind = kind, Key = key });
        }

        void SendLocalState()
        {
            var player = Game.LocalPlayer;
            if (player == null) return;

            var t = player.transform;
            var rot = t.rotation;
            var cam = Game.Camera;
            if (cam != null) rot = Quaternion.Euler(0f, cam.transform.eulerAngles.y, 0f); // face where they look

            var flags = PlayerFlags.None;
            if (Game.Is(player, "IsUnderwater")) flags |= PlayerFlags.Underwater;
            if (Game.Is(player, "IsInSub")) flags |= PlayerFlags.InBase;
            if (Game.PlayerVehicle(player) != null) flags |= PlayerFlags.InVehicle;

            Send(new PlayerStatePacket
            {
                Position = new Vec3(t.position.x, t.position.y, t.position.z),
                Rotation = new Quat(rot.x, rot.y, rot.z, rot.w),
                Flags = flags,
            });
        }

        // ---------- receiving ----------

        void PumpPackets()
        {
            while (_client.TryDequeue(out var packet))
            {
                try { Handle(packet); }
                catch (Exception e) { Plugin.Log.LogError($"Handling {packet.Type} failed: {e}"); }
            }
        }

        void Handle(Packet packet)
        {
            switch (packet)
            {
                case WelcomePacket welcome:
                    foreach (var p in welcome.Players) AddRemote(p.Id, p.Name);
                    _hostId = welcome.HostId;
                    _worldId = welcome.World.WorldId;
                    _slotRecorded = false;
                    _gameMode = welcome.World.GameMode;
                    WorldStarted = welcome.World.Started;
                    World.OnWelcome(welcome.World);
                    Vehicles.OnWelcome(welcome.World);
                    AddChat($"Connected! {welcome.Players.Count} other player(s) here. {_gameMode} world.");
                    if (_autoStart && !Game.InWorld) StartCoroutine(AutoStart());
                    _autoStart = false;
                    if (_lastAddress != null) SafeRun("server list", () => ServerList.Remember(_lastAddress));
                    break;

                case HostPacket host:
                    _hostId = host.HostId;
                    if (Joined) AddChat($"{NameOf(_hostId)} is the host now");
                    break;

                case StartGamePacket _:
                    WorldStarted = true;
                    AddChat($"{HostName} started the game!");
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

                case UnlockPacket unlock: World.OnUnlock(unlock); break;
                case EntityRemovedPacket removed: World.OnEntityRemoved(removed.EntityId); break;
                case TimeSyncPacket time: World.OnTime(time.TimePassed); break;
                case VehicleSpawnedPacket vs: Vehicles.OnSpawned(vs.Vehicle); break;
                case VehicleStatePacket vst: Vehicles.OnState(vst); break;
                case VehicleOwnerPacket vo: Vehicles.OnOwner(vo.Id, vo.OwnerId); break;
                case VehicleRemovedPacket vr: Vehicles.OnRemoved(vr.Id); break;
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
                World.Reset();
                Vehicles.Reset();
            }
            _lastState = state;
        }

        // ---------- launcher auto-start ----------

        // We're on the main menu and connected: load our save for this world, or start a new game in its mode.
        System.Collections.IEnumerator AutoStart()
        {
            yield return new WaitForSecondsRealtime(1f); // let the menu finish setting up

            var slot = WorldSlots.Get(_worldId);
            if (slot != null)
            {
                // the save list loads in the background; give it a few seconds
                for (float t = 0f; t < 8f && !Game.SaveExists(slot); t += 0.5f)
                    yield return new WaitForSecondsRealtime(0.5f);

                if (Game.SaveExists(slot))
                {
                    AddChat("Loading your save for this world...");
                    bool ok = false;
                    SafeRun("load save", () => ok = Game.TryLoadSlot(slot, this));
                    if (!ok) AddChat("Couldn't open your save by itself. Load it from the menu.");
                    yield break;
                }
                AddChat("Your save for this world is gone, starting a fresh one.");
            }

            AddChat($"Starting a new {_gameMode} game...");
            bool started = false;
            SafeRun("new game", () => started = Game.StartNewGame(_gameMode, this));
            _newGameStarted = started;
            _modeApplied = false;
            if (!started) AddChat("Couldn't start the game by itself. Click Play > New Game.");
        }

        void StartForEveryone()
        {
            if (!IsHost || WorldStarted) return;
            Send(new StartGamePacket());
        }

        // ---------- connect / host ----------

        string _lastAddress; // what the player typed, remembered in the server list once it works

        // From the main menu's Multiplayer screen: join, then load/start the world by ourselves.
        public void JoinFromMenu(string address)
        {
            if (_client.State != ClientState.Disconnected) return;
            if (!JoinCode.TryParseAddress(address, Protocol.DefaultPort, out var host, out var port))
            {
                AddChat("That doesn't look like a join code or IP");
                return;
            }
            _autoStart = !Game.InWorld;
            _lastAddress = address.Trim();
            Connect(host, port);
        }

        // Host a world from the main menu: server runs inside this game, then we join it like everyone else.
        public void HostFromMenu(string worldName, string gameMode)
        {
            if (_client.State != ClientState.Disconnected) return;
            StopHosting();
            var path = Path.Combine(Path.Combine(Plugin.Folder, "worlds"), SafeFileName(worldName.Trim()) + ".dat");
            var server = new NetServer(path, gameMode);
            server.Log += msg => Plugin.Log.LogInfo("[server] " + msg);
            try { server.Start(Plugin.Port.Value); }
            catch (Exception e)
            {
                AddChat("Couldn't host: " + e.Message);
                return;
            }
            _hostedServer = server;
            _hostedPort = server.Port;
            AddChat($"Hosting '{worldName}' on port {_hostedPort}. Opening your router for friends...");
            OpenRouterPort(_hostedPort);
            _autoStart = !Game.InWorld;
            _lastAddress = null;
            Connect("127.0.0.1", _hostedPort);
        }

        public static string WorldsFolder => Path.Combine(Plugin.Folder, "worlds");

        void Connect(string host, int port)
        {
            AddChat($"Joining {host}:{port}...");
            _client.Connect(host, port, Plugin.PlayerName.Value);
        }

        void JoinFromUi()
        {
            if (!JoinCode.TryParseAddress(Plugin.ServerAddress.Value, Plugin.Port.Value, out var host, out var port))
            {
                AddChat("That doesn't look like a join code or IP");
                return;
            }
            Connect(host, port);
        }

        void Host()
        {
            if (_hostedServer == null)
            {
                var worldFile = Path.Combine(Path.Combine(Plugin.Folder, "worlds"), SafeFileName(Game.CurrentSaveSlot()) + ".dat");
                // hosting from inside a save you're already playing: no lobby
                var server = new NetServer(worldFile, Game.CurrentGameMode(), started: true);
                server.Log += msg => Plugin.Log.LogInfo("[server] " + msg);
                try
                {
                    server.Start(Plugin.Port.Value);
                }
                catch (Exception e)
                {
                    AddChat("Couldn't host: " + e.Message);
                    return;
                }
                _hostedServer = server;
                _hostedPort = server.Port;
                AddChat($"Hosting on port {_hostedPort}. Opening your router for friends on other wifi...");
                OpenRouterPort(_hostedPort);
            }
            Connect("127.0.0.1", _hostedPort);
        }

        void OpenRouterPort(int port)
        {
            new Thread(() =>
            {
                var r = Upnp.OpenPort(port, "Subnautica Multiplayer");
                string ip = r.Success ? r.ExternalIp : null;
                if (string.IsNullOrEmpty(ip)) ip = Upnp.LookUpPublicIp();
                string msg;
                if (r.Success && !r.BehindCgnat)
                    msg = "Router opened! Friends anywhere can join.";
                else if (r.BehindCgnat)
                    msg = "Your internet provider blocks hosting (CGNAT). Use Radmin VPN / Tailscale / playit.gg, or let a friend host.";
                else
                    msg = "Couldn't auto-open your router (" + r.Error + "). Forward TCP port " + port + " by hand, or use Radmin VPN / Tailscale.";

                if (System.Net.IPAddress.TryParse(ip ?? "", out var addr) && addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    _joinCode = JoinCode.Encode(addr, port);
                _pendingChat.Enqueue(msg);
                if (_joinCode != null) _pendingChat.Enqueue("Join code: " + _joinCode + " (F8 to see it again)");
            }) { IsBackground = true, Name = "SubnauticaMP upnp" }.Start();
        }

        void StopHosting()
        {
            if (_hostedServer == null) return;
            _hostedServer.Stop();
            Upnp.ClosePort(_hostedPort);
            _hostedServer = null;
            _joinCode = null;
            AddChat("Stopped hosting.");
        }

        void Leave()
        {
            _client.Disconnect();
            StopHosting();
            ClearRemotes();
            World.Reset();
            Vehicles.Reset();
        }

        static string SafeFileName(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
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
            if (_remotes.TryGetValue(id, out var remote) && remote != null) Destroy(remote.gameObject);
            _remotes.Remove(id);
            _names.Remove(id);
        }

        void ClearRemotes()
        {
            foreach (var r in _remotes.Values) if (r != null) Destroy(r.gameObject);
            _remotes.Clear();
            _names.Clear();
        }

        public string NameOf(int id) => id == _client.LocalId ? Plugin.PlayerName.Value
            : _names.TryGetValue(id, out var n) ? n : "Player " + id;
    }
}
