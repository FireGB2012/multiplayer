using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;

namespace SubnauticaMP.Shared
{
    // One TCP socket with a background reader thread and a background writer thread.
    // Send never blocks the caller (the game's main thread, or the server relaying for someone else):
    // packets go into a queue and the writer thread pushes them out. Small live updates (positions)
    // jump ahead of big ones (base snapshots) that haven't started sending yet.
    public sealed class Connection
    {
        const long MaxQueuedBytes = 64L * 1024 * 1024; // someone this far behind isn't coming back

        readonly TcpClient _tcp;
        readonly NetworkStream _stream;
        readonly object _queueLock = new object();
        // each entry is a Packet (turned into bytes on the writer thread) or already-made bytes, plus when it was queued
        readonly Queue<(object item, long queued)> _urgent = new Queue<(object, long)>();
        readonly Queue<(object item, long queued)> _normal = new Queue<(object, long)>();
        long _queuedBytes;
        string _closeAfterSend;
        int _closed;

        public event Action<Connection, Packet> PacketReceived;

        // Stats for the lag reports: counts since the start, and the longest a packet sat in the queue
        // before going out (big = this PC is too busy to send in time). TakeMaxWaitMs resets it.
        long _packetsIn, _packetsOut, _bytesOut;
        int _maxWaitMs;
        public long PacketsIn => Interlocked.Read(ref _packetsIn);
        public long PacketsOut => Interlocked.Read(ref _packetsOut);
        public long BytesOut => Interlocked.Read(ref _bytesOut);
        public int TakeMaxWaitMs() => Interlocked.Exchange(ref _maxWaitMs, 0);
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
            // above normal: when the game loads new terrain on every core, the network still gets its turn
            // (otherwise everyone's updates stall while the host turns and swims)
            new Thread(ReadLoop) { IsBackground = true, Name = "SubnauticaMP reader", Priority = ThreadPriority.AboveNormal }.Start();
            new Thread(WriteLoop) { IsBackground = true, Name = "SubnauticaMP writer", Priority = ThreadPriority.AboveNormal }.Start();
        }

        // The packet is serialized (and compressed) on the writer thread, so the caller never waits.
        // Don't change the packet after sending it.
        public void Send(Packet packet)
        {
            if (packet != null) Enqueue(packet, 0, Protocol.IsUrgent(packet.Type));
        }

        // Already-serialized bytes (the server serializes a broadcast once for everyone).
        public void SendRaw(byte[] data, bool urgent)
        {
            if (data != null) Enqueue(data, data.Length, urgent);
        }

        void Enqueue(object item, int size, bool urgent)
        {
            if (!IsOpen) return;
            bool tooMuch;
            lock (_queueLock)
            {
                if (_closeAfterSend != null) return;
                (urgent ? _urgent : _normal).Enqueue((item, Stopwatch.GetTimestamp()));
                _queuedBytes += size;
                tooMuch = _queuedBytes > MaxQueuedBytes;
                Monitor.Pulse(_queueLock);
            }
            if (tooMuch) Close("Connection too slow to keep up");
        }

        // Sends everything already queued, then closes (so a "you were rejected" message still arrives).
        public void CloseAfterSend(string reason)
        {
            lock (_queueLock)
            {
                _closeAfterSend = reason ?? "Closed";
                Monitor.Pulse(_queueLock);
            }
        }

        public void Close(string reason)
        {
            if (Interlocked.Exchange(ref _closed, 1) != 0) return;
            lock (_queueLock)
            {
                _urgent.Clear();
                _normal.Clear();
                _queuedBytes = 0;
                Monitor.PulseAll(_queueLock);
            }
            try { _tcp.Close(); } catch { }
            Closed?.Invoke(this, reason);
        }

        void WriteLoop()
        {
            try
            {
                while (IsOpen)
                {
                    object item = null;
                    long queued = 0;
                    string closeReason = null;
                    lock (_queueLock)
                    {
                        while (IsOpen && _urgent.Count == 0 && _normal.Count == 0 && _closeAfterSend == null)
                            Monitor.Wait(_queueLock);
                        if (!IsOpen) return;
                        if (_urgent.Count > 0) (item, queued) = _urgent.Dequeue();
                        else if (_normal.Count > 0) (item, queued) = _normal.Dequeue();
                        else closeReason = _closeAfterSend; // everything sent, now close
                        if (item is byte[] raw) _queuedBytes -= raw.Length;
                    }
                    if (closeReason != null)
                    {
                        try { _stream.Flush(); Thread.Sleep(100); } catch { }
                        Close(closeReason);
                        return;
                    }
                    byte[] data;
                    try { data = item as byte[] ?? Protocol.Serialize((Packet)item); }
                    catch (InvalidOperationException) { continue; } // too big to send: drop it, keep the connection
                    _stream.Write(data, 0, data.Length);
                    Interlocked.Increment(ref _packetsOut);
                    Interlocked.Add(ref _bytesOut, data.Length);
                    int waited = (int)((Stopwatch.GetTimestamp() - queued) * 1000 / Stopwatch.Frequency);
                    int was;
                    while (waited > (was = Volatile.Read(ref _maxWaitMs)) && Interlocked.CompareExchange(ref _maxWaitMs, waited, was) != was) { }
                }
            }
            catch (Exception e)
            {
                Close("Send failed: " + e.Message);
            }
        }

        void ReadLoop()
        {
            try
            {
                while (IsOpen)
                {
                    var packet = Protocol.ReadPacket(_stream, MaxPacketSize);
                    if (packet == null) { Close("Connection closed"); return; }
                    packet.ReceivedTicks = Stopwatch.GetTimestamp();
                    Interlocked.Increment(ref _packetsIn);
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
