using System;
using System.IO;
using System.Text;

namespace SubnauticaMP.Shared
{
    public static class Protocol
    {
        public const int Version = 7;
        public const int DefaultPort = 11000;
        public const int MaxPlayers = 16;
        public const int MaxPacketSize = 16 * 1024 * 1024; // server -> client: welcome snapshot can get big
        public const int MaxHelloPacketSize = 64 * 1024;          // before a client has said hello
        public const int MaxClientPacketSize = 8 * 1024 * 1024;   // joined clients: a big base snapshot can be a few MB
        public const int MaxNameLength = 24;
        public const int MaxChatLength = 200;

        // Frame layout: [int32 length][byte type][payload]. length covers type + payload.
        public static byte[] Serialize(Packet packet)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms, Encoding.UTF8))
            {
                w.Write(0); // placeholder for length
                w.Write((byte)packet.Type);
                packet.Write(w);
                w.Flush();
                int length = (int)ms.Length - 4;
                if (length > MaxPacketSize) throw new InvalidOperationException("Packet too large");
                ms.Position = 0;
                w.Write(length);
                return ms.ToArray();
            }
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

            using (var ms = new MemoryStream(body))
            using (var r = new BinaryReader(ms, Encoding.UTF8))
            {
                var packet = Packet.Create((PacketType)r.ReadByte());
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
