using System.Collections.Generic;
using System.Linq;
using UnityEngine.Rendering;
using SubnauticaMP.Shared;
using UnityEngine;

namespace SubnauticaMP
{
    // Emotes: G (or "/e wave" in chat) plays one, everyone else's game animates your diver.
    // You can't see your own body in first person, so while your emote plays the camera swings out behind
    // you (third person) and your own diver does it too.
    internal sealed class EmoteSync
    {
        const float ResendSeconds = 4f;   // looping emotes: so people who join late see them too
        const float Cooldown = 0.35f;
        const float MoveToStop = 1.5f;    // dancing ends once you swim this far

        readonly Session _s;
        Emote _local = Emote.None;
        float _started, _nextSend, _cooldownUntil;
        double _clockStarted = double.NaN; // game clock when your emote started: everyone dances on the same frame
        Vector3 _startPos;

        public EmoteSync(Session s)
        {
            _s = s;
            EmoteAnimator.PartyNow = PartyClip;
        }

        public Emote Local => _local;

        public void Reset()
        {
            _local = Emote.None;
            EndSelfView();
            EndSelfRagdoll();
            _party = null;
            PartyLights(false);
        }

        public void Play(Emote emote)
        {
            if (!_s.Joined) return;
            if (emote == Emote.None) { Stop(); return; }
            if (emote == Emote.Party && _party == null) { StartParty(); return; }
            var info = Emotes.Get(emote);
            if (info == null || Time.unscaledTime < _cooldownUntil) return;
            var player = Game.LocalPlayer;
            if (player != null && Game.PlayerVehicle(player) != null)
            {
                _s.AddChat("Get out of the vehicle to do emotes (nobody can see you in there).");
                return;
            }
            _cooldownUntil = Time.unscaledTime + Cooldown;
            _local = emote;
            _started = Time.unscaledTime;
            _nextSend = _started + ResendSeconds;
            _startPos = PlayerPosition() ?? Vector3.zero;
            _clockStarted = Game.GetTime() ?? double.NaN;
            _s.Send(new EmotePacket { Emote = emote, StartTime = _clockStarted });
            StartSelfView(emote);
            _s.AddChat($"* {Plugin.PlayerName.Value} {info.Did}" + (info.Seconds <= 0f ? " (move to stop)" : ""));
        }

        // Just on your own screen (a push knocked you over: everyone else already knows from the push).
        public void PlayLocalOnly(Emote emote, Vector3 knockDirection, Vector3? ragdollVelocity = null)
        {
            _local = emote;
            _started = Time.unscaledTime;
            if (ragdollVelocity.HasValue && StartSelfRagdoll(ragdollVelocity.Value)) return;
            StartSelfView(emote);
            _self?.SetKnockDirection(knockDirection);
        }

        // ---------- your own ragdoll: a copy of you flops over, the camera watches it, your body hides meanwhile ----------

        Ragdoll _selfRagdoll;
        GameObject _selfRagdollRoot;
        readonly List<Renderer> _hiddenBody = new List<Renderer>();

        bool StartSelfRagdoll(Vector3 velocity)
        {
            if (!Plugin.EmoteCamera.Value || !Plugin.RealRagdoll.Value) return false;
            var player = Game.LocalPlayer;
            var body = player != null ? player.transform.Find("body") : null;
            var cam = Game.Camera;
            if (body == null || cam == null) return false;
            EndSelfRagdoll();
            try
            {
                _selfRagdollRoot = new GameObject("SubnauticaMP_SelfRagdoll");
                _selfRagdollRoot.transform.SetPositionAndRotation(player.transform.position, Quaternion.Euler(0f, cam.transform.eulerAngles.y, 0f));
                var copy = DiverModel.Create(_selfRagdollRoot.transform);
                if (copy == null) { EndSelfRagdoll(); return false; }
                copy.transform.localPosition = body.localPosition;
                Game.TryDo("ragdoll color", () => SuitPaint.Apply(copy, Plugin.DiverColor.Value));
                _selfRagdoll = Ragdoll.Start(copy, _selfRagdollRoot.transform, velocity, Game.Is(player, "IsUnderwater"));
                if (_selfRagdoll == null) { EndSelfRagdoll(); return false; }
                foreach (var r in body.GetComponentsInChildren<Renderer>(true))
                    if (r.enabled) { r.enabled = false; _hiddenBody.Add(r); }
                HookCamera();
                return true;
            }
            catch (System.Exception e)
            {
                Game.WarnOnce("selfragdoll", "Couldn't ragdoll you: " + e.GetBaseException().Message);
                EndSelfRagdoll();
                return false;
            }
        }

