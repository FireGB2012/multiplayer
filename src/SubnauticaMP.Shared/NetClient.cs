using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading;

namespace SubnauticaMP.Shared
{
    public enum ClientState { Disconnected, Connecting, Connected }

    // Network runs on background threads; the game polls TryDequeue() from its main thread.
    public sealed class NetClient
    {
        readonly ConcurrentQueue<Packet> _incoming = new ConcurrentQueue<Packet>();
        Connection _conn;

        public ClientState State { get; private set; } = ClientState.Disconnected;
        public int LocalId { get; private set; }
        public string LastError { get; private set; }

        // Non-blocking: kicks off the connect on a worker thread.
        public void Connect(string host, int port, string name, int timeoutMs = 5000, string password = null, int color = DiverColors.Default,
            System.Collections.Generic.List<ModInfo> mods = null, System.Collections.Generic.Dictionary<string, int> techTypes = null)
        {
            if (State != ClientState.Disconnected) return;
            State = ClientState.Connecting;
            LastError = null;
            LocalId = 0;

            var thread = new Thread(() => DoConnect(host, port, name, timeoutMs, password, color, mods, techTypes)) { IsBackground = true, Name = "SubnauticaMP connect" };
            thread.Start();
        }

        void DoConnect(string host, int port, string name, int timeoutMs, string password, int color,
            System.Collections.Generic.List<ModInfo> mods, System.Collections.Generic.Dictionary<string, int> techTypes)
        {
            try
            {
                var tcp = new TcpClient();
                var result = tcp.BeginConnect(host, port, null, null);
                if (!result.AsyncWaitHandle.WaitOne(timeoutMs))
                {
                    tcp.Close();
                    Fail("Connection timed out");
                    return;
                }
                tcp.EndConnect(result);

                var conn = new Connection(tcp);
                conn.PacketReceived += OnPacket;
                conn.Closed += OnClosed;
                _conn = conn;
                conn.Start();
                conn.Send(new HelloPacket { ProtocolVersion = Protocol.Version, Name = Protocol.CleanName(name), Password = password ?? "", Color = color,
                    Mods = mods ?? new System.Collections.Generic.List<ModInfo>(),
                    TechTypes = techTypes ?? new System.Collections.Generic.Dictionary<string, int>() });
            }
            catch (Exception e)
            {
                Fail(e.Message);
            }
        }

        void OnPacket(Connection conn, Packet packet)
        {
            switch (packet)
            {
                case WelcomePacket welcome:
                    LocalId = welcome.YourId;
                    State = ClientState.Connected;
                    break;
                case RejectedPacket rejected:
                    LastError = rejected.Reason;
                    break;
            }
            _incoming.Enqueue(packet);
        }

        void OnClosed(Connection conn, string reason)
        {
            if (conn != _conn) return;
            _conn = null;
            if (LastError == null) LastError = reason;
            State = ClientState.Disconnected;
        }

        void Fail(string error)
        {
            LastError = error;
            State = ClientState.Disconnected;
        }

        public bool TryDequeue(out Packet packet) => _incoming.TryDequeue(out packet);
        public int QueuedCount => _incoming.Count;

        public void Send(Packet packet)
        {
            if (State == ClientState.Connected) _conn?.Send(packet);
        }

        public void Disconnect()
        {
            var conn = _conn;
            if (conn == null) { State = ClientState.Disconnected; return; }
            LastError = "Disconnected";
            conn.Close("Disconnected");
            while (_incoming.TryDequeue(out _)) { }
        }
    }
}
