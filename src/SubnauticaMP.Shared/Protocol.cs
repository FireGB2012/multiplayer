using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace SubnauticaMP.Shared
{
    public static class Protocol
    {
        public const int Version = 13;
        public const int DefaultPort = 11000;
        public const int MaxPlayers = 16;
        public const int MaxPacketSize = 64 * 1024 * 1024; // server -> client: welcome snapshot (bases + spawn book) can get big
        public const int MaxHelloPacketSize = 256 * 1024;          // before a client has said hello
        public const int MaxClientPacketSize = 8 * 1024 * 1024;   // joined clients: a big base snapshot can be a few MB
        public const int MaxNameLength = 24;
        public const int MaxChatLength = 200;

        const byte CompressedFlag = 0x80;
        const int CompressAbove = 32 * 1024;

        // Live updates nothing else depends on: allowed to jump ahead of big queued snapshots.
        public static bool IsUrgent(PacketType type) =>
            type == PacketType.PlayerState || type == PacketType.CreatureStates ||
            type == PacketType.BuildGhost || type == PacketType.TimeSync;

        // Frame layout: [int32 length][byte type][payload]. length covers type + payload.
        // Big payloads (world snapshots, bases) are deflated; the top bit of the type byte says so.
        public static byte[] Serialize(Packet packet)
        {
            byte[] payload;
            using (var body = new MemoryStream())
            {
                using (var w = new BinaryWriter(body, Encoding.UTF8, true)) packet.Write(w);
                payload = body.ToArray();
            }

            byte type = (byte)packet.Type;
            if (payload.Length > CompressAbove)
            {
                using (var packed = new MemoryStream())
                {
                    using (var z = new DeflateStream(packed, CompressionLevel.Fastest, true)) z.Write(payload, 0, payload.Length);
                    if (packed.Length < payload.Length * 0.9)
                    {
                        payload = packed.ToArray();
                        type |= CompressedFlag;
                    }
                }
            }

            int length = payload.Length + 1;
            if (length > MaxPacketSize) throw new InvalidOperationException("Packet too large");
            var frame = new byte[4 + length];
            BitConverter.GetBytes(length).CopyTo(frame, 0);
            frame[4] = type;
            Buffer.BlockCopy(payload, 0, frame, 5, payload.Length);
            return frame;
        }

        // Blocks until a full packet arrives. Returns null on clean disconnect.
        public static Packet ReadPacket(Stream stream, int maxSize = MaxPacketSize)
        {
            var header = new byte[4];
            if (!ReadExactly(stream, header, 4)) return null;
            int length = BitConverter.ToInt32(header, 0);
            if (length < 1 || length > maxSize) throw new InvalidDataException("Bad packet length " + length);

            var body = new byte[length];
            if (!ReadExactly(stream, body, length)) return null;

            byte type = body[0];
            Stream payload = new MemoryStream(body, 1, body.Length - 1);
            if ((type & CompressedFlag) != 0)
            {
                var unpacked = new MemoryStream();
                using (var z = new DeflateStream(payload, CompressionMode.Decompress))
                {
                    var buf = new byte[81920];
                    int n;
                    while ((n = z.Read(buf, 0, buf.Length)) > 0)
                    {
                        unpacked.Write(buf, 0, n);
                        if (unpacked.Length > MaxPacketSize * 4L) throw new InvalidDataException("Packet unpacks too big");
                    }
                }
                unpacked.Position = 0;
                payload = unpacked;
            }
            using (payload)
            using (var r = new BinaryReader(payload, Encoding.UTF8))
            {
                var packet = Packet.Create((PacketType)(type & ~CompressedFlag));
                packet.Read(r);
                return packet;
            }
        }

        static bool ReadExactly(Stream stream, byte[] buffer, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(buffer, offset, count - offset);
                if (read <= 0) return false;
                offset += read;
            }
            return true;
        }

        public static string CleanName(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length > MaxNameLength) name = name.Substring(0, MaxNameLength);
            return name.Length == 0 ? "Diver" : name;
        }
    }
}
