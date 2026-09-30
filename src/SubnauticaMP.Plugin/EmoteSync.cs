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
        Vector3 _startPos;

        public EmoteSync(Session s) { _s = s; }

        public Emote Local => _local;

        public void Reset()
        {
            _local = Emote.None;
            EndSelfView();
        }

        public void Play(Emote emote)
        {
            if (!_s.Joined) return;
            if (emote == Emote.None) { Stop(); return; }
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
            _s.Send(new EmotePacket { Emote = emote });
            StartSelfView(emote);
            _s.AddChat($"* {Plugin.PlayerName.Value} {info.Did}" + (info.Seconds <= 0f ? " (move to stop)" : ""));
        }

        public void Stop()
        {
            if (_local == Emote.None) return;
            _local = Emote.None;
            if (_s.Joined) _s.Send(new EmotePacket { Emote = Emote.None });
        }

        public void Update()
        {
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
                _s.Send(new EmotePacket { Emote = _local });
            }
        }

        public void OnEmote(EmotePacket packet)
        {
            var remote = _s.RemotePlayers.FirstOrDefault(r => r != null && r.Id == packet.Id);
            if (remote == null) return;
            if (packet.Emote == Emote.None) { remote.StopEmote(); return; }
            var info = Emotes.Get(packet.Emote);
            if (info == null) return;
            if (remote.PlayEmote(packet.Emote)) _s.AddChat($"* {remote.PlayerName} {info.Did}");
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
            if (_self == null) return;
            if (_selfBody == null) { _self = null; _view = 0f; ShowHead(false); return; }
            if (_local == Emote.None) _self.Stop();
            _self.Apply(Time.unscaledDeltaTime);
            _view = Mathf.MoveTowards(_view, _self.Current != Emote.None ? 1f : 0f, Time.unscaledDeltaTime / CamBlendSeconds);
            if (_view <= 0f && !_self.Showing) { ShowHead(false); return; }

            // where the camera goes: behind and a bit above you, looking at you, not through walls
            var cam = Game.Camera;
            if (cam == null) return;
            var target = _self.Middle + Vector3.up * 0.25f;
            var flat = Vector3.ProjectOnPlane(_selfBody.forward, Vector3.up);
            if (flat.sqrMagnitude < 0.01f) flat = cam.transform.forward;
            var back = (-flat.normalized * 0.9f + Vector3.up * 0.35f).normalized;
            float dist = CamDistance;
            if (Physics.SphereCast(target, 0.2f, back, out var hit, CamDistance, ~0, QueryTriggerInteraction.Ignore)
                && !hit.transform.IsChildOf(Game.LocalPlayer.transform))
                dist = Mathf.Max(0.6f, hit.distance - 0.1f);
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
            if (me == null || me._view <= 0f || me._self == null || cam != Game.Camera) return;
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
}