        // Your real body goes where your ragdoll went, so you stand up where you landed (not back where you were
        // pushed). While lying it follows sideways (and up/down in water); at the end it takes the exact spot.
        void FollowRagdoll(bool final)
        {
            var player = Game.LocalPlayer;
            if (player == null || _selfRagdoll == null || _selfRagdollRoot == null) return;
            var t = player.transform;
            var rb = Game.TryGet(Game.Player, player, "rigidBody") as Rigidbody ?? player.GetComponent<Rigidbody>();
            Vector3 target;
            if (final) target = _selfRagdollRoot.transform.position; // moved to the landing spot when it got up
            else
            {
                var chest = _selfRagdoll.Focus.position;
                target = Game.Is(player, "IsUnderwater") ? chest - Vector3.up * 0.8f : new Vector3(chest.x, t.position.y, chest.z);
            }
            t.position = target;
            if (rb != null)
            {
                rb.position = target;
                if (!rb.isKinematic) rb.velocity = Vector3.zero;
            }
        }

        void EndSelfRagdoll()
        {
            _selfRagdoll?.Remove();
            _selfRagdoll = null;
            if (_selfRagdollRoot != null) Object.Destroy(_selfRagdollRoot);
            _selfRagdollRoot = null;
            foreach (var r in _hiddenBody) if (r != null) r.enabled = true;
            _hiddenBody.Clear();
            if (_self == null) _view = 0f;
        }

        void HookCamera()
        {
            _active = this;
            if (_hooked) return;
            _hooked = true;
            Camera.onPreCull += PreCull;
            Camera.onPostRender += PostRender;
        }

        public void Stop()
        {
            if (_local == Emote.None) return;
            _local = Emote.None;
            if (_s.Joined) _s.Send(new EmotePacket { Emote = Emote.None });
        }

        public void Update()
        {
            if (_local == Emote.Party && _party == null) Stop();
            if (_local == Emote.None) return;
            var info = Emotes.Get(_local);
            float now = Time.unscaledTime;
            if (info == null) { _local = Emote.None; return; }
            if (info.Seconds > 0f)
            {
                if (now - _started >= info.Seconds) _local = Emote.None; // finished by itself everywhere
                return;
            }

            var player = Game.LocalPlayer;
            var pos = PlayerPosition();
            if (player == null || pos == null || (pos.Value - _startPos).magnitude > MoveToStop || Game.PlayerVehicle(player) != null)
            {
                Stop();
                return;
            }
            if (now >= _nextSend)
            {
                _nextSend = now + ResendSeconds;
                _s.Send(new EmotePacket { Emote = _local, StartTime = _clockStarted });
            }
        }

        public void OnEmote(EmotePacket packet)
        {
            var remote = _s.RemotePlayers.FirstOrDefault(r => r != null && r.Id == packet.Id);
            if (remote == null) return;
            if (packet.Emote == Emote.None) { remote.StopEmote(); return; }
            var info = Emotes.Get(packet.Emote);
            if (info == null) return;
            if (remote.PlayEmote(packet.Emote, startTime: packet.StartTime)) _s.AddChat($"* {remote.PlayerName} {info.Did}");
        }

        // How long ago (seconds) an emote started at this game-clock time. Null when we can't tell.
        public static float? Elapsed(double startTime)
        {
            var now = Game.GetTime();
            if (double.IsNaN(startTime) || now == null) return null;
            double t = now.Value - startTime;
            if (t < 0) return 0f;                       // clocks a hair apart
            return t < 3600 ? (float)t : (float?)null;  // way off = the clock got reset (new world)
        }

        // ---------- dance party ----------

        PartyPacket _party;
        GameObject _lights;
        readonly System.Random _rng = new System.Random();

        public bool PartyActive => _party != null;
        public bool LeadingParty => _party != null && _party.LeaderId == _s.LocalId;

        // Start one (you're the DJ), or end yours.
        public void StartParty()
        {
            if (!_s.Joined) return;
            var pos = PlayerPosition();
            if (pos == null) return;
            _s.Send(new PartyPacket
            {
                Active = true,
                Center = new Vec3(pos.Value.x, pos.Value.y, pos.Value.z),
                StartTime = Game.GetTime() ?? Time.unscaledTime,
                Seed = _rng.Next(1, int.MaxValue),
            });
        }

        public void EndParty()
        {
            if (_party != null) _s.Send(new PartyPacket { Active = false });
        }

