using System;
using SubnauticaMP.Shared;
using UnityEngine;

namespace SubnauticaMP
{
    // Another player: a real diver model (copied from your own body, animated) once you're in the world,
    // or a colored capsule until then / if the copy fails.
    // Movement is replayed a tiny bit in the past on the sender's own clock (snapshot interpolation): even when
    // their game hitches (turning into new terrain) or the network bunches packets up, they glide evenly.
    public sealed class RemotePlayer : MonoBehaviour
    {
        const float SnapDistance = 30f; // teleports / respawns: jump instead of gliding across the map
        const float InterpDelay = 0.12f;   // how far in the past they're shown (a bit over 2 updates)
        const float MaxExtrapolate = 0.3f; // updates late: keep them going this long in the same direction
        const int MaxSnaps = 32;

        struct Snap { public float T; public Vector3 P; public Quaternion R; }
        readonly System.Collections.Generic.List<Snap> _snaps = new System.Collections.Generic.List<Snap>(MaxSnaps + 1);
        float _clockOffset; // their clock minus ours (the least delayed estimate)

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
        EmoteAnimator _emoteAnim;
        Emote _emote = Emote.None;
        float _emoteStarted;
        Vector3 _knockDir = Vector3.back;
        Ragdoll _ragdoll;
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

        // Colors the orange / yellow parts of their suit.
        void Tint()
        {
            if (_diver == null) return;
            try { SuitPaint.Apply(_diver, SuitColor); }
            catch (Exception e) { Game.WarnOnce("tint", "Couldn't color a suit: " + e.GetBaseException().Message); }
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

            float now = Time.unscaledTime;
            float sent = state.SentAt > 0f ? state.SentAt : now; // older versions don't send their clock
            bool teleported = !_hasTarget || Vector3.Distance(_lastTargetPos, _targetPos) > SnapDistance;
            if (teleported || (_snaps.Count > 0 && sent < _snaps[_snaps.Count - 1].T - 1f)) _snaps.Clear(); // jumped / their game restarted
            if (_snaps.Count == 0 || sent > _snaps[_snaps.Count - 1].T)
            {
                // how fast they're going, for the swim animation (on their clock: hitches don't fake speed bursts)
                if (_snaps.Count > 0)
                {
                    var last = _snaps[_snaps.Count - 1];
                    _velocity = Vector3.Lerp(_velocity, (_targetPos - last.P) / Mathf.Max(0.02f, sent - last.T), 0.5f);
                }
                _snaps.Add(new Snap { T = sent, P = _targetPos, R = _targetRot });
                if (_snaps.Count > MaxSnaps) _snaps.RemoveAt(0);
                float offset = sent - now;
                if (_snaps.Count == 1 || offset > _clockOffset || offset < _clockOffset - 1f) _clockOffset = offset;
                else _clockOffset -= 0.002f; // drift slowly back so a single early packet doesn't count forever
            }
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

        public Emote CurrentEmote => _emote;
        public bool Visible => _visible;
        public Vector3 Chest => transform.position; // what a push aims at

        // Returns false when it's just a looping emote (dance...) being sent again to keep it going.
        public bool PlayEmote(Emote emote, Vector3? knockDirection = null)
        {
            if (knockDirection.HasValue) _knockDir = knockDirection.Value;
            // knocked over: a real physics ragdoll when we can, the animated fall if not
            if (emote == Emote.Knocked && Plugin.RealRagdoll.Value && _diver != null && _diver.activeSelf)
            {
                _ragdoll?.Remove();
                _ragdoll = Ragdoll.Start(_diver, transform, _knockDir * 5f + Vector3.up * 1.5f, _underwater);
                if (_ragdoll != null)
                {
                    _emoteAnim?.ResetNow();
                    _emote = emote;
                    _emoteStarted = Time.unscaledTime;
                    return true;
                }
            }
            if (emote == Emote.None) { StopEmote(); return false; }
            bool repeat = emote == _emote && Emotes.Loops(emote) && EmoteRunning();
            if (!repeat) _emoteStarted = Time.unscaledTime;
            _emote = emote;
            try { _emoteAnim?.Play(emote); _emoteAnim?.SetKnockDirection(_knockDir); }
            catch (Exception e) { Game.WarnOnce("emote", "Couldn't play emote: " + e.GetBaseException().Message); }
            return !repeat;
        }

        public void StopEmote()
        {
            _emote = Emote.None;
            _emoteAnim?.Stop();
        }

        bool EmoteRunning()
        {
            var info = Emotes.Get(_emote);
            return info != null && (info.Seconds <= 0f || Time.unscaledTime - _emoteStarted < info.Seconds);
        }

        void LateUpdate()
        {
            if (_ragdoll != null)
            {
                if (!_ragdoll.LateUpdate(Time.unscaledDeltaTime)) _ragdoll = null;
                return;
            }
            if (_emoteAnim == null || _diver == null || !_diver.activeSelf) return;
            try { _emoteAnim.Apply(Time.unscaledDeltaTime); }
            catch (Exception e)
            {
                Game.WarnOnce("emote", "Emotes turned off for " + PlayerName + ": " + e.GetBaseException().Message);
                _emoteAnim = null;
            }
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
                    try { _emoteAnim = new EmoteAnimator(_diver); }
                    catch (Exception e) { Game.WarnOnce("emote", "No emotes on divers: " + e.GetBaseException().Message); }
                    if (_emote != Emote.None && EmoteRunning()) _emoteAnim?.Play(_emote);
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

            // dancing / chilling stops once they swim off or get in a vehicle
            if (_emote != Emote.None && (!EmoteRunning() || !_visible ||
                (Emotes.Loops(_emote) && Time.unscaledTime - _emoteStarted > 0.6f && _velocity.magnitude > 1.5f)))
                StopEmote();

            if (_anim != null && _diver != null && _diver.activeSelf)
            {
                if (Time.unscaledTime - _lastTargetTime > 0.5f) _velocity = Vector3.zero; // stopped sending = standing still
                _anim.Update(transform, _velocity, _underwater, Time.unscaledDeltaTime);
            }
            if (_ragdoll != null) return; // lying where physics put them; catches up after getting up
            var pos = _targetPos;
            var rot = _targetRot;
            float k = 12f;
            if (_subId.Length == 0 && _snaps.Count > 0)
            {
                Sample(Time.unscaledTime + _clockOffset - InterpDelay, out pos, out rot);
                k = 30f; // already smooth: only soften the small fixes after guessing ahead
            }
            float t = 1f - Mathf.Exp(-k * Time.unscaledDeltaTime);
            transform.position = Vector3.Lerp(transform.position, pos, t);
            transform.rotation = Quaternion.Slerp(transform.rotation, rot, t);
        }

        // Where they were at `time` on their clock: between the two updates around it, or guessed a little ahead.
        void Sample(float time, out Vector3 pos, out Quaternion rot)
        {
            int n = _snaps.Count;
            var first = _snaps[0];
            if (n == 1 || time <= first.T) { pos = first.P; rot = first.R; return; }
            var last = _snaps[n - 1];
            if (time >= last.T)
            {
                var prev = _snaps[n - 2];
                var vel = (last.P - prev.P) / Mathf.Max(0.02f, last.T - prev.T);
                pos = last.P + vel * Mathf.Min(time - last.T, MaxExtrapolate);
                rot = last.R;
                return;
            }
            for (int i = n - 1; i > 0; i--)
            {
                var a = _snaps[i - 1];
                if (a.T > time) continue;
                var b = _snaps[i];
                float f = Mathf.Clamp01((time - a.T) / Mathf.Max(0.0001f, b.T - a.T));
                pos = Vector3.LerpUnclamped(a.P, b.P, f);
                rot = Quaternion.Slerp(a.R, b.R, f);
                return;
            }
            pos = first.P; rot = first.R;
        }
    }
}
