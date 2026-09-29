using System.Collections.Generic;
using System.Linq;
using SubnauticaMP.Shared;
using UnityEngine;

namespace SubnauticaMP
{
    // Seamoths, Prawn suits and Cyclopses. Whoever is driving a vehicle "owns" it and streams its position;
    // everyone else's copy has physics off and follows along.
    internal sealed class VehicleSync
    {
        const float ScanSeconds = 2f;
        const float SpawnDelay = 8f;        // let the save finish loading its own vehicles first
        const float DrivingSendRate = 0.05f;
        const float ParkedSendRate = 1f;
        const float SnapDistance = 50f;

        sealed class Tracked
        {
            public VehicleInfo Info;
            public GameObject Go;
            public Vector3 TargetPos;
            public Quaternion TargetRot = Quaternion.identity;
            public bool HasTarget;
            public bool RemoteDriven;
            public bool Spawning;
            public float RetryAt;
            public float SendTimer;
            public Vector3 LastSentPos;
            public Quaternion LastSentRot;
        }

        readonly Session _s;
        readonly Dictionary<string, Tracked> _vehicles = new Dictionary<string, Tracked>();
        HashSet<string> _baseline; // vehicles already in our save before syncing
        readonly HashSet<int> _builtHere = new HashSet<int>(); // copies we spawned for other players' vehicles
        bool _worldIsNew;
        float _scanTimer, _worldTime;
        string _drivingId;

        public VehicleSync(Session s) { _s = s; }

        public void Reset()
        {
            foreach (var t in _vehicles.Values)
                if (t.Go != null && t.RemoteDriven) Game.SetRemoteDriven(t.Go, false);
            _vehicles.Clear();
            _builtHere.Clear();
            _baseline = null;
            _drivingId = null;
        }

        public void OnWelcome(WorldState w)
        {
            Reset();
            _worldIsNew = !w.HasTime;
            foreach (var v in w.Vehicles.Values) _vehicles[v.Id] = NewTracked(v.Clone());
        }

        Tracked NewTracked(VehicleInfo info) => new Tracked
        {
            Info = info,
            TargetPos = ToUnity(info.Position),
            TargetRot = ToUnity(info.Rotation),
            HasTarget = true,
        };

        public void Update()
        {
            if (!Game.InWorld)
            {
                // back in the menu: forget the GameObjects, they're gone
                _worldTime = 0f;
                _baseline = null;
                foreach (var t in _vehicles.Values) { t.Go = null; t.Spawning = false; }
                return;
            }
            if (!_s.InWorldAndSettled) return;
            _worldTime += Time.unscaledDeltaTime;

            _scanTimer += Time.unscaledDeltaTime;
            if (_scanTimer >= ScanSeconds || _baseline == null)
            {
                _scanTimer = 0f;
                Scan();
            }

            ClaimIfDriving();
            SendOwned();
        }

        public void LateUpdate()
        {
            float t = 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime);
            foreach (var v in _vehicles.Values)
            {
                if (!v.RemoteDriven || v.Go == null || !v.HasTarget) continue;
                Game.SetRemoteDriven(v.Go, true); // the game likes to turn physics back on
                var tr = v.Go.transform;
                if (Vector3.Distance(tr.position, v.TargetPos) > SnapDistance)
                    tr.SetPositionAndRotation(v.TargetPos, v.TargetRot);
                else
                    tr.SetPositionAndRotation(Vector3.Lerp(tr.position, v.TargetPos, t), Quaternion.Slerp(tr.rotation, v.TargetRot, t));
            }
        }

        void Scan()
        {
            bool first = _baseline == null;
            if (first) _baseline = new HashSet<string>();

            foreach (var go in Game.FindVehicles())
            {
                var id = Game.GetId(go);
                if (string.IsNullOrEmpty(id) || _builtHere.Contains(go.GetInstanceID())) continue;

                if (_vehicles.TryGetValue(id, out var known))
                {
                    if (known.Go == null) Attach(known, go);
                    continue;
                }

                if (first)
                {
                    _baseline.Add(id);
                    if (!_worldIsNew) continue; // someone else's world: keep our old save's vehicles to ourselves
                }
                else if (_baseline.Contains(id)) continue;

                Announce(go, id);
            }

            // Build anything the server knows about that isn't in our world yet.
            if (_worldTime < SpawnDelay) return;
            foreach (var t in _vehicles.Values)
            {
                if (t.Go != null || t.Spawning || Time.unscaledTime < t.RetryAt) continue;
                var existing = Game.FindById(t.Info.Id);
                if (existing != null) { Attach(t, existing); continue; }
                Spawn(t);
            }
        }

        void Announce(GameObject go, string id)
        {
            var tech = Game.TechTypeOf(go);
            if (tech == null) return;
            var info = new VehicleInfo
            {
                Id = id,
                TechType = tech,
                Position = ToShared(go.transform.position),
                Rotation = ToShared(go.transform.rotation),
                OwnerId = _s.LocalId,
            };
            _vehicles[id] = new Tracked { Info = info, Go = go };
            _s.Send(new VehicleSpawnedPacket { Vehicle = info.Clone() });
            Plugin.Log.LogInfo($"Shared {tech} {id}");
        }

