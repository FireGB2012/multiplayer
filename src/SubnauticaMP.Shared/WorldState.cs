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
        const int FileVersion = 3;
        const int MaxEntries = 1_000_000;

        public HashSet<string> Blueprints = new HashSet<string>();
        public HashSet<string> Analyzed = new HashSet<string>();
        public HashSet<string> Databank = new HashSet<string>();
        public HashSet<string> RemovedEntities = new HashSet<string>();
        public Dictionary<string, VehicleInfo> Vehicles = new Dictionary<string, VehicleInfo>();
        public bool HasTime;
        public double TimePassed;
        public string WorldId = Guid.NewGuid().ToString("N"); // lets each player find their own save for this world
        public string GameMode = GameModes.Survival;
        public bool Started; // false = still in the lobby, waiting for the host to start
        public HashSet<string> PdaLog = new HashSet<string>();
        public Dictionary<string, int> Fragments = new Dictionary<string, int>();
        public Dictionary<string, byte[]> Structures = new Dictionary<string, byte[]>();
        public Dictionary<string, List<byte[]>> Containers = new Dictionary<string, List<byte[]>>();

        public HashSet<string> SetFor(UnlockKind kind)
        {
            switch (kind)
            {
                case UnlockKind.Analyzed: return Analyzed;
                case UnlockKind.Databank: return Databank;
                case UnlockKind.PdaLog: return PdaLog;
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
            w.Write(WorldId ?? "");
            w.Write(GameMode ?? GameModes.Survival);
            w.Write(Started);

            WriteSet(w, PdaLog);
            w.Write(Fragments.Count);
            foreach (var kv in Fragments) { w.Write(kv.Key); w.Write(kv.Value); }
            w.Write(Structures.Count);
            foreach (var kv in Structures) { w.Write(kv.Key); Bytes.Write(w, kv.Value); }
            w.Write(Containers.Count);
            foreach (var kv in Containers)
            {
                w.Write(kv.Key);
                w.Write(kv.Value.Count);
                foreach (var item in kv.Value) Bytes.Write(w, item);
            }
        }

        public static WorldState Read(BinaryReader r) => Read(r, FileVersion);

        static WorldState Read(BinaryReader r, int version)
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
            if (version >= 2)
            {
                s.WorldId = r.ReadString();
                s.GameMode = GameModes.Normalize(r.ReadString());
                s.Started = r.ReadBoolean();
            }
            else
            {
                s.Started = true; // worlds from v0.2 were already being played
            }
            if (version >= 3)
            {
                s.PdaLog = ReadSet(r);
                int n = ReadCount(r);
                for (int i = 0; i < n; i++) s.Fragments[r.ReadString()] = r.ReadInt32();
                n = ReadCount(r);
                for (int i = 0; i < n; i++) s.Structures[r.ReadString()] = Bytes.Read(r);
                n = ReadCount(r);
                for (int i = 0; i < n; i++)
                {
                    var id = r.ReadString();
                    int items = ReadCount(r);
                    var list = new List<byte[]>(items);
                    for (int j = 0; j < items; j++) list.Add(Bytes.Read(r));
                    s.Containers[id] = list;
                }
            }
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
                WorldId = WorldId,
                GameMode = GameMode,
                Started = Started,
                PdaLog = new HashSet<string>(PdaLog),
                Fragments = new Dictionary<string, int>(Fragments),
                Structures = new Dictionary<string, byte[]>(Structures),        // blobs are never mutated, sharing is fine
                Containers = Containers.ToDictionary(kv => kv.Key, kv => new List<byte[]>(kv.Value)),
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
                if (version < 1 || version > FileVersion) throw new InvalidDataException("Unsupported world file version " + version);
                return Read(r, version);
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
