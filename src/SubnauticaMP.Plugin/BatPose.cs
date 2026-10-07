using System.Linq;
using SubnauticaMP.Shared;
using UnityEngine;

namespace SubnauticaMP
{
    // Holding and swinging the Titanium Bat, on a diver body (yours in first person, or another player's).
    // Runs after the game has animated the body: the arms get bent by two-bone IK so the right hand goes where
    // the swing (BatSwing.Keys, in "look space") wants it, the hand turns so the bat points the right way,
    // the left hand grabs the handle while swinging, and the chest turns into the swing (legs stay put).
    internal sealed class BatPose
    {
        const float InSeconds = 0.22f;  // picking the bat up: ease-out
        const float OutSeconds = 0.15f; // putting it away: ease-in, faster than in
        const float LeftHandBelow = 0.11f; // m, left hand under the right on the handle

        readonly Transform _chest, _upperR, _lowerR, _handR, _upperL, _lowerL, _handL;
        readonly Transform[] _chestKeep; // chest children that aren't arms (the spine / legs hang off the chest)
        readonly Vector3[] _keepPos;
        readonly Quaternion[] _keepRot;
        Transform _grip, _tip;
        float _nextSearch;
        float _hold, _holdShown; // 0..1 picked up
        float _time = -1f;       // seconds into a swing, -1 = not swinging
        float _hitStop;
        float _idleClock;

        public bool Valid => _upperR != null && _lowerR != null && _handR != null;
        public bool Swinging => _time >= 0f;
        public float SwingTime => _time;
        // set on the frame the swing passes the contact moment (BatSync checks for hits then)
        public bool JustContacted { get; private set; }
        public bool JustStruck { get; private set; } // passed StrikeStart: whoosh

        public BatPose(GameObject body)
        {
            var all = body.GetComponentsInChildren<Transform>(true);
            Transform Bone(params string[] names)
            {
                foreach (var n in names)
                {
                    var t = all.FirstOrDefault(b => string.Equals(b.name, n, System.StringComparison.OrdinalIgnoreCase));
                    if (t != null) return t;
                }
                return null;
            }
            _chest = Bone("chest", "spine_3");
            _upperR = Bone("shoulder_R", "upperarm_R");
            _lowerR = Bone("elbow_R", "forearm_R");
            _handR = Bone("hand_R", "wrist_R");
            _upperL = Bone("shoulder_L", "upperarm_L");
            _lowerL = Bone("elbow_L", "forearm_L");
            _handL = Bone("hand_L", "wrist_L");
            if (_chest != null)
            {
                _chestKeep = Enumerable.Range(0, _chest.childCount).Select(i => _chest.GetChild(i))
                    .Where(c => !(_upperR != null && _upperR.IsChildOf(c)) && !(_upperL != null && _upperL.IsChildOf(c))).ToArray();
                _keepPos = new Vector3[_chestKeep.Length];
                _keepRot = new Quaternion[_chestKeep.Length];
            }
        }

        public void Swing()
        {
            _time = 0f;
            _hitStop = 0f;
        }

        // freeze the swing for a moment on a hit, so it lands with weight
        public void HitStop(float seconds) => _hitStop = Mathf.Max(_hitStop, seconds);

        public void Cancel()
        {
            _time = -1f;
            _hold = 0f;
            _holdShown = 0f;
        }

