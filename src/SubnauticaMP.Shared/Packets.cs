using System;
using System.Collections.Generic;
using System.IO;

namespace SubnauticaMP.Shared
{
    public enum PacketType : byte
    {
        Hello = 1,          // client -> server: name + protocol version
        Welcome = 2,        // server -> client: your id, who's here, full world snapshot
        Rejected = 3,       // server -> client: reason, then disconnect
        PlayerJoined = 4,   // server -> all
        PlayerLeft = 5,     // server -> all
        PlayerState = 6,    // client -> server -> others: position/rotation
        Chat = 7,           // client -> server -> all
        Unlock = 8,         // blueprint / analyzed tech / databank entry learned
        EntityRemoved = 9,  // world item picked up or broken
        VehicleSpawned = 10,
        VehicleState = 11,  // owner -> others: vehicle position
        VehicleOwner = 12,  // client claims a vehicle (entered it); server -> all: new owner
        VehicleRemoved = 13,
        TimeSync = 14,      // server -> clients: world clock. client -> server once to seed a new world
        StartGame = 15,     // host -> server -> all: leave the lobby, play the intro together
        Host = 16,          // server -> all: who the host is now
        Structure = 17,     // a base or built object, saved with the game's own serializer (empty = gone)
        Container = 18,     // everything inside a locker / storage
        Fragment = 19,      // fragment scan progress (e.g. 2 of 3 Seamoth fragments)
        ItemDropped = 20,   // an item put down in the world (saved with the game's serializer)
        Door = 21,          // door / hatch opened or closed
        PlayerDied = 22,
    }

    public struct Vec3
    {
        public float X, Y, Z;
        public Vec3(float x, float y, float z) { X = x; Y = y; Z = z; }
        public void Write(BinaryWriter w) { w.Write(X); w.Write(Y); w.Write(Z); }
        public static Vec3 Read(BinaryReader r) => new Vec3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    }

    public struct Quat
    {
        public float X, Y, Z, W;
        public Quat(float x, float y, float z, float w) { X = x; Y = y; Z = z; W = w; }
        public void Write(BinaryWriter w) { w.Write(X); w.Write(Y); w.Write(Z); w.Write(W); }
        public static Quat Read(BinaryReader r) => new Quat(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    }

    [Flags]
    public enum PlayerFlags : byte
    {
        None = 0,
        Underwater = 1,
        InBase = 2,
        InVehicle = 4,
    }

    public enum UnlockKind : byte
    {
        Blueprint = 0,   // KnownTech.Add
        Analyzed = 1,    // KnownTech.Analyze
        Databank = 2,    // PDAEncyclopedia.Add
        PdaLog = 3,      // PDALog.Add (voice logs / messages)
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
                case PacketType.Unlock: return new UnlockPacket();
                case PacketType.EntityRemoved: return new EntityRemovedPacket();
                case PacketType.VehicleSpawned: return new VehicleSpawnedPacket();
                case PacketType.VehicleState: return new VehicleStatePacket();
                case PacketType.VehicleOwner: return new VehicleOwnerPacket();
                case PacketType.VehicleRemoved: return new VehicleRemovedPacket();
                case PacketType.TimeSync: return new TimeSyncPacket();
                case PacketType.StartGame: return new StartGamePacket();
                case PacketType.Host: return new HostPacket();
                case PacketType.Structure: return new StructurePacket();
                case PacketType.Container: return new ContainerPacket();
                case PacketType.Fragment: return new FragmentPacket();
                case PacketType.ItemDropped: return new ItemDroppedPacket();
                case PacketType.Door: return new DoorPacket();
                case PacketType.PlayerDied: return new PlayerDiedPacket();
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
        public int HostId;
        public List<PlayerInfo> Players = new List<PlayerInfo>();
        public WorldState World = new WorldState();
        public override PacketType Type => PacketType.Welcome;

        public override void Write(BinaryWriter w)
        {
            w.Write(YourId);
            w.Write(HostId);
            w.Write(Players.Count);
            foreach (var p in Players) { w.Write(p.Id); w.Write(p.Name ?? ""); }
            World.Write(w);
        }

