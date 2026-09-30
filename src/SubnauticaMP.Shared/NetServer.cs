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
        }

        const double TimeSyncSeconds = 5;
        const double SaveSeconds = 30;

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
            LoadWorld();

            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _clock.Restart();
            _tick = new Timer(_ => Tick(), null, 1000, 1000);

            var thread = new Thread(AcceptLoop) { IsBackground = true, Name = "SubnauticaMP accept" };
            thread.Start();
            Log?.Invoke("Server listening on port " + Port);
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
        double _lastTimeSync, _lastSave;

        void Tick()
        {
            double up = _uptime.Elapsed.TotalSeconds;
            bool hasTime;
            double worldTime;
            lock (_lock) { hasTime = _world.HasTime; worldTime = CurrentTime(); }

            if (hasTime && up - _lastTimeSync >= TimeSyncSeconds)
            {
                _lastTimeSync = up;
                Broadcast(new TimeSyncPacket { TimePassed = worldTime }, except: null);
            }

            if (up - _lastSave >= SaveSeconds)
            {
                _lastSave = up;
                if (_dirty) SaveWorld();
            }
        }

        void LoadWorld()
        {
            if (_savePath == null || !File.Exists(_savePath)) return;
            try
            {
                var world = WorldState.LoadFromFile(_savePath);
                lock (_lock)
                {
                    _world = world;
                    foreach (var v in _world.Vehicles.Values) v.OwnerId = 0;
                    _timeAtClockStart = world.TimePassed;
                }
                Log?.Invoke($"Loaded world: {world.Blueprints.Count} blueprints, {world.Vehicles.Count} vehicles, {world.RemovedEntities.Count} items taken");
            }
            catch (Exception e)
            {
                Log?.Invoke("Couldn't load world file, starting fresh: " + e.Message);
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

        void OnPacket(Connection conn, Packet packet)
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
                    Broadcast(state, except: conn);
                    break;

                case ChatPacket chat:
                    var text = (chat.Text ?? "").Trim();
                    if (text.Length == 0) return;
                    if (text.Length > Protocol.MaxChatLength) text = text.Substring(0, Protocol.MaxChatLength);
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
                        if (structure.Data == null || structure.Data.Length == 0) _world.Structures.Remove(structure.Id);
                        else _world.Structures[structure.Id] = structure.Data;
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
            bool hostChanged;
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
            }

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
