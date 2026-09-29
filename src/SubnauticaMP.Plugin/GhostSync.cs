using System.Collections.Generic;
using SubnauticaMP.Shared;
using UnityEngine;

namespace SubnauticaMP
{
    // The see-through hologram you get while placing something with the Habitat Builder, shown to everyone.
    internal sealed class GhostSync
    {
        const float SendSeconds = 0.15f;

        sealed class Ghost
        {
            public string Tech = "";
            public GameObject Go;
            public int Request;
            public Vector3 Pos;
            public Quaternion Rot = Quaternion.identity;
        }

        readonly Session _s;
        readonly Dictionary<int, Ghost> _ghosts = new Dictionary<int, Ghost>();
        Material _material;
        float _timer, _lastForced;
        string _sentTech = "";
        Vector3 _sentPos;
        Quaternion _sentRot;

        public GhostSync(Session s) { _s = s; }

        public void Reset()
        {
            foreach (var g in _ghosts.Values) if (g.Go != null) Object.Destroy(g.Go);
            _ghosts.Clear();
            _sentTech = "";
        }

        public void Remove(int playerId)
        {
            if (!_ghosts.TryGetValue(playerId, out var g)) return;
            if (g.Go != null) Object.Destroy(g.Go);
            _ghosts.Remove(playerId);
        }

        public void Update()
        {
            // other players' ghosts glide to where they are
            float k = 1f - Mathf.Exp(-15f * Time.unscaledDeltaTime);
            foreach (var g in _ghosts.Values)
            {
                if (g.Go == null) continue;
                g.Go.transform.position = Vector3.Lerp(g.Go.transform.position, g.Pos, k);
                g.Go.transform.rotation = Quaternion.Slerp(g.Go.transform.rotation, g.Rot, k);
            }

            if (!_s.InWorldAndSettled || Game.Builder == null) return;
            _timer += Time.unscaledDeltaTime;
            if (_timer < SendSeconds) return;
            _timer = 0f;

            bool placing = Game.TryGet(Game.Builder, null, "isPlacing") is bool b && b;
            var model = placing ? Game.TryGet(Game.Builder, null, "GetGhostModel") as GameObject : null;
            if (model == null)
            {
                if (_sentTech.Length > 0) { _sentTech = ""; _s.Send(new BuildGhostPacket { TechType = "" }); }
                return;
            }

            var tech = Game.TryGet(Game.Builder, null, "constructableTechType")?.ToString() ?? "";
            var pos = model.transform.position;
            var rot = model.transform.rotation;
            bool moved = tech != _sentTech || (pos - _sentPos).sqrMagnitude > 0.0025f || Quaternion.Angle(rot, _sentRot) > 1f;
            if (!moved && Time.unscaledTime - _lastForced < 2f) return;
            _lastForced = Time.unscaledTime;
            _sentTech = tech;
            _sentPos = pos;
            _sentRot = rot;
            _s.Send(new BuildGhostPacket
            {
                TechType = tech,
                Position = new Vec3(pos.x, pos.y, pos.z),
                Rotation = new Quat(rot.x, rot.y, rot.z, rot.w),
                CanPlace = Game.TryGet(Game.Builder, null, "canPlace") is bool can && can,
            });
        }

        public void OnGhost(BuildGhostPacket p)
        {
            if (!_ghosts.TryGetValue(p.Id, out var g)) _ghosts[p.Id] = g = new Ghost();
            g.Pos = new Vector3(p.Position.X, p.Position.Y, p.Position.Z);
            g.Rot = new Quaternion(p.Rotation.X, p.Rotation.Y, p.Rotation.Z, p.Rotation.W);
            if (p.TechType == g.Tech)
            {
                Tint(g.Go, p.CanPlace);
                return;
            }

            g.Tech = p.TechType ?? "";
            if (g.Go != null) Object.Destroy(g.Go);
            g.Go = null;
            int request = ++g.Request;
            if (g.Tech.Length == 0) return;

            bool canPlace = p.CanPlace;
            _s.StartCoroutine(Game.LoadPrefab(g.Tech, prefab =>
            {
                if (prefab == null || request != g.Request) return;
                try
                {
                    var prop = DiverModel.MakeProp(prefab);
                    prop.name = "SubnauticaMP_BuildGhost";
                    prop.transform.SetParent(null, false);
                    prop.transform.SetPositionAndRotation(g.Pos, g.Rot);
                    MakeHologram(prop);
                    Tint(prop, canPlace);
                    prop.SetActive(true);
                    g.Go = prop;
                }
                catch (System.Exception e) { Game.WarnOnce("ghost", "Couldn't show a build hologram: " + e.GetBaseException().Message); }
            }));
        }

        void MakeHologram(GameObject go)
        {
            if (_material == null)
            {
                var src = Game.TryGet(Game.Builder, null, "originalGhostStructureMaterial") as Material;
                _material = src != null ? new Material(src) : null;
            }
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (_material == null) { r.enabled = false; continue; }
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = _material;
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        static readonly Color Ok = new Color(0.3f, 1f, 0.6f, 0.5f), Blocked = new Color(1f, 0.3f, 0.3f, 0.5f);

        static void Tint(GameObject go, bool canPlace)
        {
            if (go == null) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var block = new MaterialPropertyBlock();
                r.GetPropertyBlock(block);
                block.SetColor("_Tint", canPlace ? Ok : Blocked);
                block.SetColor("_Color", canPlace ? Ok : Blocked);
                r.SetPropertyBlock(block);
            }
        }
    }
}
