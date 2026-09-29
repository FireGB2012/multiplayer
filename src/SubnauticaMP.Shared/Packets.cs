using System;
using System.Collections.Generic;
using System.IO;

namespace SubnauticaMP.Shared
{
    public enum PacketType : byte
    {
        Hello = 1,        // client -> server: name + protocol version
        Welcome = 2,      // server -> client: your id + everyone already here
        Rejected = 3,     // server -> client: reason, then disconnect
        PlayerJoined = 4, // server -> all
        PlayerLeft = 5,   // server -> all
        PlayerState = 6,  // client -> server -> others: position/rotation
        Chat = 7,         // client -> server -> all
    }

    public struct Vec3
    {
        public float X, Y, Z;
        public Vec3(float x, float y, float z) { X = x; Y = y; Z = z; }
    }

    public struct Quat
    {
        public float X, Y, Z, W;
        public Quat(float x, float y, float z, float w) { X = x; Y = y; Z = z; W = w; }
    }

    [Flags]
    public enum PlayerFlags : byte
    {
        None = 0,
        Underwater = 1,
        InBase = 2,
        InVehicle = 4,
    }

    public abstract class Packet
    {
        public abstract PacketType Type { get; }
        public abstract void Write(BinaryWriter w);
        public abstract void Read(BinaryReader r);

        public static Packet Create(PacketType type)
        {
            switch (type)
            {
                case PacketType.Hello: return new HelloPacket();
                case PacketType.Welcome: return new WelcomePacket();
                case PacketType.Rejected: return new RejectedPacket();
                case PacketType.PlayerJoined: return new PlayerJoinedPacket();
                case PacketType.PlayerLeft: return new PlayerLeftPacket();
                case PacketType.PlayerState: return new PlayerStatePacket();
                case PacketType.Chat: return new ChatPacket();
                default: throw new InvalidDataException("Unknown packet type " + (byte)type);
            }
        }
    }

    public sealed class HelloPacket : Packet
    {
        public int ProtocolVersion;
        public string Name;
        public override PacketType Type => PacketType.Hello;
        public override void Write(BinaryWriter w) { w.Write(ProtocolVersion); w.Write(Name ?? ""); }
        public override void Read(BinaryReader r) { ProtocolVersion = r.ReadInt32(); Name = r.ReadString(); }
    }

    public sealed class PlayerInfo
    {
        public int Id;
        public string Name;
    }

    public sealed class WelcomePacket : Packet
    {
        public int YourId;
        public List<PlayerInfo> Players = new List<PlayerInfo>();
        public override PacketType Type => PacketType.Welcome;

        public override void Write(BinaryWriter w)
        {
            w.Write(YourId);
            w.Write(Players.Count);
            foreach (var p in Players) { w.Write(p.Id); w.Write(p.Name ?? ""); }
        }

        public override void Read(BinaryReader r)
        {
            YourId = r.ReadInt32();
            int count = r.ReadInt32();
            if (count < 0 || count > Protocol.MaxPlayers) throw new InvalidDataException("Bad player count");
            Players = new List<PlayerInfo>(count);
            for (int i = 0; i < count; i++)
                Players.Add(new PlayerInfo { Id = r.ReadInt32(), Name = r.ReadString() });
        }
    }

    public sealed class RejectedPacket : Packet
    {
        public string Reason;
        public override PacketType Type => PacketType.Rejected;
        public override void Write(BinaryWriter w) { w.Write(Reason ?? ""); }
        public override void Read(BinaryReader r) { Reason = r.ReadString(); }
    }

    public sealed class PlayerJoinedPacket : Packet
    {
        public int Id;
        public string Name;
        public override PacketType Type => PacketType.PlayerJoined;
        public override void Write(BinaryWriter w) { w.Write(Id); w.Write(Name ?? ""); }
        public override void Read(BinaryReader r) { Id = r.ReadInt32(); Name = r.ReadString(); }
    }

    public sealed class PlayerLeftPacket : Packet
    {
        public int Id;
        public override PacketType Type => PacketType.PlayerLeft;
        public override void Write(BinaryWriter w) { w.Write(Id); }
        public override void Read(BinaryReader r) { Id = r.ReadInt32(); }
    }

    public sealed class PlayerStatePacket : Packet
    {
        public int Id; // filled in by the server; clients send 0
        public Vec3 Position;
        public Quat Rotation;
        public PlayerFlags Flags;
        public override PacketType Type => PacketType.PlayerState;

        public override void Write(BinaryWriter w)
        {
            w.Write(Id);
            w.Write(Position.X); w.Write(Position.Y); w.Write(Position.Z);
            w.Write(Rotation.X); w.Write(Rotation.Y); w.Write(Rotation.Z); w.Write(Rotation.W);
            w.Write((byte)Flags);
        }

        public override void Read(BinaryReader r)
        {
            Id = r.ReadInt32();
            Position = new Vec3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            Rotation = new Quat(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            Flags = (PlayerFlags)r.ReadByte();
        }
    }

    public sealed class ChatPacket : Packet
    {
        public int SenderId; // filled in by the server
        public string Text;
        public override PacketType Type => PacketType.Chat;
        public override void Write(BinaryWriter w) { w.Write(SenderId); w.Write(Text ?? ""); }
        public override void Read(BinaryReader r) { SenderId = r.ReadInt32(); Text = r.ReadString(); }
    }
}
