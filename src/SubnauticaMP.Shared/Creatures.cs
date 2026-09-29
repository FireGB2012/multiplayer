using System.Collections.Generic;
using System.IO;

namespace SubnauticaMP.Shared
{
    // What the game rolled for one entity slot (fish, plants, resources): everyone uses the first roll.
    public struct SpawnSlot
    {
        public string Key;     // slot world position, rounded
        public string ClassId; // "" = the slot stays empty
        public int Count;
        public void Write(BinaryWriter w) { w.Write(Key ?? ""); w.Write(ClassId ?? ""); w.Write(Count); }
        public static SpawnSlot Read(BinaryReader r) => new SpawnSlot { Key = r.ReadString(), ClassId = r.ReadString(), Count = r.ReadInt32() };
    }

    public sealed class SpawnSlotsPacket : Packet
    {
        public const int MaxSlots = 20000;
        public List<SpawnSlot> Slots = new List<SpawnSlot>();
        public override PacketType Type => PacketType.SpawnSlots;

        public override void Write(BinaryWriter w)
        {
            w.Write(Slots.Count);
            foreach (var s in Slots) s.Write(w);
        }

        public override void Read(BinaryReader r)
        {
            int n = r.ReadInt32();
            if (n < 0 || n > MaxSlots) throw new InvalidDataException("Bad slot count");
            Slots = new List<SpawnSlot>(n);
            for (int i = 0; i < n; i++) Slots.Add(SpawnSlot.Read(r));
        }
    }

    // client -> server: OwnerId = my id (claim), 0 (release) or another player's id (hand over).
    // server -> all: these creatures now belong to OwnerId (0 = nobody).
    public sealed class CreatureOwnerPacket : Packet
    {
        public const int MaxIds = 5000;
        public List<string> Ids = new List<string>();
        public int OwnerId;
        public override PacketType Type => PacketType.CreatureOwner;

        public override void Write(BinaryWriter w)
        {
            w.Write(OwnerId);
            w.Write(Ids.Count);
            foreach (var id in Ids) w.Write(id ?? "");
        }

        public override void Read(BinaryReader r)
        {
            OwnerId = r.ReadInt32();
            int n = r.ReadInt32();
            if (n < 0 || n > MaxIds) throw new InvalidDataException("Bad id count");
            Ids = new List<string>(n);
            for (int i = 0; i < n; i++) Ids.Add(r.ReadString());
        }
    }

    public struct CreatureState
    {
        public string Id;
        public Vec3 Position;
        public Quat Rotation;
        public float Aggression;
        public void Write(BinaryWriter w) { w.Write(Id ?? ""); Position.Write(w); Rotation.Write(w); w.Write(Aggression); }
        public static CreatureState Read(BinaryReader r) => new CreatureState { Id = r.ReadString(), Position = Vec3.Read(r), Rotation = Quat.Read(r), Aggression = r.ReadSingle() };
    }

    // owner -> others: where its creatures are
    public sealed class CreatureStatesPacket : Packet
    {
        public const int MaxStates = 5000;
        public List<CreatureState> States = new List<CreatureState>();
        public override PacketType Type => PacketType.CreatureStates;

        public override void Write(BinaryWriter w)
        {
            w.Write(States.Count);
            foreach (var s in States) s.Write(w);
        }

        public override void Read(BinaryReader r)
        {
            int n = r.ReadInt32();
            if (n < 0 || n > MaxStates) throw new InvalidDataException("Bad state count");
            States = new List<CreatureState>(n);
            for (int i = 0; i < n; i++) States.Add(CreatureState.Read(r));
        }
    }

    // someone hurt a creature they don't own -> server -> its owner
    public sealed class CreatureDamagePacket : Packet
    {
        public string Id;
        public float Damage;
        public int DamageType;
        public Vec3 Position;
        public override PacketType Type => PacketType.CreatureDamage;
        public override void Write(BinaryWriter w) { w.Write(Id ?? ""); w.Write(Damage); w.Write(DamageType); Position.Write(w); }
        public override void Read(BinaryReader r) { Id = r.ReadString(); Damage = r.ReadSingle(); DamageType = r.ReadInt32(); Position = Vec3.Read(r); }
    }

    public sealed class CreatureDiedPacket : Packet
    {
        public string Id;
        public override PacketType Type => PacketType.CreatureDied;
        public override void Write(BinaryWriter w) => w.Write(Id ?? "");
        public override void Read(BinaryReader r) => Id = r.ReadString();
    }
}