        // holding: is the bat in this body's hand. look: look space (where they face, pitch included).
        // twist: how much of the chest turn to use (less in first person, where it'd swing your view of yourself).
        public void Apply(float dt, bool holding, Quaternion look, float twist = 1f)
        {
            JustContacted = false;
            JustStruck = false;
            if (!Valid) return;

            // picking up / putting away
            if (holding) { _hold = Mathf.Min(1f, _hold + dt / InSeconds); _holdShown = Ease.OutCubic(_hold); }
            else { _hold = Mathf.Max(0f, _hold - dt / OutSeconds); _holdShown = 1f - Ease.InCubic(1f - _hold); }
            if (!holding) _time = -1f;
            if (_holdShown <= 0.001f) return;

            if (_time >= 0f)
            {
                float before = _time;
                if (_hitStop > 0f) _hitStop -= dt;
                else _time += dt;
                if (before < BatSwing.StrikeStart && _time >= BatSwing.StrikeStart) JustStruck = true;
                if (before < BatSwing.Contact && _time >= BatSwing.Contact) JustContacted = true;
                if (_time >= BatSwing.Seconds) _time = -1f;
            }
            _idleClock += dt;

            // the pose now
            Vector3 hand, bat, pole;
            float reach, turn, left;
            if (_time >= 0f)
            {
                var (a, b, f) = BatSwing.At(_time);
                hand = Vector3.Slerp(V(a.Hand).normalized, V(b.Hand).normalized, f);
                bat = Vector3.Slerp(V(a.Bat).normalized, V(b.Bat).normalized, f);
                pole = Vector3.Slerp(V(a.Pole).normalized, V(b.Pole).normalized, f);
                reach = Mathf.LerpUnclamped(a.Reach, b.Reach, f);
                turn = Mathf.LerpUnclamped(a.Twist, b.Twist, f);
                left = Mathf.Clamp01(Mathf.LerpUnclamped(a.Left, b.Left, f));
            }
            else
            {
                var k = BatSwing.Idle;
                hand = V(k.Hand).normalized;
                // barely-there sway so it doesn't look frozen
                bat = Quaternion.Euler(Mathf.Sin(_idleClock * 1.7f) * 1.2f, Mathf.Sin(_idleClock * 1.1f) * 1.5f, 0f) * V(k.Bat).normalized;
                pole = V(k.Pole).normalized;
                reach = k.Reach;
                turn = 0f;
                left = 0f;
            }
            float w = _holdShown;

            // chest turn (around the body's up), with the spine/legs turned back so only the shoulders move
            if (_chest != null && Mathf.Abs(turn * twist) > 0.01f)
            {
                for (int i = 0; i < _chestKeep.Length; i++) { _keepPos[i] = _chestKeep[i].position; _keepRot[i] = _chestKeep[i].rotation; }
                _chest.rotation = Quaternion.AngleAxis(turn * twist * w, Vector3.up) * _chest.rotation;
                for (int i = 0; i < _chestKeep.Length; i++) _chestKeep[i].SetPositionAndRotation(_keepPos[i], _keepRot[i]);
            }

            // right arm
            float lenR = Vector3.Distance(_upperR.position, _lowerR.position) + Vector3.Distance(_lowerR.position, _handR.position);
            var target = _upperR.position + look * hand * (reach * lenR);
            TwoBone(_upperR, _lowerR, _handR, target, look * pole, w);

            // turn the hand so the bat points where it should
            FindMarkers();
            if (_grip != null && _tip != null)
            {
                var now = _tip.position - _grip.position;
                if (now.sqrMagnitude > 1e-6f)
                    _handR.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(now, look * bat), w) * _handR.rotation;
            }

            // left hand on the handle, under the right one
            if (left > 0.001f && _upperL != null && _lowerL != null && _handL != null && _grip != null && _tip != null)
            {
                var axis = (_tip.position - _grip.position).normalized;
                var leftTarget = _grip.position - axis * LeftHandBelow;
                TwoBone(_upperL, _lowerL, _handL, leftTarget, look * new Vector3(-0.6f, -0.8f, 0f), w * Ease.InOutCubic(left));
            }
        }

        static Vector3 V(Vec3 v) => new Vector3(v.X, v.Y, v.Z);

        // The bat's grip / tip markers (BatItem puts them in the model), on whatever's in the right hand.
        void FindMarkers()
        {
            if (_grip != null && _tip != null) return;
            if (Time.unscaledTime < _nextSearch) return; // the bat model can take a moment to show up
            _nextSearch = Time.unscaledTime + 0.25f;
            _grip = _tip = null;
            foreach (var t in _handR.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "BatGrip") _grip = t;
                else if (t.name == "BatTip") _tip = t;
            }
        }

        // Bends upper/lower so `end` reaches `target` (as far as the arm allows), the elbow toward `pole`.
        // w blends from the animation's pose (0) to the full IK pose (1).
        static void TwoBone(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 pole, float w)
        {
            var a = upper.position;
            float lenA = Vector3.Distance(a, lower.position);
            float lenB = Vector3.Distance(lower.position, end.position);
            if (lenA < 1e-4f || lenB < 1e-4f) return;
            var toTarget = target - a;
            float d = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(lenA - lenB) + 1e-3f, (lenA + lenB) * 0.999f);
            var dir = toTarget.sqrMagnitude > 1e-8f ? toTarget.normalized : upper.forward;
            // elbow: on the circle around the shoulder->target line, on the pole's side
            float cos = Mathf.Clamp((lenA * lenA + d * d - lenB * lenB) / (2f * lenA * d), -1f, 1f);
            float sin = Mathf.Sqrt(1f - cos * cos);
            var side = Vector3.ProjectOnPlane(pole, dir);
            if (side.sqrMagnitude < 1e-6f) side = Vector3.ProjectOnPlane(lower.position - a, dir);
            if (side.sqrMagnitude < 1e-6f) side = Vector3.ProjectOnPlane(Vector3.down, dir);
            side.Normalize();
            var elbow = a + dir * (cos * lenA) + side * (sin * lenA);
            var reach = a + dir * d;

            upper.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(lower.position - a, elbow - a), w) * upper.rotation;
            lower.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(end.position - lower.position, reach - lower.position), w) * lower.rotation;
        }
    }
}
