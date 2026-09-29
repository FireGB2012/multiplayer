using System;
using System.Net.Sockets;
using System.Threading;

namespace SubnauticaMP.Shared
{
    // One TCP socket with a background reader thread. Callbacks fire on that thread.
    public sealed class Connection
    {
        readonly TcpClient _tcp;
        readonly NetworkStream _stream;
        readonly object _sendLock = new object();
        int _closed;

        public event Action<Connection, Packet> PacketReceived;
        public event Action<Connection, string> Closed;

        public bool IsOpen => _closed == 0;
        public object Tag; // free slot for whoever owns the connection
        public int MaxPacketSize = Protocol.MaxPacketSize;
        public string RemoteIp
        {
            get
            {
                try { return (_tcp.Client.RemoteEndPoint as System.Net.IPEndPoint)?.Address.ToString() ?? ""; }
                catch { return ""; }
            }
        }

        public Connection(TcpClient tcp)
        {
            _tcp = tcp;
            _tcp.NoDelay = true;
            _stream = tcp.GetStream();
        }

        public void Start()
        {
            var thread = new Thread(ReadLoop) { IsBackground = true, Name = "SubnauticaMP reader" };
            thread.Start();
        }

        public void Send(Packet packet)
        {
            if (!IsOpen) return;
            var data = Protocol.Serialize(packet);
            try
            {
                lock (_sendLock) _stream.Write(data, 0, data.Length);
            }
            catch (Exception e)
            {
                Close("Send failed: " + e.Message);
            }
        }

        public void Close(string reason)
        {
            if (Interlocked.Exchange(ref _closed, 1) != 0) return;
            try { _tcp.Close(); } catch { }
            Closed?.Invoke(this, reason);
        }

        void ReadLoop()
        {
            try
            {
                while (IsOpen)
                {
                    var packet = Protocol.ReadPacket(_stream, MaxPacketSize);
                    if (packet == null) { Close("Connection closed"); return; }
                    PacketReceived?.Invoke(this, packet);
                }
            }
            catch (Exception e)
            {
                Close(IsOpen ? "Read failed: " + e.Message : "Closed");
            }
        }
    }
}
