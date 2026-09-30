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
        internal StructureSync Structures;
        internal ContainerSync Containers;
        internal ItemSync Items;
        internal StorySync Story;
        internal CreatureSync Creatures;
        internal SleepSync Sleep;
        MainMenuUi _menuUi;
        internal PowerSync Power;
        internal BaseLifeSync BaseLife;
        internal GhostSync Ghosts;
        internal EmoteSync Emoting;
        internal EmoteWheel Wheel;
        internal IEnumerable<RemotePlayer> Remotes => _remotes.Values;

        NetServer _hostedServer;
        int _hostedPort;
        string _joinCode;
        float _sendTimer;
        float _inWorldTimer;
        ClientState _lastState;
        LaunchInfo _autoJoin;
        static LaunchInfo _pendingLaunch;
        static bool _launchRead;
        bool _autoStart;        // came from the launcher: start/load the game ourselves once connected
        bool _newGameStarted;   // we clicked New Game for this world, so set its game mode after loading
        bool _modeApplied;
        bool _slotRecorded;
        bool _wasInMenu;
        int _hostId;
        string _worldId;
        string _gameMode = GameModes.Survival;

        public bool Joined => _client.State == ClientState.Connected;
        public bool Connecting => _client.State == ClientState.Connecting;
        public int LocalId => _client.LocalId;
        public bool InWorldAndSettled => _inWorldTimer >= SettleSeconds;
        public bool WorldStarted { get; private set; }
        public bool FreshStart { get; set; }   // we were here when the host hit Start: the intro waits for everyone
        public string WorldId => _worldId;
        public bool InLobby => Joined && !WorldStarted;
        public bool IsHost => Joined && _hostId == LocalId;
        public string HostName => NameOf(_hostId);

        void Awake()
        {
            Instance = this;
            _menuUi = new MainMenuUi(this);
            World = new WorldSync(this);
            Vehicles = new VehicleSync(this);
            Structures = new StructureSync(this);
            Containers = new ContainerSync(this);
            Items = new ItemSync(this);
            Story = new StorySync(this);
            Creatures = new CreatureSync(this);
            Sleep = new SleepSync(this);
            Power = new PowerSync(this);
            BaseLife = new BaseLifeSync(this);
            Ghosts = new GhostSync(this);
            Emoting = new EmoteSync(this);
            Wheel = new EmoteWheel(this);

            if (!_launchRead)
            {
                _launchRead = true;
                var launchPath = Path.Combine(Plugin.Folder, LaunchInfo.FileName);
                _pendingLaunch = LaunchInfo.TryLoad(launchPath);
                try { File.Delete(launchPath); } catch { }
            }
            _autoJoin = _pendingLaunch; // survives the session being rebuilt
            if (_autoJoin != null)
            {
                if (!string.IsNullOrEmpty(_autoJoin.PlayerName)) Plugin.PlayerName.Value = Protocol.CleanName(_autoJoin.PlayerName);
                AddChat($"Launcher: joining {_autoJoin.Host}:{_autoJoin.Port}...");
            }
        }

        void Update()
        {
            if (Input.GetKeyDown(Plugin.MenuKey.Value)) ToggleMultiplayerWindow();
            if (Plugin.EnterForChat.Value && Joined && Game.InWorld && !Lobby.Holding && !Loading && !_menuOpen && !PausePageShowing &&
                Cursor.lockState == CursorLockMode.Locked && !UiKit.Typing() &&
                (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) &&
                !(_pauseMenu != null && _pauseMenu.gameObject.activeInHierarchy))
                ShowPausePage(true); // Enter = chat
            if (!Lobby.Holding && !Loading && !_menuOpen && !PausePageShowing) SafeRun("emote wheel", Wheel.Update);
            else Wheel.Close();
            bool inMenu = Game.MainMenu != null;
            if (inMenu != _wasInMenu)
            {
                _wasInMenu = inMenu;
                Plugin.Log.LogInfo(inMenu ? "Main menu detected" : "Left the main menu");
            }
            if (inMenu)
            {
                MainMenuButton.Update(OpenMultiplayerMenu);
                SafeRun("menu", _menuUi.Update);
            }
            else _mpMenuOpen = false;
            SafeRun("pause menu", UpdatePauseUi);
            SafeRun("overlay", UpdateOverlay);

            _inWorldTimer = Game.InWorld ? _inWorldTimer + Time.unscaledDeltaTime : 0f;

            // From the launcher: connect as soon as the main menu is up, then the server tells us what to load.
            if (_autoJoin != null && _client.State == ClientState.Disconnected && (Game.MainMenu != null || Game.InWorld))
            {
                var a = _autoJoin;
                _autoJoin = _pendingLaunch = null;
                _autoStart = !Game.InWorld;
                _lastAddress = a.Host == "127.0.0.1" ? null : a.Host + ":" + a.Port;
                Connect(a.Host, a.Port, a.Password);
            }

            PumpPacketsWithBudget();
            WatchConnection();
            SafeRun("object lists", SceneIndex.Tick);
            SafeRun("duplicate cleanup", DuplicateCleaner.Tick);
            RunDue();
            if (Time.unscaledTime >= _optionsSyncAt) { _optionsSyncAt = Time.unscaledTime + 1f; NautilusCompat.SyncOptions(); }
            UpdateLoading();
            SafeRun("performance", () => { SubnauticaMP.Performance.Apply(); SubnauticaMP.Performance.Update(Loading || Lobby.Holding || (!Game.InWorld && Game.MainMenu == null)); });
            WatchFrameTime();
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
            SafeRun("building", Structures.Update);
            SafeRun("lockers", Containers.Update);
            SafeRun("items", Items.Update);
            SafeRun("story", Story.Update);
            SafeRun("creatures", Creatures.Update);
            SafeRun("beds", Sleep.Update);
            SafeRun("power", Power.Update);
            SafeRun("base life", BaseLife.Update);
            SafeRun("build holograms", Ghosts.Update);
            SafeRun("emotes", Emoting.Update);

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
            SafeRun("emote camera", Emoting.LateUpdate);
            SafeRun("emote preview", Wheel.LateUpdate);
            Game.TryDo("avatar input", () => Game.SetAvatarInput(!Wheel.IsOpen)); // clicks on the wheel mustn't re-lock the mouse
            if (_menuOpen || Lobby.Holding || Wheel.IsOpen)
            {
                // the game re-locks the cursor every frame; keep it free while our window is up
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        // One broken feature shouldn't take the whole mod down with it.
        readonly HashSet<string> _failedOnce = new HashSet<string>();
        readonly System.Diagnostics.Stopwatch _runTimer = new System.Diagnostics.Stopwatch();
        readonly Dictionary<string, float> _slowLogged = new Dictionary<string, float>();

        void SafeRun(string what, Action a)
        {
            _runTimer.Restart();
            try { a(); }
            catch (Exception e)
            {
                if (_failedOnce.Add(what)) Plugin.Log.LogError($"[{what}] {e}");
            }
            // lag hunting: anything of ours that takes a noticeable part of a frame goes in the log
            var ms = _runTimer.Elapsed.TotalMilliseconds;
            if (ms > 15 && (!_slowLogged.TryGetValue(what, out var last) || Time.unscaledTime - last > 10f))
            {
                _slowLogged[what] = Time.unscaledTime;
                Plugin.Log.LogWarning($"[lag] '{what}' took {ms:0} ms this frame");
            }
        }

        float _lastHitchLog;

        // Whole-frame hitches (ours or the game's), so a log shows when the stutters happen.
        void WatchFrameTime()
        {
            if (!Game.InWorld || Loading) return;
            float dt = Time.unscaledDeltaTime;
            if (dt > 0.25f && Time.unscaledTime - _lastHitchLog > 5f)
            {
                _lastHitchLog = Time.unscaledTime;
                Plugin.Log.LogWarning($"[lag] frame took {dt * 1000:0} ms (players: {_remotes.Count + 1}, creatures synced: {Creatures.TrackedCount}, GC heap {GC.GetTotalMemory(false) / (1024 * 1024)} MB)");
            }
        }

        void OnDestroy()
        {
            Plugin.Log.LogWarning("Multiplayer session object was destroyed (will be recreated on next scene load)");
            if (Instance == this) Instance = null;
            Shutdown();
        }
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

        string _heldCache = "", _gearCache = "", _animCache = "";
        float _heldCheck;

        void SendLocalState()
        {
            // what's in our hand: checked a few times a second, not every packet
            if (Time.unscaledTime >= _heldCheck)
            {
                _heldCheck = Time.unscaledTime + 0.3f;
                try { _heldCache = Game.HeldTech(); } catch { _heldCache = ""; }
                try { _gearCache = PlayerLooks.ReadGear(); } catch { _gearCache = ""; }
            }
            try { _animCache = PlayerLooks.ReadAnim(); } catch { _animCache = ""; }
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
            if (Sleep.LocalAsleep) flags |= PlayerFlags.Sleeping;

            var (health, food, water) = Game.Vitals(player);

            // standing in a synced Cyclops: send where we are inside it, so we don't slide around on their screen
            string subId = "";
            Vec3 localPos = default;
            Quat localRot = default;
            var sub = Game.PlayerSub(player);
            if (Game.IsCyclops(sub))
            {
                var id = Game.GetId(sub.gameObject);
                if (Vehicles.IsTracked(id))
                {
                    subId = id;
                    var lp = sub.transform.InverseTransformPoint(t.position);
                    var lr = Quaternion.Inverse(sub.transform.rotation) * rot;
                    localPos = new Vec3(lp.x, lp.y, lp.z);
                    localRot = new Quat(lr.x, lr.y, lr.z, lr.w);
                }
            }

            Send(new PlayerStatePacket
            {
                Position = new Vec3(t.position.x, t.position.y, t.position.z),
                Rotation = new Quat(rot.x, rot.y, rot.z, rot.w),
                Flags = flags,
                Health = health,
                Food = food,
                Water = water,
                Held = _heldCache,
                SubId = subId,
                LocalPosition = localPos,
                LocalRotation = localRot,
                Gear = _gearCache,
                Anim = _animCache,
            });
        }

        // ---------- receiving ----------

        void Handle(Packet packet)
        {
            switch (packet)
            {
                case WelcomePacket welcome:
                    foreach (var p in welcome.Players) AddRemote(p.Id, p.Name, p.Color);
                    _hostId = welcome.HostId;
                    _worldId = welcome.World.WorldId;
                    _slotRecorded = false;
                    _gameMode = welcome.World.GameMode;
                    WorldStarted = welcome.World.Started;
                    World.OnWelcome(welcome.World);
                    Vehicles.OnWelcome(welcome.World);
                    Structures.OnWelcome(welcome.World);
                    Containers.OnWelcome(welcome.World);
                    Items.OnWelcome(welcome.World);
                    Story.OnWelcome(welcome.World);
                    Creatures.OnWelcome(welcome.World);
                    Power.OnWelcome(welcome.World);
                    Sleep.Reset();
                    BaseLife.Reset();
                    Ghosts.Reset();
                    AddChat($"Connected! {welcome.Players.Count} other player(s) here. {_gameMode} world.");
                    if (_autoStart && !Game.InWorld)
                    {
                        // a world nobody has started yet: wait together in the party lobby, in the menu
                        if (WorldStarted || !_menuUi.OpenLobby()) StartCoroutine(AutoStart());
                        else _menuLobby = true;
                    }
                    _autoStart = false;
                    if (_lastAddress != null) SafeRun("server list", () => ServerList.Remember(_lastAddress, null, _lastPassword));
                    break;

                case HostPacket host:
                    _hostId = host.HostId;
                    if (Joined) AddChat($"{NameOf(_hostId)} is the host now");
                    break;

                case StartGamePacket _:
                    WorldStarted = true;
                    FreshStart = true;
                    AddChat($"{HostName} started the game!");
                    if (_menuLobby && !Game.InWorld) StartCoroutine(AutoStart());
                    _menuLobby = false;
                    break;

                case PlayerJoinedPacket joined:
                    AddRemote(joined.Id, joined.Name, joined.Color);
                    AddChat($"{joined.Name} joined");
                    break;

                case PlayerLeftPacket left:
                    AddChat($"{NameOf(left.Id)} left");
                    RemoveRemote(left.Id);
                    Ghosts.Remove(left.Id);
                    break;

                case PlayerStatePacket state:
                    if (_remotes.TryGetValue(state.Id, out var remote)) remote.SetTarget(state);
                    break;

                case ChatPacket chat:
                    AddChat(chat.SenderId == 0 ? chat.Text : $"{NameOf(chat.SenderId)}: {chat.Text}");
                    break;

                case RejectedPacket rejected:
                    AddChat("Server said no: " + rejected.Reason);
                    if (rejected.Reason.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0)
                        AskForPassword(_lastAddress, rejected.Reason);
                    break;

                case UnlockPacket unlock: World.OnUnlock(unlock); break;
                case EntityRemovedPacket removed:
                    Items.OnPickedUp(removed.EntityId);
                    World.OnEntityRemoved(removed.EntityId);
                    break;
                case ItemDroppedPacket dropped: Items.OnDropped(dropped); break;
                case VehicleSnapshotPacket vsnap: Vehicles.OnSnapshot(vsnap); break;
                case VehicleDockPacket vdock: Vehicles.OnDock(vdock); break;
                case CyclopsStatePacket cyc: Vehicles.OnCyclops(cyc); break;
                case StoryGoalPacket goal: Story.OnGoal(goal); break;
                case AuroraPacket aurora: Story.OnAurora(aurora); break;
                case SpawnSlotsPacket slots: Creatures.OnSlots(slots); break;
                case CreatureOwnerPacket co: Creatures.OnOwner(co); break;
                case CreatureStatesPacket cs: Creatures.OnStates(cs); break;
                case CreatureDamagePacket cd: Creatures.OnDamage(cd); break;
                case CreatureDiedPacket cdied: Creatures.OnDied(cdied.Id); break;
                case SleepPacket sleep: Sleep.OnSleep(sleep); break;
                case PowerPacket power: Power.OnPower(power); break;
                case CraftPacket craft: BaseLife.OnCraft(craft); break;
                case FiresPacket fires: BaseLife.OnFires(fires); break;
                case FireDousePacket douse: BaseLife.OnDouse(douse); break;
                case HullHealthPacket hull: BaseLife.OnHull(hull); break;
                case PickedPacket picked: BaseLife.OnPicked(picked); break;
                case PlayerProfilePacket profile: OnProfile(profile); break;
                case ModFixPacket fix:
                    if (NautilusCompat.ApplyFix(fix.TechTypes))
                        AddChat("Your Nautilus item numbers were fixed to match this world. Quit and restart Subnautica (don't save first), then join again.");
                    break;
                case BuildGhostPacket ghost: Ghosts.OnGhost(ghost); break;
                case EmotePacket emote: Emoting.OnEmote(emote); break;
                case PartyPacket party: Emoting.OnParty(party); break;
                case IntroPacket intro: if (intro.Go) Lobby.IntroGo = true; break;
                case DoorPacket door: Items.OnDoor(door); break;
                case PlayerDiedPacket died:
                    AddChat($"{NameOf(died.Id)} died!");
                    SafeRun("death beacon", () => DeathBeacon.Place(NameOf(died.Id), new Vector3(died.Position.X, died.Position.Y, died.Position.Z)));
                    break;
                case TimeSyncPacket time: World.OnTime(time.TimePassed); break;
                case VehicleSpawnedPacket vs: Vehicles.OnSpawned(vs.Vehicle); break;
                case VehicleStatePacket vst: Vehicles.OnState(vst); break;
                case VehicleOwnerPacket vo: Vehicles.OnOwner(vo.Id, vo.OwnerId); break;
                case VehicleRemovedPacket vr: Vehicles.OnRemoved(vr.Id); break;
                case StructurePacket st: Structures.OnStructure(st); break;
                case ContainerPacket ct: Containers.OnContainer(ct); break;
                case FragmentPacket fr: World.OnFragment(fr); break;
            }
        }

        void WatchConnection()
        {
            var state = _client.State;
            if (state == _lastState) return;
            if (state == ClientState.Disconnected && _lastState != ClientState.Disconnected)
            {
                if (_menuLobby && !Game.InWorld) _menuUi.Open();
                _menuLobby = false;
                AddChat("Disconnected: " + _client.LastError);
                ClearRemotes();
                World.Reset();
                Vehicles.Reset();
                Structures.Reset();
                Containers.Reset();
                Items.Reset();
                Story.Reset();
                Creatures.Reset();
                Sleep.Reset();
                Power.Reset();
                BaseLife.Reset();
                Ghosts.Reset();
                Emoting.Reset();
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
                AddChat("No save for this world on this PC (remember to save before quitting!). Starting fresh; bases, unlocks and story still come from the server.");
            }

            AddChat($"Starting a new {_gameMode} game...");
            bool started = false;
            SafeRun("new game", () => started = Game.StartNewGame(_gameMode, this));
            _newGameStarted = started;
            _modeApplied = false;
            if (!started) AddChat("Couldn't start the game by itself. Click Play > New Game.");
        }

        bool _menuLobby; // waiting in the party lobby on the main menu
        float _optionsSyncAt;

        static readonly List<(float at, Action action)> _later = new List<(float, Action)>();

        // Runs something a bit later on the main thread (works before the session exists too).
        public static void RunLater(float seconds, Action action) => _later.Add((Time.realtimeSinceStartup + seconds, action));

        void RunDue()
        {
            for (int i = _later.Count - 1; i >= 0; i--)
            {
                if (Time.realtimeSinceStartup < _later[i].at) continue;
                var a = _later[i].action;
                _later.RemoveAt(i);
                SafeRun("later", a);
            }
        }

        public bool InMenuLobby => _menuLobby && Joined && !Game.InWorld;
        internal string JoinCodeText => _joinCode;
        internal string GameModeName => _gameMode;
        internal int HostPlayerId => _hostId;
        internal System.Collections.Generic.IEnumerable<RemotePlayer> RemotePlayers => _remotes.Values;
        internal void StartFromLobby() => StartForEveryone();
        internal void LeaveServer() { Leave(); _menuLobby = false; }

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
            Connect(host, port, ServerList.Find(address)?.Password);
        }

        // Host a world from the main menu: server runs inside this game, then we join it like everyone else.
        public void HostFromMenu(string worldName, string gameMode)
        {
            if (_client.State != ClientState.Disconnected) return;
            StopHosting();
            var path = Path.Combine(Path.Combine(Plugin.Folder, "worlds"), SafeFileName(worldName.Trim()) + ".dat");
            var server = new NetServer(path, gameMode) { Password = Plugin.HostPassword.Value ?? "" };
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

        string _lastPassword;

        void Connect(string host, int port, string password = null)
        {
            _lastPassword = string.IsNullOrEmpty(password) ? null : password;
            AddChat($"Joining {host}:{port}...");
            _client.Connect(host, port, Plugin.PlayerName.Value, password: password ?? "", color: Plugin.DiverColor.Value,
                mods: NautilusCompat.ContentMods(), techTypes: NautilusCompat.ModdedTechTypes());
        }

        void JoinFromUi()
        {
            if (!JoinCode.TryParseAddress(Plugin.ServerAddress.Value, Plugin.Port.Value, out var host, out var port))
            {
                AddChat("That doesn't look like a join code or IP");
                return;
            }
            Connect(host, port, ServerList.Find(Plugin.ServerAddress.Value)?.Password);
        }

        void Host()
        {
            if (_hostedServer == null)
            {
                var worldFile = Path.Combine(Path.Combine(Plugin.Folder, "worlds"), SafeFileName(Game.CurrentSaveSlot()) + ".dat");
                // hosting from inside a save you're already playing: no lobby
                var server = new NetServer(worldFile, Game.CurrentGameMode(), started: true) { Password = Plugin.HostPassword.Value ?? "" };
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
                Plugin.Log.LogInfo("Router details: " + r.Report());
                string ip = r.PublicIp ?? (r.Success ? r.ExternalIp : null);
                string msg;
                if (r.Success && !r.BehindCgnat && !r.DoubleNat)
                    msg = "Router opened! Checking if friends can reach you...";
                else if (r.BehindCgnat)
                    msg = "Your internet provider shares your IP (CGNAT): nobody can connect in. Use Radmin VPN / Tailscale, or let a friend host.";
                else if (r.DoubleNat)
                    msg = $"Router opened the port, but your ISP modem ({r.ExternalIp}) sits in front of it. Forward TCP {port} on the modem too, or use Radmin VPN.";
                else
                    msg = "Couldn't auto-open your router (" + r.Error + "). Forward TCP port " + port + " by hand, or use Radmin VPN / Tailscale.";

                if (System.Net.IPAddress.TryParse(ip ?? "", out var addr) && addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    _joinCode = JoinCode.Encode(addr, port);
                _pendingChat.Enqueue(msg);
                if (_joinCode != null) _pendingChat.Enqueue("Join code: " + _joinCode + " (F8 to see it again)");

                var reach = Upnp.CheckReachable(port);
                if (reach == true) _pendingChat.Enqueue("Friends on other wifi CAN reach you!");
                else if (reach == false)
                    _pendingChat.Enqueue("Port is closed from the internet. Probably Windows Firewall: in the launcher's Server tab hit 'Allow through firewall'.");
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
            Structures.Reset();
            Containers.Reset();
            Items.Reset();
            Story.Reset();
            Creatures.Reset();
            Sleep.Reset();
            Power.Reset();
            BaseLife.Reset();
            Ghosts.Reset();
            Emoting.Reset();
        }

        static string SafeFileName(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }

        // ---------- remote players ----------

        readonly Dictionary<int, int> _colors = new Dictionary<int, int>();

        void AddRemote(int id, string name, int color = DiverColors.Default)
        {
            _names[id] = name;
            _colors[id] = color;
            if (id == _client.LocalId || _remotes.ContainsKey(id)) return;
            _remotes[id] = RemotePlayer.Create(id, name, color);
        }

        public int ColorOf(int id) => _colors.TryGetValue(id, out var c) ? c : DiverColors.Default;

        // Name / suit color changed (in the party lobby).
        public void SendProfile(string name, int color)
        {
            Plugin.PlayerName.Value = Protocol.CleanName(name);
            Plugin.DiverColor.Value = color;
            if (Joined) Send(new PlayerProfilePacket { Name = Plugin.PlayerName.Value, Color = color });
        }

        void OnProfile(PlayerProfilePacket p)
        {
            _names[p.Id] = p.Name;
            _colors[p.Id] = p.Color;
            if (p.Id == _client.LocalId) Plugin.PlayerName.Value = p.Name; // the server may have added a number
            else if (_remotes.TryGetValue(p.Id, out var r) && r != null) r.SetProfile(p.Name, p.Color);
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
