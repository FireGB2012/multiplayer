using System;
using SubnauticaMP.Shared;
using UnityEngine;

namespace SubnauticaMP
{
    // Another player: a real diver model (copied from your own body, animated) once you're in the world,
    // or a colored capsule until then / if the copy fails.
    // Smoothly chases the latest position from the network so 20Hz updates don't look choppy.
    public sealed class RemotePlayer : MonoBehaviour
    {
        const float SnapDistance = 30f; // teleports / respawns: jump instead of gliding across the map

        public int Id { get; private set; }
        public string PlayerName { get; private set; }
        public byte Health { get; private set; }
        public byte Food { get; private set; }
        public byte Water { get; private set; }
        public bool HasVitals { get; private set; }
        public bool Exposed { get; private set; } // swimming in open water: creatures can go for them

        string _held = "";
        string _gear = "", _anims = "";
        public bool Sleeping { get; private set; }
        string _subId = "";
        Vector3 _localPos;
        Quaternion _localRot = Quaternion.identity;
        GameObject _heldModel;
        int _heldRequest;

        Vector3 _targetPos;
        Quaternion _targetRot = Quaternion.identity;
        bool _hasTarget;
        Renderer[] _renderers;       // capsule placeholder
        GameObject _capsule;
        GameObject _diver;
        DiverAnimator _anim;
        Vector3 _velocity, _lastTargetPos;
        float _lastTargetTime;
        bool _underwater = true, _visible = true;
        float _nextModelTry;

        public int SuitColor { get; private set; } = DiverColors.Default;

        // New name / suit color from the party lobby.
        public void SetProfile(string name, int color)
        {
            if (!string.IsNullOrEmpty(name) && name != PlayerName)
            {
                PlayerName = name;
                var ping = Game.PingInstance != null ? GetComponent(Game.PingInstance) : null;
                if (ping != null) Game.TryDo("ping label", () => Game.Call(Game.PingInstance, ping, "SetLabel", name));
            }
            if (color != SuitColor)
            {
                SuitColor = color;
                Tint();
            }
        }

        // Colors their suit (the game's shaders multiply textures by _Color).
        void Tint()
        {
            if (_diver == null) return;
            var c = new Color(((SuitColor >> 16) & 0xFF) / 255f, ((SuitColor >> 8) & 0xFF) / 255f, (SuitColor & 0xFF) / 255f, 1f);
            var block = new MaterialPropertyBlock();
            foreach (var r in _diver.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                r.GetPropertyBlock(block);
                block.SetColor("_Color", c);
                r.SetPropertyBlock(block);
            }
        }

        public static RemotePlayer Create(int id, string name, int color = DiverColors.Default)
        {
            var root = new GameObject("RemotePlayer_" + id);
            Game.KeepAlive(root);

            var capsule = new GameObject("Capsule");
            capsule.transform.SetParent(root.transform, false);
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Destroy(body.GetComponent<Collider>()); // ghosts shouldn't block anyone
            body.transform.SetParent(capsule.transform, false);
            body.transform.localScale = new Vector3(0.6f, 0.9f, 0.6f);

            var visor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(visor.GetComponent<Collider>());
            visor.transform.SetParent(capsule.transform, false);
            visor.transform.localPosition = new Vector3(0f, 0.55f, 0.28f);
            visor.transform.localScale = new Vector3(0.4f, 0.15f, 0.1f);
            visor.GetComponent<Renderer>().material.color = Color.yellow;

            var remote = root.AddComponent<RemotePlayer>();
            remote.Id = id;
            remote.PlayerName = name;
            remote.SuitColor = color;
            body.GetComponent<Renderer>().material.color = ColorFor(id);
            remote._capsule = capsule;
            remote._renderers = capsule.GetComponentsInChildren<Renderer>();
            // teammates show on your HUD like beacons, with their name and distance
            try { Game.AddPing(root, name); }
            catch (Exception e) { Game.WarnOnce("ping", "Couldn't add player marker: " + e.GetBaseException().Message); }

            root.SetActive(false); // hidden until we get their first position
            return remote;
        }

        static Color ColorFor(int id) => Color.HSVToRGB((id * 0.618034f) % 1f, 0.7f, 1f);