        public void OnParty(PartyPacket p)
        {
            if (p.Active)
            {
                bool isNew = _party == null;
                _party = p;
                PartyLights(true);
                if (p.LeaderId == _s.LocalId)
                {
                    _local = Emote.None; // start dancing along
                    _cooldownUntil = 0f;
                    Play(Emote.Party);
                }
                else if (isNew) _s.AddChat($"{_s.NameOf(p.LeaderId)} started a dance party! Press {Plugin.EmoteKey.Value} and click the middle of the wheel to join.");
            }
            else
            {
                if (_party == null) return;
                _party = null;
                PartyLights(false);
                if (_local == Emote.Party) Stop();
                foreach (var r in _s.RemotePlayers)
                    if (r != null && r.CurrentEmote == Emote.Party) r.StopEmote();
                _s.AddChat("The dance party is over.");
            }
        }

        // Which dance the party is on right now and how far into it: the same on every PC.
        (EmoteClips.Clip clip, float time)? PartyClip()
        {
            var p = _party;
            if (p == null) return null;
            double t = (Game.GetTime() ?? Time.unscaledTime) - p.StartTime;
            if (t < 0) t = 0;
            var dance = Emotes.PartyDance(t, p.Seed);
            var clip = EmoteClips.Get(dance.Clip);
            if (clip == null) return null;
            return (clip, (float)(t % Emotes.PartySongSeconds));
        }

        public string PartyDanceName()
        {
            var p = _party;
            if (p == null) return null;
            double t = (Game.GetTime() ?? Time.unscaledTime) - p.StartTime;
            return Emotes.PartyDance(System.Math.Max(0, t), p.Seed).Label;
        }

        // Disco: colored lights swirling over the party spot.
        void PartyLights(bool on)
        {
            if (!on)
            {
                if (_lights != null) Object.Destroy(_lights);
                _lights = null;
                return;
            }
            if (_lights == null)
            {
                _lights = new GameObject("SubnauticaMP_PartyLights");
                Game.KeepAlive(_lights);
                var colors = new[] { new Color(1f, 0.2f, 0.6f), new Color(0.2f, 0.9f, 1f), new Color(1f, 0.85f, 0.2f), new Color(0.5f, 0.3f, 1f) };
                for (int i = 0; i < colors.Length; i++)
                {
                    var go = new GameObject("Disco" + i);
                    go.transform.SetParent(_lights.transform, false);
                    var light = go.AddComponent<Light>();
                    light.type = LightType.Point;
                    light.color = colors[i];
                    light.range = 14f;
                    light.intensity = 2.2f;
                }
                _lights.AddComponent<DiscoSpin>();
            }
            _lights.transform.position = new Vector3(_party.Center.X, _party.Center.Y + 3f, _party.Center.Z);
        }

        // ---------- seeing yourself ----------

        const float CamDistance = 2.8f, CamBlendSeconds = 0.35f;
        EmoteAnimator _self;
        Transform _selfBody;
        readonly List<Renderer> _head = new List<Renderer>();
        float _view;          // 0 = normal first person, 1 = fully behind you
        Vector3 _camPos, _savedPos;
        Quaternion _camRot, _savedRot;
        bool _moved;
        static bool _hooked; // once per game, even if the session object gets rebuilt
        static EmoteSync _active;

        void StartSelfView(Emote emote)
        {
            if (!Plugin.EmoteCamera.Value) return;
            var body = Game.LocalPlayer != null ? Game.LocalPlayer.transform.Find("body") : null;
            if (body == null) return;
            try
            {
                if (_selfBody != body || _self == null)
                {
                    _self?.ResetNow();
                    _selfBody = body;
                    _self = new EmoteAnimator(body.gameObject);
                }
                _self.Play(emote);
                ShowHead(true);
                _active = this;
                if (!_hooked)
                {
                    _hooked = true;
                    Camera.onPreCull += PreCull;
                    Camera.onPostRender += PostRender;
                }
            }
            catch (System.Exception e) { Game.WarnOnce("selfemote", "Can't show your own emote: " + e.GetBaseException().Message); }
        }

