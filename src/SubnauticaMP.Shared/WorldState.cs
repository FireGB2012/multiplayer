using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SubnauticaMP.Shared
{
    // Everything the server remembers about the shared world. Sent to players when they join
    // and saved to disk so the world survives server restarts.
    public sealed class WorldState
    {
        const int FileMagic = 0x534E4D50; // "SNMP"
        const int FileVersion = 1;
        const int MaxEntries = 1_000_000;

        public HashSet<string> Blueprints = new HashSet<string>();
        public HashSet<string> Analyzed = new HashSet<string>();
        public HashSet<string> Databank = new HashSet<string>();
        public HashSet<string> RemovedEntities = new HashSet<string>();
        public Dictionary<string, VehicleInfo> Vehicles = new Dictionary<string, VehicleInfo>();
        public bool HasTime;
        public double TimePassed;

        public HashSet<string> SetFor(UnlockKind kind)
        {
            switch (kind)
            {
                case UnlockKind.Analyzed: return Analyzed;
                case UnlockKind.Databank: return Databank;
                default: return Blueprints;
            }
        }

        public void Write(BinaryWriter w)
        {
            WriteSet(w, Blueprints);
            WriteSet(w, Analyzed);
            WriteSet(w, Databank);
            WriteSet(w, RemovedEntities);
            w.Write(Vehicles.Count);
            foreach (var v in Vehicles.Values) v.Write(w);
            w.Write(HasTime);
            w.Write(TimePassed);
        }

        public static WorldState Read(BinaryReader r)
        {
            var s = new WorldState
            {
                Blueprints = ReadSet(r),
                Analyzed = ReadSet(r),
                Databank = ReadSet(r),
                RemovedEntities = ReadSet(r),
            };
            int count = ReadCount(r);
            for (int i = 0; i < count; i++)
            {
                var v = VehicleInfo.Read(r);
                s.Vehicles[v.Id] = v;
            }
            s.HasTime = r.ReadBoolean();
            s.TimePassed = r.ReadDouble();
            return s;
        }

        public WorldState Clone()
        {
            return new WorldState
            {
                Blueprints = new HashSet<string>(Blueprints),
                Analyzed = new HashSet<string>(Analyzed),
                Databank = new HashSet<string>(Databank),
                RemovedEntities = new HashSet<string>(RemovedEntities),
                Vehicles = Vehicles.ToDictionary(kv => kv.Key, kv => kv.Value.Clone()),
                HasTime = HasTime,
                TimePassed = TimePassed,
            };
        }

        public void SaveToFile(string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = path + ".tmp";
            using (var fs = File.Create(tmp))
            using (var w = new BinaryWriter(fs, Encoding.UTF8))
            {
                w.Write(FileMagic);
                w.Write(FileVersion);
                Write(w);
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        public static WorldState LoadFromFile(string path)
        {
            using (var fs = File.OpenRead(path))
            using (var r = new BinaryReader(fs, Encoding.UTF8))
            {
                if (r.ReadInt32() != FileMagic) throw new InvalidDataException("Not a SubnauticaMP world file");
                int version = r.ReadInt32();
                if (version != FileVersion) throw new InvalidDataException("Unsupported world file version " + version);
                return Read(r);
            }
        }

        static void WriteSet(BinaryWriter w, HashSet<string> set)
        {
            w.Write(set.Count);
            foreach (var s in set) w.Write(s);
        }

        static HashSet<string> ReadSet(BinaryReader r)
        {
            int count = ReadCount(r);
            var set = new HashSet<string>();
            for (int i = 0; i < count; i++) set.Add(r.ReadString());
            return set;
        }

        static int ReadCount(BinaryReader r)
        {
            int count = r.ReadInt32();
            if (count < 0 || count > MaxEntries) throw new InvalidDataException("Bad count " + count);
            return count;
        }
    }
}
