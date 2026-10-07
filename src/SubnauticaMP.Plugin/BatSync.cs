using System.Collections.Generic;
using System.Linq;
using SubnauticaMP.Shared;
using UnityEngine;

namespace SubnauticaMP
{
    // The Titanium Bat in your hands: click to swing (your arms are posed by BatPose, everyone sees the swing),
    // and if a teammate is in front of you when it connects they get launched: far along where you're looking,
    // straight up if you're looking up. Their own game flies them (PushSync.LaunchMe), with a ragdoll.
    internal sealed class BatSync
    {
        const float HitStopSeconds = 0.07f; // the swing freezes this long when it connects: feels heavy
        const float SwingAgainAfter = 0.55f; // can start the next swing once this far into one
        const float LocalTwist = 0.4f;      // first person: only a little chest turn (your view of your arms)

        readonly Session _s;
        static bool _hooked, _viaLoop;
        BatPose _pose;
        Transform _poseBody;
        bool _holding, _pda;
        float _heldCheck;
        object _rightHand; // GameInput.Button.RightHand (the "use tool" button, left mouse by default)
        bool _inputLooked;

        public BatSync(Session s)
        {
            _s = s;
            if (!_hooked)
            {
                _hooked = true;
                AfterLateUpdate.Run += () => Session.Instance?.Batting?.PoseLocal();
                _viaLoop = AfterLateUpdate.Install();
            }
        }

        public void Reset()
        {
            _pose?.Cancel();
            _holding = false;
        }

        public void Update()
        {
            if (!Game.InWorld) { _holding = false; return; }
            if (Time.unscaledTime >= _heldCheck)
            {
                _heldCheck = Time.unscaledTime + 0.15f;
                try { _holding = BatItem.Registered && BatItem.IsBat(Game.HeldTech()); } catch { _holding = false; }
                try { _pda = _holding && PlayerLooks.ReadAnim().Contains("using_pda"); } catch { _pda = false; }
            }
            if (_pose == null) return; // made on the first frame you hold it (PoseLocal)

            // the swing reached these moments last frame
            if (_pose.JustStruck) Whoosh();
            if (_pose.JustContacted) TryHit();

            if (!_holding || !CanSwing() || !SwingPressed()) return;
            if (_pose.Swinging && _pose.SwingTime < SwingAgainAfter) return;
            var cam = Game.Camera;
            if (cam == null) return;
            _pose.Swing();
            if (_s.Joined) _s.Send(new BatSwingPacket { Pitch = Pitch(cam.transform) });
        }

        public void LateUpdate()
        {
            if (!_viaLoop) PoseLocal(); // couldn't hook the end of the frame: next best
        }

        static float Pitch(Transform cam) => -Mathf.DeltaAngle(0f, cam.eulerAngles.x); // + = looking up

        bool CanSwing()
        {
            var player = Game.LocalPlayer;
            if (player == null || Game.PlayerVehicle(player) != null || Game.IsPiloting(player)) return false;
            if (!Game.LockCursor || UiKit.Typing() || _s.Wheel.IsOpen || _s.Pushing.Knocked || _s.Emoting.SelfView) return false;
            return !_pda;
        }

        bool SwingPressed()
        {
            if (!_inputLooked)
            {
                _inputLooked = true;
                var buttons = Game.GameInput?.GetNestedType("Button");
                if (buttons != null) try { _rightHand = System.Enum.Parse(buttons, "RightHand"); } catch { }
            }
            if (_rightHand != null && Game.Call(Game.GameInput, null, "GetButtonDown", _rightHand) is bool b) return b;
            return Input.GetMouseButtonDown(0);
        }

        // ---------- your arms ----------

        void PoseLocal()
        {
            var player = Game.LocalPlayer;
            if (player == null || !Game.InWorld) return;
            var body = player.transform.Find("body");
            if (body == null) return;
            if (_pose == null || _poseBody != body)
            {
                if (!_holding) return; // nothing to do until you take the bat out
                _poseBody = body;
                _pose = new BatPose(body.gameObject);
                if (!_pose.Valid) Game.WarnOnce("batpose", "Couldn't find your arm bones: the bat won't swing visibly");
            }
            var cam = Game.Camera;
            if (cam == null) return;
            bool show = _holding && Game.PlayerVehicle(player) == null && !_s.Pushing.Knocked && !_s.Emoting.SelfView && !_pda;
            _pose.Apply(Time.deltaTime, show, cam.transform.rotation, LocalTwist);
        }

