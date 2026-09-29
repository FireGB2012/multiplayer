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
            public bool Joined;
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

        public event Action<string> Log;
        public event Action PlayersChanged;
        public int Port { get; private set; }
        public bool Running => _listener != null;

        // savePath = null keeps the world in memory only.
        public NetServer(string savePath = null)
        {
            _savePath = savePath;
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
                        .Select(c => new PlayerInfo { Id = c.Id, Name = c.Name }).ToList();
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
                SnapshotWorld().SaveToFile(_savePath);
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

                var conn = new Connection(tcp) { Tag = new Client(), MaxPacketSize = Protocol.MaxClientPacketSize };
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
                    lock (_lock) isNew = _world.RemovedEntities.Add(removed.EntityId);
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

        void HandleHello(Connection conn, Client client, HelloPacket hello)
        {
            if (hello.ProtocolVersion != Protocol.Version)
            {
                Reject(conn, $"Version mismatch (server {Protocol.Version}, you {hello.ProtocolVersion}). Update the mod.");
                return;
            }

            var welcome = new WelcomePacket();
            lock (_lock)
            {
                if (_connections.Count(c => ((Client)c.Tag).Joined) >= Protocol.MaxPlayers)
                {
                    Reject(conn, "Server is full");
                    return;
                }

                client.Id = _nextId++;
                client.Name = Protocol.CleanName(hello.Name);
                foreach (var other in _connections)
                {
                    var oc = (Client)other.Tag;
                    if (oc.Joined) welcome.Players.Add(new PlayerInfo { Id = oc.Id, Name = oc.Name });
                }
                welcome.YourId = client.Id;
                welcome.World = _world.Clone();
                welcome.World.TimePassed = CurrentTime();
                client.Joined = true;
            }

            conn.Send(welcome);
            Broadcast(new PlayerJoinedPacket { Id = client.Id, Name = client.Name }, except: conn);
            Log?.Invoke($"{client.Name} joined (id {client.Id})");
            PlayersChanged?.Invoke();
        }

        static void Reject(Connection conn, string reason)
        {
            conn.Send(new RejectedPacket { Reason = reason });
            conn.Close("Rejected: " + reason);
        }

        void OnClosed(Connection conn, string reason)
        {
            var client = (Client)conn.Tag;
            var released = new List<string>();
            lock (_lock)
            {
                _connections.Remove(conn);
                if (!client.Joined) return;
                foreach (var v in _world.Vehicles.Values)
                    if (v.OwnerId == client.Id) { v.OwnerId = 0; released.Add(v.Id); }
            }

            foreach (var id in released) Broadcast(new VehicleOwnerPacket { Id = id, OwnerId = 0 }, except: null);
            Broadcast(new PlayerLeftPacket { Id = client.Id }, except: null);
            Log?.Invoke($"{client.Name} left ({reason})");
            PlayersChanged?.Invoke();
        }

        void Broadcast(Packet packet, Connection except)
        {
            Connection[] targets;
            lock (_lock) targets = _connections.Where(c => c != except && ((Client)c.Tag).Joined).ToArray();
            foreach (var c in targets) c.Send(packet);
        }
    }
}