        // After the game has posed your body (LateUpdate).
        public void LateUpdate()
        {
            if (_selfRagdoll != null)
            {
                if (_selfRagdoll.LateUpdate(Time.unscaledDeltaTime))
                {
                    FollowRagdoll(false);
                    _view = Mathf.MoveTowards(_view, 1f, Time.unscaledDeltaTime / CamBlendSeconds);
                    var cam0 = Game.Camera;
                    if (cam0 != null) AimCamera(cam0, _selfRagdoll.Focus.position + Vector3.up * 0.2f, cam0.transform.forward, _selfRagdollRoot != null ? _selfRagdollRoot.transform : null);
                }
                else { FollowRagdoll(true); EndSelfRagdoll(); }
                return;
            }
            if (_self == null) return;
            if (_selfBody == null) { _self = null; _view = 0f; ShowHead(false); return; }
            if (_local == Emote.None) _self.Stop();
            _self.Apply(Time.unscaledDeltaTime);
            _view = Mathf.MoveTowards(_view, _self.Current != Emote.None ? 1f : 0f, Time.unscaledDeltaTime / CamBlendSeconds);
            if (_view <= 0f && !_self.Showing) { ShowHead(false); return; }

            var cam = Game.Camera;
            if (cam == null) return;
            AimCamera(cam, _self.Middle + Vector3.up * 0.25f, _selfBody.forward, null);
        }

        // Where the camera goes: behind and a bit above `target`, looking at it, not through walls.
        void AimCamera(Camera cam, Vector3 target, Vector3 facing, Transform ignore)
        {
            var flat = Vector3.ProjectOnPlane(facing, Vector3.up);
            if (flat.sqrMagnitude < 0.01f) flat = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
            if (flat.sqrMagnitude < 0.01f) flat = Vector3.forward;
            var back = (-flat.normalized * 0.9f + Vector3.up * 0.35f).normalized;
            float dist = CamDistance;
            var me = Game.LocalPlayer != null ? Game.LocalPlayer.transform : null;
            foreach (var hit in Physics.SphereCastAll(target, 0.2f, back, CamDistance, ~0, QueryTriggerInteraction.Ignore))
            {
                if (me != null && hit.transform.IsChildOf(me)) continue;
                if (ignore != null && hit.transform.IsChildOf(ignore)) continue;
                dist = Mathf.Min(dist, Mathf.Max(0.6f, hit.distance - 0.1f));
            }
            var outPos = target + back * dist;
            var outRot = Quaternion.LookRotation(target - outPos, Vector3.up);
            float t = _view * _view * (3f - 2f * _view);
            _camPos = Vector3.Lerp(cam.transform.position, outPos, t);
            _camRot = Quaternion.Slerp(cam.transform.rotation, outRot, t);
        }

        void EndSelfView()
        {
            _self?.ResetNow();
            _self = null;
            _view = 0f;
            ShowHead(false);
        }

        // In first person your head only casts a shadow; show it while you're looking at yourself.
        void ShowHead(bool show)
        {
            if (show)
            {
                if (_head.Count > 0 || _selfBody == null) return;
                foreach (var r in _selfBody.GetComponentsInChildren<Renderer>(true))
                    if (r.shadowCastingMode == ShadowCastingMode.ShadowsOnly) { _head.Add(r); r.shadowCastingMode = ShadowCastingMode.On; }
            }
            else
            {
                foreach (var r in _head) if (r != null) r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                _head.Clear();
            }
        }

        // Only for the moment the game's camera draws the world: the game itself never sees the camera move.
        static void PreCull(Camera cam)
        {
            var me = _active;
            if (me == null || me._view <= 0f || (me._self == null && me._selfRagdoll == null) || cam != Game.Camera) return;
            me._savedPos = cam.transform.position;
            me._savedRot = cam.transform.rotation;
            cam.transform.SetPositionAndRotation(me._camPos, me._camRot);
            me._moved = true;
        }

        static void PostRender(Camera cam)
        {
            var me = _active;
            if (me == null || !me._moved || cam != Game.Camera) return;
            cam.transform.SetPositionAndRotation(me._savedPos, me._savedRot);
            me._moved = false;
        }

        static Vector3? PlayerPosition()
        {
            var player = Game.LocalPlayer;
            return player != null ? player.transform.position : (Vector3?)null;
        }
    }

    // Swirls the party lights around and pulses them to a beat.
    internal sealed class DiscoSpin : MonoBehaviour
    {
        void Update()
        {
            float t = Time.unscaledTime;
            int i = 0;
            foreach (Transform child in transform)
            {
                float a = t * (1.2f + i * 0.35f) + i * Mathf.PI * 0.5f;
                child.localPosition = new Vector3(Mathf.Cos(a) * 3.5f, Mathf.Sin(t * 0.9f + i) * 1.2f, Mathf.Sin(a) * 3.5f);
                var light = child.GetComponent<Light>();
                if (light != null) light.intensity = 1.4f + 1.2f * Mathf.Abs(Mathf.Sin(t * 4.2f + i));
                i++;
            }
        }
    }
}