        public override void Read(BinaryReader r)
        {
            YourId = r.ReadInt32();
            HostId = r.ReadInt32();
            int count = r.ReadInt32();
            if (count < 0 || count > Protocol.MaxPlayers) throw new InvalidDataException("Bad player count");
            Players = new List<PlayerInfo>(count);
            for (int i = 0; i < count; i++)
                Players.Add(new PlayerInfo { Id = r.ReadInt32(), Name = r.ReadString() });
            World = WorldState.Read(r);
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
        public byte Health, Food, Water; // 0-100
        public string Held;              // TechType in their hand, "" = empty
        public override PacketType Type => PacketType.PlayerState;

        public override void Write(BinaryWriter w)
        {
            w.Write(Id);
            Position.Write(w);
            Rotation.Write(w);
            w.Write((byte)Flags);
            w.Write(Health); w.Write(Food); w.Write(Water);
            w.Write(Held ?? "");
        }

        public override void Read(BinaryReader r)
        {
            Id = r.ReadInt32();
            Position = Vec3.Read(r);
            Rotation = Quat.Read(r);
            Flags = (PlayerFlags)r.ReadByte();
            Health = r.ReadByte(); Food = r.ReadByte(); Water = r.ReadByte();
            Held = r.ReadString();
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

    public sealed class UnlockPacket : Packet
    {
        public UnlockKind Kind;
        public string Key; // TechType name, or databank key
        public override PacketType Type => PacketType.Unlock;
        public override void Write(BinaryWriter w) { w.Write((byte)Kind); w.Write(Key ?? ""); }
        public override void Read(BinaryReader r) { Kind = (UnlockKind)r.ReadByte(); Key = r.ReadString(); }
    }

    public sealed class EntityRemovedPacket : Packet
    {
        public string EntityId;
        public override PacketType Type => PacketType.EntityRemoved;
        public override void Write(BinaryWriter w) { w.Write(EntityId ?? ""); }
        public override void Read(BinaryReader r) { EntityId = r.ReadString(); }
    }

    public sealed class VehicleInfo
    {
        public string Id;       // the game's UniqueIdentifier id, same on every machine
        public string TechType; // "Seamoth", "Exosuit", "Cyclops"
        public Vec3 Position;
        public Quat Rotation;
        public int OwnerId;     // player simulating it right now, 0 = nobody

        public void Write(BinaryWriter w)
        {
            w.Write(Id ?? ""); w.Write(TechType ?? "");
            Position.Write(w); Rotation.Write(w);
            w.Write(OwnerId);
        }

        public static VehicleInfo Read(BinaryReader r) => new VehicleInfo
        {
            Id = r.ReadString(), TechType = r.ReadString(),
            Position = Vec3.Read(r), Rotation = Quat.Read(r),
            OwnerId = r.ReadInt32(),
        };

        public VehicleInfo Clone() => (VehicleInfo)MemberwiseClone();
    }

    public sealed class VehicleSpawnedPacket : Packet
    {
        public VehicleInfo Vehicle = new VehicleInfo();
        public override PacketType Type => PacketType.VehicleSpawned;
        public override void Write(BinaryWriter w) { Vehicle.Write(w); }
        public override void Read(BinaryReader r) { Vehicle = VehicleInfo.Read(r); }
    }

    public sealed class VehicleStatePacket : Packet
    {
        public string Id;
        public Vec3 Position;
        public Quat Rotation;
        public override PacketType Type => PacketType.VehicleState;
        public override void Write(BinaryWriter w) { w.Write(Id ?? ""); Position.Write(w); Rotation.Write(w); }
        public override void Read(BinaryReader r) { Id = r.ReadString(); Position = Vec3.Read(r); Rotation = Quat.Read(r); }
    }

    public sealed class VehicleOwnerPacket : Packet
    {
        public string Id;
        public int OwnerId; // ignored when a client sends it: the sender becomes owner
        public override PacketType Type => PacketType.VehicleOwner;
        public override void Write(BinaryWriter w) { w.Write(Id ?? ""); w.Write(OwnerId); }
        public override void Read(BinaryReader r) { Id = r.ReadString(); OwnerId = r.ReadInt32(); }
    }

    public sealed class VehicleRemovedPacket : Packet
    {
        public string Id;
        public override PacketType Type => PacketType.VehicleRemoved;
        public override void Write(BinaryWriter w) { w.Write(Id ?? ""); }
        public override void Read(BinaryReader r) { Id = r.ReadString(); }
    }

    public sealed class TimeSyncPacket : Packet
    {
        public double TimePassed; // DayNightCycle.timePassedAsDouble, 1200 = one day
        public override PacketType Type => PacketType.TimeSync;
        public override void Write(BinaryWriter w) { w.Write(TimePassed); }
        public override void Read(BinaryReader r) { TimePassed = r.ReadDouble(); }
    }
}

namespace SubnauticaMP.Shared
{
    public sealed class StartGamePacket : Packet
    {
        public override PacketType Type => PacketType.StartGame;
        public override void Write(BinaryWriter w) { }
        public override void Read(BinaryReader r) { }
    }

    public sealed class HostPacket : Packet
    {
        public int HostId;
        public override PacketType Type => PacketType.Host;
        public override void Write(BinaryWriter w) { w.Write(HostId); }
        public override void Read(BinaryReader r) { HostId = r.ReadInt32(); }
    }
}

namespace SubnauticaMP.Shared
{
    internal static class Bytes
    {
        public const int MaxBlob = 8 * 1024 * 1024;

        public static void Write(BinaryWriter w, byte[] data)
        {
            data = data ?? new byte[0];
            w.Write(data.Length);
            w.Write(data);
        }

        public static byte[] Read(BinaryReader r)
        {
            int len = r.ReadInt32();
            if (len < 0 || len > MaxBlob) throw new InvalidDataException("Bad blob length " + len);
            return r.ReadBytes(len);
        }
    }

    public sealed class StructurePacket : Packet
    {
        public string Id;
        public byte[] Data; // empty = deconstructed / gone
        public override PacketType Type => PacketType.Structure;
        public override void Write(BinaryWriter w) { w.Write(Id ?? ""); Bytes.Write(w, Data); }
        public override void Read(BinaryReader r) { Id = r.ReadString(); Data = Bytes.Read(r); }
    }

    public sealed class ContainerPacket : Packet
    {
        public string Id;
        public List<byte[]> Items = new List<byte[]>();
        public override PacketType Type => PacketType.Container;

        public override void Write(BinaryWriter w)
        {
            w.Write(Id ?? "");
            w.Write(Items.Count);
            foreach (var item in Items) Bytes.Write(w, item);
        }

        public override void Read(BinaryReader r)
        {
            Id = r.ReadString();
            int count = r.ReadInt32();
            if (count < 0 || count > 10000) throw new InvalidDataException("Bad item count");
            Items = new List<byte[]>(count);
            for (int i = 0; i < count; i++) Items.Add(Bytes.Read(r));
        }
    }

    public sealed class FragmentPacket : Packet
    {
        public string TechType;
        public int Unlocked;
        public override PacketType Type => PacketType.Fragment;
        public override void Write(BinaryWriter w) { w.Write(TechType ?? ""); w.Write(Unlocked); }
        public override void Read(BinaryReader r) { TechType = r.ReadString(); Unlocked = r.ReadInt32(); }
    }
}

namespace SubnauticaMP.Shared
{
    public sealed class ItemDroppedPacket : Packet
    {
        public string Id;
        public byte[] Data;
        public override PacketType Type => PacketType.ItemDropped;
        public override void Write(BinaryWriter w) { w.Write(Id ?? ""); Bytes.Write(w, Data); }
        public override void Read(BinaryReader r) { Id = r.ReadString(); Data = Bytes.Read(r); }
    }

    public sealed class DoorPacket : Packet
    {
        public string Id;
        public bool Open;
        public float Duration;
        public override PacketType Type => PacketType.Door;
        public override void Write(BinaryWriter w) { w.Write(Id ?? ""); w.Write(Open); w.Write(Duration); }
        public override void Read(BinaryReader r) { Id = r.ReadString(); Open = r.ReadBoolean(); Duration = r.ReadSingle(); }
    }

    public sealed class PlayerDiedPacket : Packet
    {
        public int Id; // filled in by the server
        public Vec3 Position;
        public override PacketType Type => PacketType.PlayerDied;
        public override void Write(BinaryWriter w) { w.Write(Id); Position.Write(w); }
        public override void Read(BinaryReader r) { Id = r.ReadInt32(); Position = Vec3.Read(r); }
    }
}
