using System.Linq;
using SubnauticaMP.Shared;
using UnityEngine;

namespace SubnauticaMP
{
    // Nothing in your hand + left click on a teammate right in front of you = shove.
    // They get knocked away, flop over (ragdoll), can't move for a few seconds, then stand back up.
    internal sealed class PushSync
    {
        const float Range = 2.8f;        // meters
        const float Cone = 40f;          // degrees off where you look
        const float Cooldown = 0.8f;
        const float Strength = 9f;       // m/s shove underwater
        const float LandStrength = 6f;

        readonly Session _s;
        float _nextPush, _knockedUntil;

        public PushSync(Session s) { _s = s; }

        public bool Knocked => Time.unscaledTime < _knockedUntil;

        public void Reset() => _knockedUntil = 0f;

        public void Update()
        {
            if (Knocked)
            {
                // flat on the floor: no swimming, looking or tools until you're up again
                Game.TryDo("block game input", () => Game.Set(Game.GameInput, null, "clearInputFrame", Time.frameCount + 1));
                return;
            }
            if (!_s.Joined || !Game.InWorld || !Input.GetMouseButtonDown(0)) return;
            if (!Game.LockCursor || UiKit.Typing() || _s.Wheel.IsOpen || Time.unscaledTime < _nextPush) return;
            var player = Game.LocalPlayer;
            if (player == null || Game.PlayerVehicle(player) != null) return;
            string held;
            try { held = Game.HeldTech(); } catch { held = ""; }
            if (!string.IsNullOrEmpty(held)) return; // tools do their own thing

            var cam = Game.Camera;
            if (cam == null) return;
            var eye = cam.transform.position;
            var look = cam.transform.forward;
            RemotePlayer target = null;
            float best = float.MaxValue;
            foreach (var r in _s.RemotePlayers)
            {
                if (r == null || !r.gameObject.activeInHierarchy || !r.Visible) continue;
                var to = r.Chest - eye;
                float d = to.magnitude;
                if (d > Range || d < 0.05f || Vector3.Angle(look, to) > Cone) continue;
                if (d < best) { best = d; target = r; }
            }
            if (target == null) return;

            _nextPush = Time.unscaledTime + Cooldown;
            var dir = (target.Chest - eye).normalized + look;
            _s.Send(new PushPacket { TargetId = target.Id, Direction = new Vec3(dir.x, dir.y, dir.z) });
        }

        public void OnPush(PushPacket p)
        {
            var dir = new Vector3(p.Direction.X, p.Direction.Y, p.Direction.Z);
            if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward;
            dir.Normalize();

            var pusher = _s.RemotePlayers.FirstOrDefault(r => r != null && r.Id == p.PusherId);
            pusher?.PlayEmote(Emote.Shove);

            if (p.TargetId == _s.LocalId) { KnockMe(dir); _s.AddChat($"{_s.NameOf(p.PusherId)} pushed you!"); return; }
            var target = _s.RemotePlayers.FirstOrDefault(r => r != null && r.Id == p.TargetId);
            target?.PlayEmote(Emote.Knocked, dir);
            if (p.PusherId == _s.LocalId && target != null) _s.AddChat($"You pushed {target.PlayerName}!");
        }

        void KnockMe(Vector3 dir)
        {
            var player = Game.LocalPlayer;
            if (player == null) return;
            _knockedUntil = Time.unscaledTime + Emotes.KnockedSeconds;
            _s.Emoting.Stop();

            bool underwater = Game.Is(player, "IsUnderwater");
            var push = underwater ? dir * Strength : new Vector3(dir.x, 0f, dir.z).normalized * LandStrength + Vector3.up * 2.5f;
            var rb = Game.TryGet(Game.Player, player, "rigidBody") as Rigidbody ?? player.GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic) rb.velocity = push;
            else player.transform.position += push * 0.15f; // no physics body to shove: at least move them a bit

            _s.Emoting.PlayLocalOnly(Emote.Knocked, dir, dir * 5f + Vector3.up * 1.5f); // you see yourself flop over (camera swings out)
        }
    }
}