        void Spawn(Tracked t)
        {
            t.Spawning = true;
            var info = t.Info;
            _s.StartCoroutine(Game.SpawnVehicle(info.TechType, ToUnity(info.Position), ToUnity(info.Rotation), info.Id, go =>
            {
                t.Spawning = false;
                if (go == null)
                {
                    t.RetryAt = Time.unscaledTime + 30f;
                    Game.WarnOnce("spawnfail:" + info.TechType, "Couldn't build a copy of " + info.TechType);
                    return;
                }
                if (!_vehicles.ContainsKey(info.Id)) { Object.Destroy(go); return; } // removed while we were building it
                _builtHere.Add(go.GetInstanceID());
                Attach(t, go);
                _s.AddChat($"{_s.NameOf(info.OwnerId)}'s {info.TechType} is here");
            }));
        }

        void Attach(Tracked t, GameObject go)
        {
            t.Go = go;
            SetDriven(t, t.Info.OwnerId != 0 && t.Info.OwnerId != _s.LocalId);
            if (t.RemoteDriven && t.HasTarget) go.transform.SetPositionAndRotation(t.TargetPos, t.TargetRot);
        }

        void SetDriven(Tracked t, bool remote)
        {
            t.RemoteDriven = remote;
            if (t.Go != null) Game.SetRemoteDriven(t.Go, remote);
        }

        void ClaimIfDriving()
        {
            var player = Game.LocalPlayer;
            GameObject driving = null;
            var vehicle = Game.PlayerVehicle(player);
            if (vehicle != null) driving = vehicle.gameObject;
            else
            {
                var sub = Game.PlayerSub(player);
                if (Game.IsCyclops(sub) && Game.IsPiloting(player)) driving = sub.gameObject;
            }

            _drivingId = driving != null ? Game.GetId(driving) : null;
            if (_drivingId == null || !_vehicles.TryGetValue(_drivingId, out var t)) return;
            if (t.Go == null) t.Go = driving;
            if (t.Info.OwnerId == _s.LocalId) return;

            t.Info.OwnerId = _s.LocalId;
            SetDriven(t, false);
            _s.Send(new VehicleOwnerPacket { Id = _drivingId });
        }

        void SendOwned()
        {
            float dt = Time.unscaledDeltaTime;
            foreach (var t in _vehicles.Values)
            {
                if (t.Info.OwnerId != _s.LocalId || t.Go == null) continue;
                t.SendTimer += dt;
                if (t.SendTimer < (t.Info.Id == _drivingId ? DrivingSendRate : ParkedSendRate)) continue;
                t.SendTimer = 0f;

                var tr = t.Go.transform;
                if ((tr.position - t.LastSentPos).sqrMagnitude < 0.0025f && Quaternion.Angle(tr.rotation, t.LastSentRot) < 0.5f) continue;
                t.LastSentPos = tr.position;
                t.LastSentRot = tr.rotation;
                t.Info.Position = ToShared(tr.position);
                t.Info.Rotation = ToShared(tr.rotation);
                _s.Send(new VehicleStatePacket { Id = t.Info.Id, Position = t.Info.Position, Rotation = t.Info.Rotation });
            }
        }

        // ---------- from the network ----------

        public void OnSpawned(VehicleInfo info)
        {
            if (string.IsNullOrEmpty(info.Id) || _vehicles.ContainsKey(info.Id)) return;
            _vehicles[info.Id] = NewTracked(info);
            _scanTimer = ScanSeconds; // build it on the next frame
        }

        public void OnState(VehicleStatePacket p)
        {
            if (!_vehicles.TryGetValue(p.Id ?? "", out var t) || t.Info.OwnerId == _s.LocalId) return;
            t.Info.Position = p.Position;
            t.Info.Rotation = p.Rotation;
            t.TargetPos = ToUnity(p.Position);
            t.TargetRot = ToUnity(p.Rotation);
            t.HasTarget = true;
        }

        public void OnOwner(string id, int ownerId)
        {
            if (!_vehicles.TryGetValue(id ?? "", out var t)) return;
            t.Info.OwnerId = ownerId;
            SetDriven(t, ownerId != 0 && ownerId != _s.LocalId);
            if (t.RemoteDriven && t.Go != null)
            {
                t.TargetPos = t.Go.transform.position; // don't yank it until their first update arrives
                t.TargetRot = t.Go.transform.rotation;
            }
        }

        public void OnRemoved(string id)
        {
            if (!_vehicles.TryGetValue(id ?? "", out var t)) return;
            _vehicles.Remove(id);
            if (t.Go != null)
            {
                Patches.ApplyingRemote = true;
                try { Object.Destroy(t.Go); }
                finally { Patches.ApplyingRemote = false; }
            }
        }

        public void OnLocalVehicleDestroyed(GameObject go)
        {
            var id = Game.GetId(go);
            if (string.IsNullOrEmpty(id) || !_vehicles.TryGetValue(id, out var t)) return;
            // Only the driver's (or an unowned) vehicle counts. If our copy of someone else's
            // vehicle dies, it just gets rebuilt on the next scan.
            if (t.Info.OwnerId != _s.LocalId && t.Info.OwnerId != 0) return;
            _vehicles.Remove(id);
            _s.Send(new VehicleRemovedPacket { Id = id });
        }

        static Vector3 ToUnity(Vec3 v) => new Vector3(v.X, v.Y, v.Z);
        static Quaternion ToUnity(Quat q)
        {
            var r = new Quaternion(q.X, q.Y, q.Z, q.W);
            return r.x == 0f && r.y == 0f && r.z == 0f && r.w == 0f ? Quaternion.identity : r;
        }
        static Vec3 ToShared(Vector3 v) => new Vec3(v.x, v.y, v.z);
        static Quat ToShared(Quaternion q) => new Quat(q.x, q.y, q.z, q.w);
    }
}
