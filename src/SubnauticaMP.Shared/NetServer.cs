using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace SubnauticaMP.Shared
{
    // Relay server: tracks who is connected and forwards state/chat to everyone else.
    // Can run standalone (SubnauticaMP.Server) or inside the game when a player hosts.
    public sealed class NetServer
    {
        sealed class Client
        {
            public int Id;
            public string Name;
            public bool Joined;
        }

        readonly object _lock = new object();
        readonly List<Connection> _connections = new List<Connection>();
        TcpListener _listener;
        int _nextId = 1;

        public event Action<string> Log;
        public int Port { get; private set; }
        public bool Running => _listener != null;

        public int PlayerCount
        {
            get { lock (_lock) return _connections.Count(c => ((Client)c.Tag).Joined); }
        }

        public void Start(int port)
        {
            if (Running) throw new InvalidOperationException("Already running");
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            var thread = new Thread(AcceptLoop) { IsBackground = true, Name = "SubnauticaMP accept" };
            thread.Start();
            Log?.Invoke("Server listening on port " + Port);
        }

        public void Stop()
        {
            var listener = _listener;
            if (listener == null) return;
            _listener = null;
            try { listener.Stop(); } catch { }

            Connection[] all;
            lock (_lock) all = _connections.ToArray();
            foreach (var c in all) c.Close("Server stopped");
            Log?.Invoke("Server stopped");
        }

        void AcceptLoop()
        {
            var listener = _listener;
            while (listener != null && _listener == listener)
            {
                TcpClient tcp;
                try { tcp = listener.AcceptTcpClient(); }
                catch { return; } // listener stopped

                var conn = new Connection(tcp);
                conn.Tag = new Client();
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
            }
        }

        void HandleHello(Connection conn, Client client, HelloPacket hello)
        {
            if (hello.ProtocolVersion != Protocol.Version)
            {
                Reject(conn, $"Version mismatch (server {Protocol.Version}, you {hello.ProtocolVersion})");
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
                client.Joined = true;
            }

            conn.Send(welcome);
            Broadcast(new PlayerJoinedPacket { Id = client.Id, Name = client.Name }, except: conn);
            Log?.Invoke($"{client.Name} joined (id {client.Id})");
        }

        static void Reject(Connection conn, string reason)
        {
            conn.Send(new RejectedPacket { Reason = reason });
            conn.Close("Rejected: " + reason);
        }

        void OnClosed(Connection conn, string reason)
        {
            var client = (Client)conn.Tag;
            lock (_lock) _connections.Remove(conn);
            if (!client.Joined) return;

            Broadcast(new PlayerLeftPacket { Id = client.Id }, except: null);
            Log?.Invoke($"{client.Name} left ({reason})");
        }

        void Broadcast(Packet packet, Connection except)
        {
            Connection[] targets;
            lock (_lock) targets = _connections.Where(c => c != except && ((Client)c.Tag).Joined).ToArray();
            foreach (var c in targets) c.Send(packet);
        }
    }
}
