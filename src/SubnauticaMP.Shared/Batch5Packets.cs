using System.Collections.Generic;
using System.IO;

namespace SubnauticaMP.Shared
{
    public sealed class KickPacket : Packet
    {
        public int TargetId;
        public bool Ban;
        public override PacketType Type => PacketType.Kick;
        public override void Write(BinaryWriter w) { w.Write(TargetId); w.Write(Ban); }
        public override void Read(BinaryReader r) { TargetId = r.ReadInt32(); Ban = r.ReadBoolean(); }
    }

    // client -> server: I'm in bed (Asleep) or got up. server -> all: who's asleep; Skip = everyone is, night skipped by Amount seconds
    public sealed class SleepPacket : Packet
    {
        public int Id;
        public bool Asleep;
        public bool Skip;
        public float Amount;
        public override PacketType Type => PacketType.Sleep;
        public override void Write(BinaryWriter w) { w.Write(Id); w.Write(Asleep); w.Write(Skip); w.Write(Amount); }
        public override void Read(BinaryReader r) { Id = r.ReadInt32(); Asleep = r.ReadBoolean(); Skip = r.ReadBoolean(); Amount = r.ReadSingle(); }
    }

    // Power: one player (the writer) runs each power source and reports its level; others send what they used (Drain).
    public sealed class PowerPacket : Packet
    {
        public string Id;
        public float Power;
        public float Drain;    // > 0: "I used this much", for the writer to take off
        public int WriterId;   // filled in by the server
        public override PacketType Type => PacketType.Power;
        public override void Write(BinaryWriter w) { w.Write(Id ?? ""); w.Write(Power); w.Write(Drain); w.Write(WriterId); }
        public override void Read(BinaryReader r) { Id = r.ReadString(); Power = r.ReadSingle(); Drain = r.ReadSingle(); WriterId = r.ReadInt32(); }
    }

    public sealed class CraftPacket : Packet
    {
        public string CrafterId;
        public string TechType;
        public float Duration;
        public override PacketType Type => PacketType.Craft;
        public override void Write(BinaryWriter w) { w.Write(CrafterId ?? ""); w.Write(TechType ?? ""); w.Write(Duration); }
        public override void Read(BinaryReader r) { CrafterId = r.ReadString(); TechType = r.ReadString(); Duration = r.ReadSingle(); }
    }

    public sealed class BuildGhostPacket : Packet
    {
        public int Id;           // filled in by the server
        public string TechType;  // "" = stopped building
        public Vec3 Position;
        public Quat Rotation;
        public bool CanPlace;
        public override PacketType Type => PacketType.BuildGhost;
        public override void Write(BinaryWriter w) { w.Write(Id); w.Write(TechType ?? ""); Position.Write(w); Rotation.Write(w); w.Write(CanPlace); }
        public override void Read(BinaryReader r) { Id = r.ReadInt32(); TechType = r.ReadString(); Position = Vec3.Read(r); Rotation = Quat.Read(r); CanPlace = r.ReadBoolean(); }
    }

    public sealed class FiresPacket : Packet
    {
        public string SubId;
        public List<string> Nodes = new List<string>();
        public override PacketType Type => PacketType.Fires;

        public override void Write(BinaryWriter w)
        {
            w.Write(SubId ?? "");
            w.Write(Nodes.Count);
            foreach (var n in Nodes) w.Write(n ?? "");
        }

        public override void Read(BinaryReader r)
        {
            SubId = r.ReadString();
            int n = r.ReadInt32();
            if (n < 0 || n > 1000) throw new InvalidDataException("Bad fire count");
            Nodes = new List<string>(n);
            for (int i = 0; i < n; i++) Nodes.Add(r.ReadString());
        }
    }

    public sealed class FireDousePacket : Packet
    {
        public string SubId;
        public string Node;
        public float Amount;
        public override PacketType Type => PacketType.FireDouse;
        public override void Write(BinaryWriter w) { w.Write(SubId ?? ""); w.Write(Node ?? ""); w.Write(Amount); }
        public override void Read(BinaryReader r) { SubId = r.ReadString(); Node = r.ReadString(); Amount = r.ReadSingle(); }
    }

    public sealed class HullHealthPacket : Packet
    {
        public string Key;    // anchor id | local position
        public float Delta;   // < 0 damage, > 0 repair
        public override PacketType Type => PacketType.HullHealth;
        public override void Write(BinaryWriter w) { w.Write(Key ?? ""); w.Write(Delta); }
        public override void Read(BinaryReader r) { Key = r.ReadString(); Delta = r.ReadSingle(); }
    }

    public sealed class PickedPacket : Packet
    {
        public string Key;    // anchor id | local position of the fruit
        public override PacketType Type => PacketType.Picked;
        public override void Write(BinaryWriter w) => w.Write(Key ?? "");
        public override void Read(BinaryReader r) => Key = r.ReadString();
    }
}
