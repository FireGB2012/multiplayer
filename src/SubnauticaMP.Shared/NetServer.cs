using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace SubnauticaMP.Shared
{
    // The server keeps the shared world (blueprints, removed items, vehicles, clock), hands it
    // to players as they join, relays live changes, and saves it to disk.
    // Runs standalone (SubnauticaMP.Server / the launcher) or inside the game when a player hosts.
    public sealed class NetServer
    {
        sealed class Client
        {
            public int Id;
            public string Name;
            public int Color = DiverColors.Default;
            public bool Joined;
            public bool IsLocal; // playing on the same PC as the server: that's the host
            public string Ip = "";
            public bool Admin; // host, or typed /login with the admin password
            public DateTime NextEmote; // spam guard
            public DateTime NextPush;
            public DateTime KnockedUntil; // lying on the floor from a push: can't be pushed again until up
            public DateTime NextSwing, NextBatHit;
            public string Held = ""; // what's in their hand (from their position updates)
        }

        const double TimeSyncSeconds = 5;
        const double BackupSeconds = 10 * 60;

        readonly object _lock = new object();
        readonly List<Connection> _connections = new List<Connection>();
        readonly string _savePath;
        WorldState _world = new WorldState();
        readonly Stopwatch _clock = new Stopwatch();
        double _timeAtClockStart;
        bool _dirty;
        TcpListener _listener;
        Timer _tick;
        int _nextId = 1;
        int _hostId;
        PartyPacket _party; // the dance party going on right now (not saved)
        const double IntroWaitSeconds = 90;
        readonly HashSet<int> _introWaiting = new HashSet<int>(), _introReady = new HashSet<int>();
        bool _introOpen;
        DateTime _introDeadline;
        readonly Dictionary<string, int> _creatureOwners = new Dictionary<string, int>(); // creature id -> player id (not saved)
        readonly HashSet<int> _sleepers = new HashSet<int>();
        readonly Dictionary<string, (int id, double time)> _powerWriters = new Dictionary<string, (int, double)>();
        const double PowerWriterTimeout = 8;
        const float MaxSleepSkip = 1200f;

        public event Action<string> Log;
        public event Action PlayersChanged; // also fires when the game starts or the host changes
        public int Port { get; private set; }
        public bool Running => _listener != null;
        public int HostId { get { lock (_lock) return _hostId; } }
        public string Password { get; set; } = ""; // empty = anyone can join
        public double AutosaveMinutes { get; set; } = 2;
        public int MaxBackups { get; set; } = 10;
        public string AdminPassword { get { lock (_lock) return _world.AdminPassword; } }
        public bool TrustLocalPlayers { get; set; } = true; // the host's own PC skips the password and can't be banned

        // savePath = null keeps the world in memory only. gameMode/started only apply when the world is brand new;
        // started = true skips the lobby (e.g. hosting from inside a save you're already playing).
        public NetServer(string savePath = null, string gameMode = GameModes.Survival, bool started = false)
        {
            _savePath = savePath;
            _world.GameMode = GameModes.Normalize(gameMode);
            _world.Started = started;
        }

        public int PlayerCount
        {
            get { lock (_lock) return _connections.Count(c => ((Client)c.Tag).Joined); }
        }

        public List<PlayerInfo> Players
        {
            get
            {
                lock (_lock)
                    return _connections.Select(c => (Client)c.Tag).Where(c => c.Joined)
                        .Select(c => new PlayerInfo { Id = c.Id, Name = c.Name, Color = c.Color }).ToList();
            }
        }

        public WorldState SnapshotWorld()
        {
            lock (_lock)
            {
                var copy = _world.Clone();
                copy.TimePassed = CurrentTime();
                return copy;
            }
        }

        public void Start(int port)
        {
            if (Running) throw new InvalidOperationException("Already running");
            var name = _savePath != null ? Path.GetFileNameWithoutExtension(_savePath) : "(not saved)";
            Log?.Invoke($"Loading world '{name}'");
            bool existed = LoadWorld();
            lock (_lock)
            {
                if (string.IsNullOrEmpty(_world.AdminPassword)) { _world.AdminPassword = MakeAdminPassword(); _dirty = true; }
            }
            if (_savePath != null)
            {
                if (existed && Backup()) Log?.Invoke("World backed up");
                SaveWorld();
                Log?.Invoke("World state saved");
            }

            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _clock.Restart();
            _tick = new Timer(_ => Tick(), null, 1000, 1000);

            var thread = new Thread(AcceptLoop) { IsBackground = true, Name = "SubnauticaMP accept" };
            thread.Start();
            Log?.Invoke($"Server is listening on port {Port} TCP");
            Log?.Invoke($"World id {_world.WorldId}, {_world.GameMode} mode, " + (_world.Started ? "in progress" : "waiting in the lobby"));
            Log?.Invoke("Save format: SubnauticaMP world file v" + WorldState.CurrentFileVersion + (_savePath != null ? " (" + _savePath + ")" : ""));
            Log?.Invoke("Server password: " + (string.IsNullOrEmpty(Password) ? "\"None. Public server.\"" : "\"" + Password + "\""));
            Log?.Invoke($"Admin password: \"{AdminPassword}\"  (in game chat: /login {AdminPassword}, then /help)");
            Log?.Invoke(_savePath == null ? "Autosave: DISABLED (no world file)" : $"Autosave: ENABLED ({AutosaveMinutes:0.#} min)");
            Log?.Invoke(_savePath == null || MaxBackups <= 0 ? "Autobackup: DISABLED" : $"Autobackup: ENABLED (every {BackupSeconds / 60:0} min, max backups: {MaxBackups})");
            Log?.Invoke(existed ? "Loaded save" : "New world created");
        }

        public void Stop()
        {
            var listener = _listener;
            if (listener == null) return;
            _listener = null;
            _tick?.Dispose();
            _tick = null;
            try { listener.Stop(); } catch { }

            Connection[] all;
            lock (_lock) all = _connections.ToArray();
            foreach (var c in all) c.Close("Server stopped");
            SaveWorld();
            Log?.Invoke("Server stopped");
        }

        // ---------- world clock + saving ----------

        double CurrentTime() => _timeAtClockStart + _clock.Elapsed.TotalSeconds;

        void SetTime(double time)
        {
            _timeAtClockStart = time;
            _clock.Restart();
            _world.HasTime = true;
        }

        readonly Stopwatch _uptime = Stopwatch.StartNew();
        double _lastTimeSync, _lastSave, _lastBackup;

        void Tick()
        {
            bool introLate;
            lock (_lock) introLate = _introOpen && DateTime.UtcNow > _introDeadline;
            if (introLate) CheckIntro(true);

            double up = _uptime.Elapsed.TotalSeconds;
            try { LagReport(up); } catch (Exception e) { Log?.Invoke("[lag] report failed: " + e.Message); }
            bool hasTime;
            double worldTime;
            lock (_lock) { hasTime = _world.HasTime; worldTime = CurrentTime(); }

            if (hasTime && up - _lastTimeSync >= TimeSyncSeconds)
            {
                _lastTimeSync = up;
                Broadcast(new TimeSyncPacket { TimePassed = worldTime }, except: null);
            }

            if (up - _lastSave >= AutosaveMinutes * 60)
            {
                _lastSave = up;
                if (_dirty) { SaveWorld(); Log?.Invoke("World state saved"); }
            }
            if (up - _lastBackup >= BackupSeconds)
            {
                _lastBackup = up;
                if (up > 1 && Backup()) Log?.Invoke("World backed up");
            }
        }

        static string MakeAdminPassword()
        {
            const string letters = "ABCDEFGHJKLMNPQRSTUVWXYZ";
            var rnd = new Random();
            var chars = new char[12];
            for (int i = 0; i < chars.Length; i++) chars[i] = letters[rnd.Next(letters.Length)];
            return new string(chars);
        }

        public string BackupFolder => _savePath == null ? null
            : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_savePath)) ?? ".", "backups", Path.GetFileNameWithoutExtension(_savePath));

        // Copies the world file into backups\<world>\ and keeps only the newest MaxBackups.
        public bool Backup()
        {
            if (_savePath == null || MaxBackups <= 0 || !File.Exists(_savePath)) return false;
            try
            {
                var dir = BackupFolder;
                Directory.CreateDirectory(dir);
                var name = Path.GetFileNameWithoutExtension(_savePath) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".dat";
                File.Copy(_savePath, Path.Combine(dir, name), true);
                foreach (var old in Directory.GetFiles(dir, "*.dat").OrderByDescending(File.GetLastWriteTimeUtc).Skip(MaxBackups))
                    File.Delete(old);
                return true;
            }
            catch (Exception e)
            {
                Log?.Invoke("Couldn't back up the world: " + e.Message);
                return false;
            }
        }

        bool LoadWorld()
        {
            if (_savePath == null || !File.Exists(_savePath)) return false;
            try
            {
                var world = WorldState.LoadFromFile(_savePath);
                lock (_lock)
                {
                    _world = world;
                    foreach (var v in _world.Vehicles.Values) v.OwnerId = 0;
                    _timeAtClockStart = world.TimePassed;
                }
                Log?.Invoke($"World contents: {world.Blueprints.Count} blueprints, {world.Structures.Count} bases/buildings, {world.Vehicles.Count} vehicles, " +
                            $"{world.Containers.Count} lockers, {world.StoryGoals.Count} story events, {world.RemovedEntities.Count} things picked up");
                return true;
            }
            catch (Exception e)
            {
                Log?.Invoke("Couldn't load world file, starting fresh: " + e.Message);
                return false;
            }
        }

        public void SaveWorld()
        {
            if (_savePath == null) return;
            try
            {
                // written straight from the live world (no copy): big worlds made a lot of garbage every save,
                // and the garbage collector pausing the game was felt as random lag when hosting in-game
                lock (_lock)
                {
                    _world.TimePassed = CurrentTime();
                    _world.SaveToFile(_savePath);
                }
                _dirty = false;
            }
            catch (Exception e)
            {
                Log?.Invoke("Couldn't save world: " + e.Message);
            }
        }

        // ---------- connections ----------

        void AcceptLoop()
        {
            var listener = _listener;
            while (listener != null && _listener == listener)
            {
                TcpClient tcp;
                try { tcp = listener.AcceptTcpClient(); }
                catch { return; } // listener stopped

                bool local = tcp.Client.RemoteEndPoint is IPEndPoint ep && IPAddress.IsLoopback(ep.Address);
                var conn = new Connection(tcp) { MaxPacketSize = Protocol.MaxHelloPacketSize };
                conn.Tag = new Client { IsLocal = local, Ip = local ? "" : conn.RemoteIp };
                conn.PacketReceived += OnPacket;
                conn.Closed += OnClosed;
                lock (_lock) _connections.Add(conn);
                conn.Start();
            }
        }

        // ---------- lag report ----------

        const double LagReportSeconds = 60;
        const int BusyMs = 150;   // a packet waiting longer than this to go out = players notice
        long _statIn;
        int _statHandleMaxMs;
        string _statHandleMaxWhat = "";
        double _lastLagReport, _lastBusyChat = -1e9;

        // Every minute while people play: how much went through and the worst delays, so lag can be pinned on
        // this PC being too busy (big waits here) vs someone's game or internet (small waits here).
        void LagReport(double up)
        {
            if (up - _lastLagReport < LagReportSeconds) return;
            double span = _lastLagReport > 0 ? up - _lastLagReport : up;
            _lastLagReport = up;
            Connection[] all;
            lock (_lock) all = _connections.ToArray();
            int players = all.Count(c => c.Tag is Client cl && cl.Joined);
            long count = Interlocked.Exchange(ref _statIn, 0);
            int handle = Interlocked.Exchange(ref _statHandleMaxMs, 0);
            string handleWhat = _statHandleMaxWhat;
            int wait = 0;
            string waitWho = "";
            foreach (var c in all)
            {
                int w = c.TakeMaxWaitMs();
                if (w > wait) { wait = w; waitWho = c.Tag is Client cl ? cl.Name : "?"; }
            }
            if (players == 0 && count == 0) return;
            Log?.Invoke($"[lag] last {span:0} s: {players} player(s), {count / Math.Max(1, span):0} packets/s in, " +
                        $"slowest handling {handle} ms ({handleWhat}), slowest send {wait} ms (to {waitWho})");
            if (wait >= BusyMs || handle >= 50)
            {
                Log?.Invoke($"[lag] The server waited up to {Math.Max(wait, handle)} ms: this PC is too busy (or {waitWho}'s internet is slow). " +
                            "Everyone sees lag when this happens. Closing other programs on the host PC helps.");
                if (wait >= 250 && up - _lastBusyChat > 300)
                {
                    _lastBusyChat = up;
                    Broadcast(new ChatPacket { SenderId = 0, Text = $"[lag] Server had to wait {wait} ms to send updates (host PC busy or a slow connection)." }, except: null);
                }
            }
        }

        // Writes the lag line right away instead of waiting for the minute (tests).
        public void ReportLagNow()
        {
            double up = _uptime.Elapsed.TotalSeconds;
            _lastLagReport = up - LagReportSeconds;
            LagReport(up);
        }

        void OnPacket(Connection conn, Packet packet)
        {
            long start = Stopwatch.GetTimestamp();
            try { HandlePacket(conn, packet); }
            finally
            {
                Interlocked.Increment(ref _statIn);
                int ms = (int)((Stopwatch.GetTimestamp() - start) * 1000 / Stopwatch.Frequency);
                if (ms > _statHandleMaxMs) { _statHandleMaxMs = ms; _statHandleMaxWhat = packet.Type.ToString(); }
            }
        }

        void HandlePacket(Connection conn, Packet packet)
        {
            var client = (Client)conn.Tag;

            if (!client.Joined)
            {
                if (packet is HelloPacket hello) HandleHello(conn, client, hello);
                else conn.Close("Expected Hello");
                return;
            }

            switch (packet)
            {
                case PlayerStatePacket state:
                    state.Id = client.Id;
                    client.Held = state.Held ?? "";
                    Broadcast(state, except: conn);
                    break;

                case ChatPacket chat:
                    var text = (chat.Text ?? "").Trim();
                    if (text.Length == 0) return;
                    if (text.Length > Protocol.MaxChatLength) text = text.Substring(0, Protocol.MaxChatLength);
                    if (text.StartsWith("/")) { HandleCommand(conn, client, text); return; }
                    Log?.Invoke($"[chat] {client.Name}: {text}");
                    Broadcast(new ChatPacket { SenderId = client.Id, Text = text }, except: null);
                    break;

                case UnlockPacket unlock:
                    if (string.IsNullOrEmpty(unlock.Key)) return;
                    bool isNew;
                    lock (_lock) isNew = _world.SetFor(unlock.Kind).Add(unlock.Key);
                    if (!isNew) return;
                    _dirty = true;
                    Log?.Invoke($"{client.Name} unlocked {unlock.Kind} {unlock.Key}");
                    Broadcast(unlock, except: conn);
                    break;

                case EntityRemovedPacket removed:
                    if (string.IsNullOrEmpty(removed.EntityId)) return;
                    lock (_lock)
                    {
                        _world.DroppedItems.Remove(removed.EntityId); // someone picked up a dropped item
                        isNew = _world.RemovedEntities.Add(removed.EntityId);
                    }
                    if (!isNew) return;
                    _dirty = true;
                    Broadcast(removed, except: conn);
                    break;

                case VehicleSpawnedPacket spawned:
                    var info = spawned.Vehicle;
                    if (string.IsNullOrEmpty(info.Id) || string.IsNullOrEmpty(info.TechType)) return;
                    lock (_lock)
                    {
                        if (_world.Vehicles.ContainsKey(info.Id)) return;
                        info.OwnerId = client.Id;
                        _world.Vehicles[info.Id] = info;
                    }
                    _dirty = true;
                    Log?.Invoke($"{client.Name} made a {info.TechType}");
                    Broadcast(spawned, except: conn);
                    break;

                case VehicleStatePacket vstate:
                    lock (_lock)
                    {
                        // only the current owner gets to move it
                        if (!_world.Vehicles.TryGetValue(vstate.Id ?? "", out var v) || v.OwnerId != client.Id) return;
                        v.Position = vstate.Position;
                        v.Rotation = vstate.Rotation;
                        if (vstate.Health >= 0) v.Health = vstate.Health;
                        if (vstate.Energy >= 0) v.Energy = vstate.Energy;
                    }
                    _dirty = true;
                    Broadcast(vstate, except: conn);
                    break;

                case VehicleOwnerPacket claim:
                    lock (_lock)
                    {
                        if (!_world.Vehicles.TryGetValue(claim.Id ?? "", out var v) || v.OwnerId == client.Id) return;
                        v.OwnerId = client.Id;
                    }
                    Broadcast(new VehicleOwnerPacket { Id = claim.Id, OwnerId = client.Id }, except: null);
                    break;

                case VehicleRemovedPacket vremoved:
                    lock (_lock)
                    {
                        if (!_world.Vehicles.Remove(vremoved.Id ?? "")) return;
                    }
                    _dirty = true;
                    Log?.Invoke($"{client.Name}'s vehicle {vremoved.Id} was destroyed");
                    Broadcast(vremoved, except: conn);
                    break;

                case StructurePacket structure:
                    if (string.IsNullOrEmpty(structure.Id)) return;
                    lock (_lock)
                    {
                        if (structure.Data == null || structure.Data.Length == 0)
                        {
                            _world.Structures.Remove(structure.Id);
                            _world.StructurePositions.Remove(structure.Id);
                        }
                        else
                        {
                            _world.Structures[structure.Id] = structure.Data;
                            if (structure.HasPosition) _world.StructurePositions[structure.Id] = structure.Position;
                        }
                    }
                    _dirty = true;
                    Broadcast(structure, except: conn);
                    break;

                case ContainerPacket container:
                    if (string.IsNullOrEmpty(container.Id)) return;
                    lock (_lock) _world.Containers[container.Id] = container.Items;
                    _dirty = true;
                    Broadcast(container, except: conn);
                    break;

                case FragmentPacket fragment:
                    if (string.IsNullOrEmpty(fragment.TechType) || fragment.Unlocked <= 0) return;
                    lock (_lock)
                    {
                        // progress only goes up
                        if (_world.Fragments.TryGetValue(fragment.TechType, out var had) && had >= fragment.Unlocked) return;
                        _world.Fragments[fragment.TechType] = fragment.Unlocked;
                    }
                    _dirty = true;
                    Broadcast(fragment, except: conn);
                    break;

                case ItemDroppedPacket dropped:
                    if (string.IsNullOrEmpty(dropped.Id) || dropped.Data == null || dropped.Data.Length == 0) return;
                    lock (_lock)
                    {
                        _world.RemovedEntities.Remove(dropped.Id); // it's back in the world
                        _world.DroppedItems[dropped.Id] = dropped.Data;
                    }
                    _dirty = true;
                    Broadcast(dropped, except: conn);
                    break;

                case DoorPacket door:
                    if (string.IsNullOrEmpty(door.Id)) return;
                    lock (_lock) _world.Doors[door.Id] = door.Open;
                    _dirty = true;
                    Broadcast(door, except: conn);
                    break;

                case VehicleSnapshotPacket snap:
                    lock (_lock)
                    {
                        // whoever drove it last (or nobody) may update it
                        if (!_world.Vehicles.TryGetValue(snap.Id ?? "", out var v) || (v.OwnerId != client.Id && v.OwnerId != 0)) return;
                        v.Snapshot = snap.Data ?? new byte[0];
                    }
                    _dirty = true;
                    Broadcast(snap, except: conn);
                    break;

                case VehicleDockPacket dock:
                    lock (_lock)
                    {
                        if (!_world.Vehicles.TryGetValue(dock.Id ?? "", out var v)) return;
                        v.Docked = dock.Docked;
                        v.DockPosition = dock.DockPosition;
                    }
                    _dirty = true;
                    Broadcast(dock, except: conn);
                    break;

                case CyclopsStatePacket cyc:
                    if (string.IsNullOrEmpty(cyc.Id)) return;
                    lock (_lock) _world.Cyclopses[cyc.Id] = cyc;
                    _dirty = true;
                    Broadcast(cyc, except: conn);
                    break;

                case StoryGoalPacket goal:
                    if (string.IsNullOrEmpty(goal.Key)) return;
                    lock (_lock)
                    {
                        if (_world.StoryGoals.Exists(g => g.Key == goal.Key)) return; // already happened
                        _world.StoryGoals.Add(goal);
                    }
                    _dirty = true;
                    Log?.Invoke($"Story: {goal.Key}");
                    Broadcast(goal, except: conn);
                    break;

                case AuroraPacket aurora:
                    lock (_lock)
                    {
                        if (_world.Aurora != null) return; // first one wins, like the clock
                        _world.Aurora = aurora;
                    }
                    _dirty = true;
                    Broadcast(aurora, except: conn);
                    break;

                case SpawnSlotsPacket slots:
                    var fresh = new SpawnSlotsPacket();
                    lock (_lock)
                    {
                        foreach (var slot in slots.Slots)
                        {
                            if (string.IsNullOrEmpty(slot.Key) || _world.SpawnBook.ContainsKey(slot.Key)) continue; // first roll wins
                            _world.SpawnBook[slot.Key] = slot;
                            fresh.Slots.Add(slot);
                        }
                    }
                    if (fresh.Slots.Count == 0) return;
                    _dirty = true;
                    Broadcast(fresh, except: conn);
                    break;

                case CreatureOwnerPacket owner:
                    HandleCreatureOwner(client, owner);
                    break;

                case CreatureStatesPacket states:
                    var mine = new CreatureStatesPacket();
                    lock (_lock)
                        foreach (var st in states.States)
                            if (_creatureOwners.TryGetValue(st.Id ?? "", out var o) && o == client.Id) mine.States.Add(st);
                    if (mine.States.Count > 0) Broadcast(mine, except: conn);
                    break;

                case CreatureDamagePacket damage:
                    Connection ownerConn = null;
                    lock (_lock)
                    {
                        if (_creatureOwners.TryGetValue(damage.Id ?? "", out var o) && o != client.Id)
                            ownerConn = _connections.FirstOrDefault(c => ((Client)c.Tag).Joined && ((Client)c.Tag).Id == o);
                    }
                    ownerConn?.Send(damage);
                    break;

                case CreatureDiedPacket dead:
                    if (string.IsNullOrEmpty(dead.Id)) return;
                    lock (_lock)
                    {
                        _creatureOwners.Remove(dead.Id);
                        if (!_world.RemovedEntities.Add(dead.Id)) return; // already dead
                    }
                    _dirty = true;
                    Broadcast(dead, except: conn);
                    break;

                case KickPacket kick:
                    bool isHost;
                    lock (_lock) isHost = client.Id == _hostId;
                    if (isHost && kick.TargetId != client.Id) Kick(kick.TargetId, kick.Ban);
                    break;

                case SleepPacket sleep:
                    HandleSleep(client, sleep.Asleep, sleep.Amount);
                    break;

                case PowerPacket power:
                    HandlePower(conn, client, power);
                    break;

                case PlayerProfilePacket profile:
                    string newName;
                    lock (_lock)
                    {
                        newName = UniqueName(Protocol.CleanName(profile.Name), client);
                        client.Name = newName;
                        client.Color = profile.Color & 0xFFFFFF;
                    }
                    Broadcast(new PlayerProfilePacket { Id = client.Id, Name = newName, Color = client.Color }, except: null);
                    PlayersChanged?.Invoke();
                    break;

                case EmotePacket emote:
                    RelayEmote(conn, client, emote.Emote, emote.StartTime);
                    break;

                case PartyPacket party:
                    HandleParty(client, party);
                    break;

                case PushPacket push:
                {
                    bool ok;
                    var now = DateTime.UtcNow;
                    lock (_lock)
                    {
                        var target = _connections.Select(c => c.Tag as Client).FirstOrDefault(t => t != null && t.Joined && t.Id == push.TargetId);
                        ok = push.TargetId != client.Id && now >= client.NextPush && target != null && now >= target.KnockedUntil;
                        if (ok)
                        {
                            client.NextPush = now.AddMilliseconds(700);
                            target.KnockedUntil = now.AddSeconds(Emotes.KnockedSeconds + 0.5); // + getting back up
                        }
                    }
                    if (!ok) break;
                    var d = push.Direction;
                    float len = (float)Math.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z);
                    if (len < 1e-3f || float.IsNaN(len)) break;
                    Broadcast(new PushPacket { PusherId = client.Id, TargetId = push.TargetId, Direction = new Vec3(d.X / len, d.Y / len, d.Z / len) }, except: null);
                    break;
                }

                case BatSwingPacket swing:
                {
                    var now = DateTime.UtcNow;
                    lock (_lock)
                    {
                        if (now < client.NextSwing) break;
                        client.NextSwing = now.AddMilliseconds(250);
                    }
                    float pitch = float.IsNaN(swing.Pitch) ? 0f : Math.Max(-90f, Math.Min(90f, swing.Pitch));
                    Broadcast(new BatSwingPacket { Id = client.Id, Pitch = pitch }, except: conn);
                    break;
                }

                case BatHitPacket hit:
                    HandleBatHit(client, hit);
                    break;

                case IntroPacket _:
                    bool open;
                    lock (_lock)
                    {
                        open = _introOpen;
                        if (open) _introReady.Add(client.Id);
                    }
                    if (!open) conn.Send(new IntroPacket { Go = true }); // nobody to wait for
                    else CheckIntro(false);
                    break;

                case CraftPacket _:
                case FiresPacket _:
                case FireDousePacket _:
                case HullHealthPacket _:
                case PickedPacket _:
                    Broadcast(packet, except: conn);
                    break;

                case BuildGhostPacket ghost:
                    ghost.Id = client.Id;
                    Broadcast(ghost, except: conn);
                    break;

                case PlayerDiedPacket died:
                    died.Id = client.Id;
                    Log?.Invoke($"{client.Name} died");
                    Broadcast(died, except: conn);
                    break;

                case StartGamePacket _:
                    lock (_lock)
                    {
                        if (client.Id != _hostId || _world.Started) return;
                        _world.Started = true;
                        // everyone here now loads in; the intro waits for all of them
                        _introWaiting.Clear();
                        _introReady.Clear();
                        foreach (var c in _connections) if (c.Tag is Client cl && cl.Joined) _introWaiting.Add(cl.Id);
                        _introOpen = true;
                        _introDeadline = DateTime.UtcNow.AddSeconds(IntroWaitSeconds);
                    }
                    _dirty = true;
                    Log?.Invoke($"{client.Name} started the game!");
                    Broadcast(new StartGamePacket(), except: null);
                    PlayersChanged?.Invoke();
                    break;

                case TimeSyncPacket time:
                    // first player into a brand-new world sets the clock (usually the host's save)
                    lock (_lock)
                    {
                        if (_world.HasTime || time.TimePassed < 0 || double.IsNaN(time.TimePassed)) return;
                        SetTime(time.TimePassed);
                    }
                    _dirty = true;
                    Log?.Invoke($"World clock set by {client.Name}");
                    Broadcast(new TimeSyncPacket { TimePassed = time.TimePassed }, except: conn);
                    break;
            }
        }

        // ---------- admin commands (typed in chat) ----------

        // Emotes go to everyone else (the sender plays their own). Max ~4 a second each, so nobody can flood.
        void RelayEmote(Connection conn, Client client, Emote emote, double startTime = double.NaN)
        {
            if (!Emotes.IsValid(emote)) return;
            if (double.IsInfinity(startTime) || startTime < 0) startTime = double.NaN;
            var now = DateTime.UtcNow;
            lock (_lock)
            {
                if (emote != Emote.None && now < client.NextEmote) return;
                client.NextEmote = now.AddMilliseconds(250);
            }
            Broadcast(new EmotePacket { Id = client.Id, Emote = emote, StartTime = startTime }, except: conn);
        }

        // Everyone who was here at the start has loaded (or waited long enough): roll the intro.
        void CheckIntro(bool timeUp)
        {
            lock (_lock)
            {
                if (!_introOpen) return;
                var connected = new HashSet<int>(_connections.Select(c => c.Tag).OfType<Client>().Where(c => c.Joined).Select(c => c.Id));
                _introWaiting.IntersectWith(connected);
                bool all = _introWaiting.All(_introReady.Contains);
                if (!all && !(timeUp && _introReady.Count > 0)) return;
                _introOpen = false;
            }
            Log?.Invoke("Everyone has loaded: intro rolling");
            Broadcast(new IntroPacket { Go = true }, except: null);
        }

        // Start (or restart) the dance party, or end it (only whoever started it, or an admin, can end it).
        // A Titanium Bat swing hit some players: launch the ones that can be (joined, not already flying / on
        // the floor), if the hitter really has the bat out and isn't swinging faster than a bat can.
        void HandleBatHit(Client client, BatHitPacket hit)
        {
            var d = hit.Direction;
            float len = (float)Math.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z);
            if (len < 1e-3f || float.IsNaN(len) || hit.Targets == null) return;
            var now = DateTime.UtcNow;
            var targets = new List<int>();
            lock (_lock)
            {
                if (client.Held != Bat.TechName || now < client.NextBatHit) return;
                foreach (var id in hit.Targets.Distinct())
                {
                    if (id == client.Id || targets.Count >= Bat.MaxTargets) continue;
                    var target = _connections.Select(c => c.Tag as Client).FirstOrDefault(t => t != null && t.Joined && t.Id == id);
                    if (target == null || now < target.KnockedUntil) continue;
                    target.KnockedUntil = now.AddSeconds(Bat.LaunchedSeconds + 0.5);
                    targets.Add(id);
                }
                if (targets.Count == 0) return;
                client.NextBatHit = now.AddMilliseconds(Bat.SwingCooldownMs);
            }
            Broadcast(new BatHitPacket { HitterId = client.Id, Targets = targets, Direction = new Vec3(d.X / len, d.Y / len, d.Z / len) }, except: null);
        }

        void HandleParty(Client client, PartyPacket p)
        {
            PartyPacket now;
            lock (_lock)
            {
                if (p.Active)
                    _party = new PartyPacket { LeaderId = client.Id, Active = true, Center = p.Center, StartTime = p.StartTime, Seed = p.Seed };
                else
                {
                    if (_party == null || (_party.LeaderId != client.Id && !client.Admin && client.Id != _hostId)) return;
                    _party = null;
                }
                now = _party ?? new PartyPacket { LeaderId = client.Id, Active = false };
            }
            Log?.Invoke(p.Active ? $"{client.Name} started a dance party" : $"{client.Name} ended the dance party");
            Broadcast(now, except: null);
        }

        void HandleCommand(Connection conn, Client client, string text)
        {
            void Reply(string msg) => conn.Send(new ChatPacket { SenderId = 0, Text = msg });
            // the game plays "/e wave" itself; this is just in case one slips through
            if (Emotes.TryParseCommand(text, out var emote, out var listOnly))
            {
                if (listOnly) Reply("Emotes: " + Emotes.List() + ". Type /e <emote> or press G in game.");
                else RelayEmote(conn, client, emote);
                return;
            }
            var parts = text.Substring(1).Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            var cmd = parts.Length > 0 ? parts[0].ToLowerInvariant() : "";
            var arg = parts.Length > 1 ? parts[1].Trim() : "";
            bool admin;
            lock (_lock) admin = client.Admin || client.Id == _hostId;

            if (cmd == "login")
            {
                bool ok;
                lock (_lock) ok = arg.Length > 0 && arg == _world.AdminPassword;
                if (ok) { client.Admin = true; Reply("You're an admin now. /help for commands."); Log?.Invoke($"{client.Name} logged in as admin"); }
                else Reply("Wrong admin password.");
                return;
            }
            if (cmd == "mods")
            {
                lock (_lock)
                    Reply(!_world.ModsDefined ? "Mods: not set yet (the first player to join sets them)."
                        : _world.Mods.Count == 0 ? "Mods: none, this is a vanilla world."
                        : "Mods everyone needs: " + string.Join(", ", _world.Mods.Select(m => m.ToString()).ToArray()));
                return;
            }
            if (cmd == "players")
            {
                Reply("Online: " + string.Join(", ", Players.Select(p => p.Name).ToArray()));
                return;
            }
            if (cmd == "help" || cmd == "?")
            {
                Reply(admin ? "Commands: /players, /mods, /e <emote>, /kick <name>, /ban <name>, /unban <name>, /bans, /save, /backup, /resetmods"
                            : "Commands: /players, /mods, /e <emote>, /login <admin password>");
                return;
            }
            if (!admin) { Reply("That needs admin. Type /login <admin password> (the host's server window shows it)."); return; }

            switch (cmd)
            {
                case "kick":
                case "ban":
                    var target = Players.FirstOrDefault(p => string.Equals(p.Name, arg, StringComparison.OrdinalIgnoreCase));
                    if (target == null) Reply($"No player called '{arg}' is online.");
                    else if (target.Id == client.Id) Reply("You can't " + cmd + " yourself.");
                    else Kick(target.Id, cmd == "ban");
                    break;
                case "unban":
                    Reply(Unban(arg) ? $"Unbanned {arg}." : $"'{arg}' wasn't banned.");
                    break;
                case "bans":
                    var bans = Bans;
                    Reply(bans.Count == 0 ? "Nobody is banned." : "Banned: " + string.Join(", ", bans.Select(b => b.Name).ToArray()));
                    break;
                case "resetmods":
                    lock (_lock) { _world.ModsDefined = false; _world.Mods.Clear(); _world.TechTypes.Clear(); }
                    _dirty = true;
                    Reply("Mod list cleared: the next player to join sets it.");
                    break;
                case "save":
                    SaveWorld();
                    Reply("World saved.");
                    break;
                case "backup":
                    Reply(Backup() ? "World backed up." : "Couldn't back up (no world file yet?).");
                    break;
                default:
                    Reply("Unknown command. /help lists them.");
                    break;
            }
        }

        // ---------- kick / ban ----------

        public List<BanEntry> Bans { get { lock (_lock) return _world.Bans.Select(b => new BanEntry { Name = b.Name, Ip = b.Ip }).ToList(); } }

        // Kicks a player; ban = they can't come back (by name and IP). Returns false if they're not here.
        public bool Kick(int playerId, bool ban)
        {
            Connection target;
            Client tc;
            lock (_lock)
            {
                target = _connections.FirstOrDefault(c => ((Client)c.Tag).Joined && ((Client)c.Tag).Id == playerId);
                if (target == null) return false;
                tc = (Client)target.Tag;
                if (ban && !_world.Bans.Any(b => string.Equals(b.Name, tc.Name, StringComparison.OrdinalIgnoreCase)))
                    _world.Bans.Add(new BanEntry { Name = tc.Name, Ip = tc.Ip });
            }
            if (ban) _dirty = true;
            Log?.Invoke($"{tc.Name} was {(ban ? "banned" : "kicked")}");
            Broadcast(new ChatPacket { SenderId = 0, Text = $"{tc.Name} was {(ban ? "banned" : "kicked")}." }, except: target);
            Reject(target, ban ? "You were banned from this server." : "You were kicked from this server.");
            return true;
        }

        public bool Unban(string name)
        {
            int removed;
            lock (_lock) removed = _world.Bans.RemoveAll(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));
            if (removed > 0) _dirty = true;
            return removed > 0;
        }

        bool IsBanned(Client c, string name) =>
            !(c.IsLocal && TrustLocalPlayers) && _world.Bans.Any(b =>
                string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(b.Ip) && b.Ip == c.Ip));

        // ---------- sleeping ----------

        void HandleSleep(Client client, bool asleep, float amount)
        {
            bool skip;
            lock (_lock)
            {
                if (asleep) _sleepers.Add(client.Id); else _sleepers.Remove(client.Id);
                skip = asleep && AllAsleep();
                if (skip)
                {
                    SetTime(CurrentTime() + Math.Max(0f, Math.Min(MaxSleepSkip, amount)));
                    _sleepers.Clear();
                }
            }
            Broadcast(new SleepPacket { Id = client.Id, Asleep = asleep }, except: null);
            if (!skip) return;
            _dirty = true;
            Log?.Invoke("Everyone's asleep: skipping the night");
            Broadcast(new SleepPacket { Id = client.Id, Asleep = false, Skip = true, Amount = amount }, except: null);
            double now;
            lock (_lock) now = CurrentTime();
            Broadcast(new TimeSyncPacket { TimePassed = now }, except: null);
        }

        // call inside _lock
        bool AllAsleep()
        {
            var ids = _connections.Select(c => (Client)c.Tag).Where(c => c.Joined).Select(c => c.Id).ToList();
            return ids.Count > 0 && ids.All(_sleepers.Contains);
        }

        // ---------- power ----------

        void HandlePower(Connection conn, Client client, PowerPacket p)
        {
            if (string.IsNullOrEmpty(p.Id) || float.IsNaN(p.Power) || float.IsNaN(p.Drain)) return;
            double now = _uptime.Elapsed.TotalSeconds;
            Connection writerConn = null;
            PowerPacket relay = null;
            lock (_lock)
            {
                _powerWriters.TryGetValue(p.Id, out var writer);
                bool writerAlive = writer.id != 0 && now - writer.time < PowerWriterTimeout &&
                                   _connections.Any(c => ((Client)c.Tag).Joined && ((Client)c.Tag).Id == writer.id);

                if (p.Drain > 0)
                {
                    // someone used power from a source another player runs
                    _world.Power.TryGetValue(p.Id, out var level);
                    _world.Power[p.Id] = Math.Max(0f, level - p.Drain);
                    if (writerAlive && writer.id != client.Id)
                        writerConn = _connections.FirstOrDefault(c => ((Client)c.Tag).Id == writer.id);
                }
                else
                {
                    if (writerAlive && writer.id != client.Id) return; // someone else runs this one
                    _powerWriters[p.Id] = (client.Id, now);
                    _world.Power[p.Id] = Math.Max(0f, p.Power);
                    relay = new PowerPacket { Id = p.Id, Power = p.Power, WriterId = client.Id };
                }
            }
            _dirty = true;
            if (writerConn != null) writerConn.Send(new PowerPacket { Id = p.Id, Drain = p.Drain, WriterId = 0 });
            if (relay != null) Broadcast(relay, except: conn);
        }

        void HandleCreatureOwner(Client client, CreatureOwnerPacket request)
        {
            var granted = new CreatureOwnerPacket { OwnerId = request.OwnerId };
            lock (_lock)
            {
                bool claim = request.OwnerId == client.Id;
                bool handoff = !claim && request.OwnerId != 0;
                if (handoff && !_connections.Any(c => ((Client)c.Tag).Joined && ((Client)c.Tag).Id == request.OwnerId)) return;

                foreach (var id in request.Ids)
                {
                    if (string.IsNullOrEmpty(id)) continue;
                    _creatureOwners.TryGetValue(id, out var current);
                    if (claim)
                    {
                        // first come first served, and dead creatures stay dead
                        if (current != 0 || _world.RemovedEntities.Contains(id)) continue;
                        _creatureOwners[id] = client.Id;
                    }
                    else
                    {
                        if (current != client.Id) continue; // only the owner can let go or hand over
                        if (handoff) _creatureOwners[id] = request.OwnerId;
                        else _creatureOwners.Remove(id);
                    }
                    granted.Ids.Add(id);
                }
            }
            if (granted.Ids.Count > 0) Broadcast(granted, except: null);
        }

        // creature owners grouped by player, for someone who just joined. Call inside _lock.
        List<CreatureOwnerPacket> CreatureOwnerPackets()
        {
            return _creatureOwners.GroupBy(kv => kv.Value)
                .SelectMany(g => g.Select(kv => kv.Key)
                    .Select((id, i) => new { id, i })
                    .GroupBy(x => x.i / CreatureOwnerPacket.MaxIds)
                    .Select(chunk => new CreatureOwnerPacket { OwnerId = g.Key, Ids = chunk.Select(x => x.id).ToList() }))
                .ToList();
        }

        void HandleHello(Connection conn, Client client, HelloPacket hello)
        {
            if (hello.ProtocolVersion != Protocol.Version)
            {
                Reject(conn, $"Version mismatch (server {Protocol.Version}, you {hello.ProtocolVersion}). Update the mod.");
                return;
            }

            var welcome = new WelcomePacket();
            bool hostChanged;
            List<CreatureOwnerPacket> owners;
            string modNote = null, modLog = null;
            lock (_lock)
            {
                var name = Protocol.CleanName(hello.Name);
                if (IsBanned(client, name))
                {
                    Reject(conn, "You're banned from this server.");
                    return;
                }
                if (!(client.IsLocal && TrustLocalPlayers) && !string.IsNullOrEmpty(Password) && hello.Password != Password)
                {
                    Reject(conn, string.IsNullOrEmpty(hello.Password) ? "This server needs a password." : "Wrong password.");
                    return;
                }
                if (_connections.Count(c => ((Client)c.Tag).Joined) >= Protocol.MaxPlayers)
                {
                    Reject(conn, "Server is full");
                    return;
                }
                if (!CheckMods(conn, client, hello, out modNote, out modLog)) return;

                client.Id = _nextId++;
                client.Name = UniqueName(Protocol.CleanName(hello.Name), client);
                client.Color = hello.Color & 0xFFFFFF;
                foreach (var other in _connections)
                {
                    var oc = (Client)other.Tag;
                    if (oc.Joined) welcome.Players.Add(new PlayerInfo { Id = oc.Id, Name = oc.Name, Color = oc.Color });
                }
                welcome.YourId = client.Id;
                welcome.World = _world.Clone();
                welcome.World.Bans.Clear(); // IPs stay on the server
                welcome.World.AdminPassword = "";
                welcome.World.TimePassed = CurrentTime();
                client.Joined = true;
                hostChanged = PickHost();
                welcome.HostId = _hostId;
                owners = CreatureOwnerPackets();
            }

            conn.MaxPacketSize = Protocol.MaxClientPacketSize;
            conn.Send(welcome);
            foreach (var p in owners) conn.Send(p);
            Broadcast(new PlayerJoinedPacket { Id = client.Id, Name = client.Name, Color = client.Color }, except: conn);
            if (hostChanged) Broadcast(new HostPacket { HostId = HostId }, except: conn);
            Log?.Invoke($"{client.Name} joined (id {client.Id})");
            if (modLog != null) Log?.Invoke(modLog);
            if (modNote != null) conn.Send(new ChatPacket { SenderId = 0, Text = modNote });
            PartyPacket party;
            lock (_lock) party = _party;
            if (party != null) conn.Send(party); // a party is already going: late joiners see it too
            PlayersChanged?.Invoke();
        }

        // Two players can't have the same name (kick/ban and chat go by name): add a number. Call inside _lock.
        string UniqueName(string name, Client self)
        {
            var taken = new HashSet<string>(_connections.Select(c => (Client)c.Tag).Where(c => c != self && c.Joined && c.Name != null)
                .Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
            if (!taken.Contains(name)) return name;
            for (int i = 2; ; i++)
            {
                var candidate = name.Length > Protocol.MaxNameLength - 3 ? name.Substring(0, Protocol.MaxNameLength - 3) : name;
                candidate += " " + i;
                if (!taken.Contains(candidate)) return candidate;
            }
        }

        // Content mods (Nautilus / SMLHelper based): the host's mods define the world; everyone else needs the
        // same ones, and the same numbers for the modded items (Nautilus picks them per PC). Call inside _lock.
        bool CheckMods(Connection conn, Client client, HelloPacket hello, out string note, out string log)
        {
            note = log = null;
            bool definer = !_world.ModsDefined || (client.IsLocal && TrustLocalPlayers);
            if (definer)
            {
                bool changed = !_world.ModsDefined || !SameMods(_world.Mods, hello.Mods) || ModInfo.Mismatches(_world.TechTypes, hello.TechTypes).Count > 0 ||
                               hello.TechTypes.Keys.Any(k => !_world.TechTypes.ContainsKey(k));
                _world.ModsDefined = true;
                _world.Mods = hello.Mods.ToList();
                foreach (var kv in hello.TechTypes) _world.TechTypes[kv.Key] = kv.Value;
                if (changed)
                {
                    _dirty = true;
                    log = _world.Mods.Count == 0 ? "World mods: none (vanilla)"
                        : $"World mods ({_world.Mods.Count}): " + string.Join(", ", _world.Mods.Select(m => m.ToString()).ToArray()) + $"; {_world.TechTypes.Count} modded items";
                }
                return true;
            }

            var have = new HashSet<string>(hello.Mods.Select(m => m.Guid), StringComparer.OrdinalIgnoreCase);
            var missing = _world.Mods.Where(m => !have.Contains(m.Guid)).ToList();
            if (missing.Count > 0)
            {
                Reject(conn, "This world uses mods you don't have: " + string.Join(", ", missing.Select(m => m.ToString()).ToArray()) +
                             ". Install them and join again.");
                return false;
            }
            var wrong = ModInfo.Mismatches(_world.TechTypes, hello.TechTypes);
            if (wrong.Count > 0)
            {
                conn.Send(new ModFixPacket { TechTypes = new Dictionary<string, int>(_world.TechTypes) });
                Reject(conn, $"Your modded items are numbered differently from this world ({wrong.Count}, like {wrong[0]}). " +
                             "It's fixed now: restart Subnautica and join again.");
                return false;
            }

            var worldGuids = new HashSet<string>(_world.Mods.Select(m => m.Guid), StringComparer.OrdinalIgnoreCase);
            var extra = hello.Mods.Where(m => !worldGuids.Contains(m.Guid)).ToList();
            var older = _world.Mods.Where(m => hello.Mods.Any(h => string.Equals(h.Guid, m.Guid, StringComparison.OrdinalIgnoreCase) && h.Version != m.Version)).ToList();
            if (extra.Count > 0)
                note = "Heads up: you have mods this world doesn't use (" + string.Join(", ", extra.Select(m => m.ToString()).ToArray()) +
                       "). Things from them won't show up for other players.";
            else if (older.Count > 0)
                note = "Heads up: your version differs from the host's for " + string.Join(", ", older.Select(m => m.ToString()).ToArray()) + ".";
            return true;
        }

        static bool SameMods(List<ModInfo> a, List<ModInfo> b) =>
            a.Count == b.Count && a.All(m => b.Any(x => string.Equals(x.Guid, m.Guid, StringComparison.OrdinalIgnoreCase) && x.Version == m.Version));

        // Host = whoever plays on the server's own PC, otherwise whoever has been here longest. Call inside _lock.
        bool PickHost()
        {
            var joined = _connections.Select(c => (Client)c.Tag).Where(c => c.Joined).OrderBy(c => c.Id).ToList();
            var host = joined.FirstOrDefault(c => c.IsLocal) ?? joined.FirstOrDefault();
            int id = host?.Id ?? 0;
            if (id == _hostId) return false;
            _hostId = id;
            return true;
        }

        static void Reject(Connection conn, string reason)
        {
            conn.Send(new RejectedPacket { Reason = reason });
            conn.CloseAfterSend("Rejected: " + reason);
        }

        void OnClosed(Connection conn, string reason)
        {
            var client = (Client)conn.Tag;
            var released = new List<string>();
            var freedCreatures = new List<string>();
            bool hostChanged, partyOver = false;
            lock (_lock)
            {
                _connections.Remove(conn);
                if (!client.Joined) return;
                foreach (var v in _world.Vehicles.Values)
                    if (v.OwnerId == client.Id) { v.OwnerId = 0; released.Add(v.Id); }
                _sleepers.Remove(client.Id);
                foreach (var id in _creatureOwners.Where(kv => kv.Value == client.Id).Select(kv => kv.Key).ToList())
                {
                    _creatureOwners.Remove(id);
                    freedCreatures.Add(id);
                }
                hostChanged = PickHost();
                if (_party != null && _party.LeaderId == client.Id) { _party = null; partyOver = true; }
            }

            if (partyOver) Broadcast(new PartyPacket { LeaderId = client.Id, Active = false }, except: null);
            CheckIntro(false); // don't keep everyone waiting on someone who left
            for (int i = 0; i < freedCreatures.Count; i += CreatureOwnerPacket.MaxIds)
                Broadcast(new CreatureOwnerPacket { OwnerId = 0, Ids = freedCreatures.Skip(i).Take(CreatureOwnerPacket.MaxIds).ToList() }, except: null);

            foreach (var id in released) Broadcast(new VehicleOwnerPacket { Id = id, OwnerId = 0 }, except: null);
            Broadcast(new PlayerLeftPacket { Id = client.Id }, except: null);
            if (hostChanged) Broadcast(new HostPacket { HostId = HostId }, except: null);
            Log?.Invoke($"{client.Name} left ({reason})");
            PlayersChanged?.Invoke();
        }

        void Broadcast(Packet packet, Connection except)
        {
            Connection[] targets;
            lock (_lock) targets = _connections.Where(c => c != except && ((Client)c.Tag).Joined).ToArray();
            if (targets.Length == 0) return;
            var data = Protocol.Serialize(packet); // once, not once per player
            bool urgent = Protocol.IsUrgent(packet.Type);
            foreach (var c in targets) c.SendRaw(data, urgent);
        }
    }
}