        public void SetTarget(PlayerStatePacket state)
        {
            _targetPos = new Vector3(state.Position.X, state.Position.Y, state.Position.Z);
            _targetRot = new Quaternion(state.Rotation.X, state.Rotation.Y, state.Rotation.Z, state.Rotation.W);

            // how fast they're going, for the swim animation
            float now = Time.unscaledTime;
            if (_hasTarget && now > _lastTargetTime)
                _velocity = Vector3.Lerp(_velocity, (_targetPos - _lastTargetPos) / (now - _lastTargetTime), 0.5f);
            _lastTargetPos = _targetPos;
            _lastTargetTime = now;
            _underwater = (state.Flags & PlayerFlags.Underwater) != 0;
            _subId = state.SubId ?? "";
            if (_subId.Length > 0)
            {
                _localPos = new Vector3(state.LocalPosition.X, state.LocalPosition.Y, state.LocalPosition.Z);
                _localRot = new Quaternion(state.LocalRotation.X, state.LocalRotation.Y, state.LocalRotation.Z, state.LocalRotation.W);
            }
            Health = state.Health; Food = state.Food; Water = state.Water;
            HasVitals = state.Health > 0 || state.Food > 0 || state.Water > 0;
            if ((state.Held ?? "") != _held) SetHeld(state.Held ?? "");
            if ((state.Gear ?? "") != _gear)
            {
                _gear = state.Gear ?? "";
                if (_diver != null) PlayerLooks.ApplyGear(_diver, _gear);
            }
            if ((state.Anim ?? "") != _anims)
            {
                _anims = state.Anim ?? "";
                _anim?.SetToolAnims(_anims);
            }
            Sleeping = (state.Flags & PlayerFlags.Sleeping) != 0;

            // In a vehicle their body is inside the seamoth/prawn; hide it so it doesn't poke out.
            _visible = (state.Flags & PlayerFlags.InVehicle) == 0;
            Exposed = _underwater && (state.Flags & (PlayerFlags.InVehicle | PlayerFlags.InBase)) == 0 && _subId.Length == 0;
            ApplyVisibility();

            if (!_hasTarget || Vector3.Distance(transform.position, _targetPos) > SnapDistance)
            {
                transform.SetPositionAndRotation(_targetPos, _targetRot);
            }
            _hasTarget = true;
            if (!gameObject.activeSelf) gameObject.SetActive(true);
        }

        void ApplyVisibility()
        {
            bool diver = _diver != null;
            if (_capsule != null) _capsule.SetActive(_visible && !diver);
            if (diver) _diver.SetActive(_visible);
        }

        void OnDestroy()
        {
            if (_diver != null) Destroy(_diver);
            if (_heldModel != null) Destroy(_heldModel);
        }

        // Puts a copy of whatever they're holding in the diver's right hand.
        void SetHeld(string tech)
        {
            _held = tech;
            if (_heldModel != null) Destroy(_heldModel);
            _heldModel = null;
            int request = ++_heldRequest;
            if (tech.Length == 0 || _diver == null) return;

            var hand = _diver.transform.Find(DiverModel.AttachPoint);
            if (hand == null) return;
            StartCoroutine(Game.LoadPrefab(tech, prefab =>
            {
                if (request != _heldRequest || prefab == null || hand == null) return; // they switched again meanwhile
                try
                {
                    var prop = DiverModel.MakeProp(prefab);
                    prop.transform.SetParent(hand, false);
                    prop.transform.localPosition = Vector3.zero;
                    prop.transform.localRotation = Quaternion.identity;
                    prop.SetActive(true);
                    _heldModel = prop;
                }
                catch (Exception e) { Game.WarnOnce("held", "Couldn't show held item: " + e.GetBaseException().Message); }
            }));
        }

        void Update()
        {
            // swap the capsule for a real diver once there's a local player to copy
            if (_diver == null && Time.unscaledTime >= _nextModelTry && Game.InWorld)
            {
                _nextModelTry = Time.unscaledTime + 5f;
                _diver = DiverModel.Create(transform);
                if (_diver != null)
                {
                    _anim = new DiverAnimator(_diver);
                    ApplyVisibility();
                    if (_held.Length > 0) SetHeld(_held); // they were already holding something
                    PlayerLooks.ApplyGear(_diver, _gear);
                    Tint();
                    _anim.SetToolAnims(_anims);
                }
            }

            if (!_hasTarget) return;

            // inside a Cyclops: follow our copy of that Cyclops, not their world position
            if (_subId.Length > 0)
            {
                var sub = Session.Instance != null ? Session.Instance.Vehicles.CyclopsTransform(_subId) : null;
                if (sub != null)
                {
                    _targetPos = sub.TransformPoint(_localPos);
                    _targetRot = sub.rotation * _localRot;
                }
            }

            if (_anim != null && _diver != null && _diver.activeSelf)
            {
                if (Time.unscaledTime - _lastTargetTime > 0.5f) _velocity = Vector3.zero; // stopped sending = standing still
                _anim.Update(transform, _velocity, _underwater, Time.unscaledDeltaTime);
            }
            float t = 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime);
            transform.position = Vector3.Lerp(transform.position, _targetPos, t);
            transform.rotation = Quaternion.Slerp(transform.rotation, _targetRot, t);
        }
    }
}