        // ---------- hitting ----------

        void Whoosh()
        {
            var cam = Game.Camera;
            if (cam == null) return;
            bool underwater = Game.LocalPlayer != null && Game.Is(Game.LocalPlayer, "IsUnderwater");
            BatItem.PlaySound(underwater ? BatItem.Sound.MissWater : BatItem.Sound.MissAir, cam.transform.position + cam.transform.forward);
        }

        void TryHit()
        {
            if (!_s.Joined) return;
            var cam = Game.Camera;
            var player = Game.LocalPlayer;
            if (cam == null || player == null) return;
            var eye = cam.transform.position;
            var look = cam.transform.forward;
            var hits = new List<(RemotePlayer r, float d)>();
            foreach (var r in _s.RemotePlayers)
            {
                if (r == null || !r.gameObject.activeInHierarchy || !r.Visible || r.KnockedDown) continue;
                var to = r.Chest - eye;
                float d = to.magnitude;
                if (d > Bat.Reach || d < 0.05f || Vector3.Angle(look, to) > Bat.Cone) continue;
                if (Blocked(eye, r.Chest, player.transform)) continue; // not through walls
                hits.Add((r, d));
            }
            if (hits.Count == 0) return;
            _pose?.HitStop(HitStopSeconds);
            BatItem.PlaySound(BatItem.Sound.Hit, hits[0].r.Chest);
            var targets = hits.OrderBy(h => h.d).Take(Bat.MaxTargets).Select(h => h.r.Id).ToList();
            _s.Send(new BatHitPacket { Targets = targets, Direction = new Vec3(look.x, look.y, look.z) });
        }

        static bool Blocked(Vector3 from, Vector3 to, Transform me)
        {
            var dir = to - from;
            foreach (var h in Physics.RaycastAll(from, dir.normalized, dir.magnitude, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.transform.IsChildOf(me)) continue;
                if (h.rigidbody != null && h.rigidbody.GetComponentInParent<RemotePlayer>() != null) continue; // their ragdoll
                if (h.distance > dir.magnitude - 0.4f) continue; // something right at them (their own body)
                return true;
            }
            return false;
        }

        // ---------- from the server ----------

        public void OnSwing(BatSwingPacket p)
        {
            var r = _s.RemotePlayers.FirstOrDefault(x => x != null && x.Id == p.Id);
            r?.SwingBat(p.Pitch);
        }

        public void OnHit(BatHitPacket p)
        {
            var look = new Vector3(p.Direction.X, p.Direction.Y, p.Direction.Z);
            if (look.sqrMagnitude < 1e-4f) return;
            bool homeRun = Bat.HomeRun(p.Direction);
            string hitter = _s.NameOf(p.HitterId);
            var names = new List<string>();
            foreach (var id in p.Targets)
            {
                if (id == _s.LocalId)
                {
                    var player = Game.LocalPlayer;
                    if (player == null) continue;
                    var v = Bat.LaunchVelocity(p.Direction, Game.Is(player, "IsUnderwater"), Game.Is(player, "IsInSub"));
                    if (_s.Pushing.LaunchMe(new Vector3(v.X, v.Y, v.Z)))
                        _s.AddChat(homeRun ? $"HOME RUN! {hitter} sent you flying!" : $"{hitter} launched you with a bat!");
                    continue;
                }
                var r = _s.RemotePlayers.FirstOrDefault(x => x != null && x.Id == id);
                if (r == null) continue;
                r.Launch(p.Direction);
                names.Add(r.PlayerName);
                if (p.HitterId != _s.LocalId) BatItem.PlaySound(BatItem.Sound.Hit, r.Chest);
            }
            if (p.HitterId == _s.LocalId && names.Count > 0)
                _s.AddChat((homeRun ? "HOME RUN! You launched " : "You launched ") + string.Join(", ", names.ToArray()) + "!");
        }
    }
}
