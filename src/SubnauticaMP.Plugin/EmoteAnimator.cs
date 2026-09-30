using System.Collections.Generic;
using System.Linq;
using SubnauticaMP.Shared;
using UnityEngine;

namespace SubnauticaMP
{
    // Plays an emote on a copied diver body, on top of the game's own swim animation.
    // The game has no emote animations, so the poses are made here: after the animator has posed the
    // skeleton each frame (LateUpdate), arm bones get turned to point where the emote wants them
    // (worked out from the bones' actual positions, so it doesn't matter how the rig's axes face),
    // the head nods / shakes, and the whole body can bob, spin or flip.
    internal sealed class EmoteAnimator
    {
        const float FadeSeconds = 0.2f;

        readonly Transform _body;
        readonly Vector3 _basePos;
        readonly Quaternion _baseRot;
        readonly Transform _chest, _head, _upperR, _lowerR, _handR, _upperL, _lowerL, _handL;
        readonly Transform _thighL, _kneeL, _footL, _thighR, _kneeR, _footR;
        readonly Vector3 _hipPivot;   // between the hips, in the body's parent space
        readonly float _legLength;
        EmoteClips.Clip _clip;
        EmoteClips.Pose _pose;

        // Party dancing: which clip and how far into it, from the party's shared clock (null = no party).
        public static System.Func<(EmoteClips.Clip clip, float time)?> PartyNow;
        readonly Transform[] _bones;
        readonly Quaternion[] _before, _after;
        bool _tracking;

        Emote _showing = Emote.None; // what's on screen (still fading out after a stop)
        bool _active;
        float _time, _seconds, _weight;
        Vector3 _pivot; // body-local-to-parent point the flips / spins turn around

        static bool _dumped;

        public Emote Current => _active ? _showing : Emote.None;

        public EmoteAnimator(GameObject body)
        {
            _body = body.transform;
            _basePos = _body.localPosition;
            _baseRot = _body.localRotation;
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

            _chest = Bone("chest", "spine_3", "spine_2", "spine");
            _upperR = Bone("shoulder_R", "upperarm_R", "arm_R");
            _lowerR = Bone("elbow_R", "forearm_R", "lowerarm_R");
            _handR = Bone("hand_R", "wrist_R");
            _upperL = Bone("shoulder_L", "upperarm_L", "arm_L");
            _lowerL = Bone("elbow_L", "forearm_L", "lowerarm_L");
            _handL = Bone("hand_L", "wrist_L");
            // the head: a bone called "head" that isn't also carrying the arms around
            _head = all.FirstOrDefault(b => string.Equals(b.name, "head", System.StringComparison.OrdinalIgnoreCase) && !Carries(b))
                    ?? all.FirstOrDefault(b => b.name.ToLowerInvariant().Contains("head") && !b.name.ToLowerInvariant().Contains("rig") && !Carries(b) && b.GetComponent<Renderer>() == null);

            // if the rig's "_R" bones are on the model's left, swap so "right" means right
            if (_upperR != null && _upperL != null &&
                _body.InverseTransformPoint(_upperR.position).x < _body.InverseTransformPoint(_upperL.position).x)
            {
                (_upperR, _upperL) = (_upperL, _upperR);
                (_lowerR, _lowerL) = (_lowerL, _lowerR);
                (_handR, _handL) = (_handL, _handR);
            }

            // legs: by name if the rig uses the usual words, else the lowest bones on each side
            var armBones = new[] { _upperR, _upperL };
            bool UnderArm(Transform t) => armBones.Any(a => a != null && t.IsChildOf(a));
            var candidates = all.Where(b => b.GetComponent<Renderer>() == null && !UnderArm(b) && b != _body).ToArray();
            FindLeg(candidates, true, out _thighL, out _kneeL, out _footL);
            FindLeg(candidates, false, out _thighR, out _kneeR, out _footR);
            if (_thighL != null && _thighR != null &&
                _body.InverseTransformPoint(_thighL.position).x > _body.InverseTransformPoint(_thighR.position).x)
            {
                (_thighL, _thighR) = (_thighR, _thighL);
                (_kneeL, _kneeR) = (_kneeR, _kneeL);
                (_footL, _footR) = (_footR, _footL);
            }
            var parent = _body.parent;
            if (_thighL != null && _thighR != null && _kneeL != null && _footL != null)
            {
                var mid = (_thighL.position + _thighR.position) * 0.5f;
                _hipPivot = parent != null ? parent.InverseTransformPoint(mid) : mid;
                _legLength = Vector3.Distance(_thighL.position, _kneeL.position) + Vector3.Distance(_kneeL.position, _footL.position);
            }
            else
            {
                _hipPivot = _basePos + Vector3.up * 0.9f;
                _legLength = 0.9f;
            }
            if (_legLength < 0.3f || _legLength > 2f) _legLength = 0.9f;

            _bones = new[] { _chest, _head, _upperR, _lowerR, _upperL, _lowerL, _thighL, _kneeL, _thighR, _kneeR }.Where(b => b != null).Distinct().ToArray();
            _before = new Quaternion[_bones.Length];
            _after = new Quaternion[_bones.Length];

            if (!_dumped)
            {
                _dumped = true;
                Plugin.Log.LogInfo($"Emote bones: chest={Name(_chest)} head={Name(_head)} right arm={Name(_upperR)}/{Name(_lowerR)}/{Name(_handR)} left arm={Name(_upperL)}/{Name(_lowerL)}/{Name(_handL)} " +
                                   $"left leg={Name(_thighL)}/{Name(_kneeL)}/{Name(_footL)} right leg={Name(_thighR)}/{Name(_kneeR)}/{Name(_footR)} leg length {_legLength:0.00}");
                if (_head == null || _upperR == null || _upperL == null || _thighL == null || _thighR == null)
                    Plugin.Log.LogInfo("Diver skeleton: " + string.Join(", ", all.Where(b => b.GetComponent<Renderer>() == null).Select(b => b.name).Take(120).ToArray()));
            }

            bool Carries(Transform t) => (_upperR != null && _upperR.IsChildOf(t)) || (_upperL != null && _upperL.IsChildOf(t));
        }

        static string Name(Transform t) => t != null ? t.name : "-";

        static readonly string[] ThighWords = { "thigh", "upleg", "upperleg", "up_leg", "hip", "leg_up" };
        static readonly string[] KneeWords = { "knee", "calf", "shin", "lowerleg", "low_leg", "leg_low", "leg" };
        static readonly string[] FootWords = { "ankle", "foot", "heel" };

        static bool IsSide(string name, bool left)
        {
            var n = name.ToLowerInvariant();
            var c = left ? "l" : "r";
            return n.EndsWith("_" + c) || n.StartsWith(c + "_") || n.Contains("_" + c + "_") || n.EndsWith("." + c) ||
                   n.Contains(left ? "left" : "right") || n.EndsWith(" " + c);
        }

        static Transform ByWords(Transform[] bones, string[] words, bool left)
        {
            foreach (var w in words)
                foreach (var b in bones)
                    if (IsSide(b.name, left) && b.name.ToLowerInvariant().Contains(w)) return b;
            return null;
        }

        void FindLeg(Transform[] bones, bool left, out Transform thigh, out Transform knee, out Transform foot)
        {
            foot = ByWords(bones, FootWords, left);
            knee = foot != null && foot.parent != null && IsSide(foot.parent.name, left) ? foot.parent : ByWords(bones, KneeWords.Where(w => w != "leg").ToArray(), left);
            thigh = knee != null && knee.parent != null ? knee.parent : ByWords(bones, ThighWords, left);
            if (foot == null && knee != null && knee.childCount > 0) foot = knee.GetChild(0);
            if (thigh != null && knee != null && foot != null && LegOk(thigh, knee, foot)) return;

            // no names we know: the lowest bone on that side is the foot (or toe), walk up from there
            float side = left ? -1f : 1f;
            Transform low = null;
            float lowY = float.MaxValue;
            foreach (var b in bones)
            {
                var p = _body.InverseTransformPoint(b.position);
                if (p.x * side < 0.03f) continue;
                if (p.y < lowY) { lowY = p.y; low = b; }
            }
            thigh = knee = foot = null;
            if (low == null) return;
            var f = low;
            if (f.parent != null && _body.InverseTransformPoint(f.parent.position).y - lowY < 0.12f) f = f.parent; // toe -> ankle
            if (f.parent == null || f.parent.parent == null) return;
            if (LegOk(f.parent.parent, f.parent, f)) { thigh = f.parent.parent; knee = f.parent; foot = f; }
        }

        static bool LegOk(Transform thigh, Transform knee, Transform foot) =>
            Vector3.Distance(thigh.position, knee.position) > 0.15f && Vector3.Distance(knee.position, foot.position) > 0.15f &&
            thigh.position.y > knee.position.y - 0.05f;

        public void Play(Emote emote)
        {
            var info = Emotes.Get(emote);
            if (info == null) { Stop(); return; }
            if (_active && _showing == emote && info.Seconds <= 0f) return; // looping one: keep going
            if (_showing != emote) _weight = 0f;
            _showing = emote;
            _active = true;
            _time = 0f;
            _seconds = info.Seconds;
            _clip = info.Clip != null ? EmoteClips.Get(info.Clip) : null;
            if (_clip != null && !_clip.Loops) _seconds = _clip.Seconds;
            // flips / spins turn around the middle of the body, not the feet
            var mid = _chest != null ? _chest.position : _body.position + _body.up * 0.9f;
            _pivot = _body.parent != null ? _body.parent.InverseTransformPoint(mid) : mid;
            if (_body.parent != null) _pivot = _basePos + Vector3.Project(_pivot - _basePos, Vector3.up); // straight above the body's origin
        }

        public void Stop() => _active = false;

        public bool Showing => _showing != Emote.None;

        public Vector3 Middle => _chest != null ? _chest.position : _body.position + _body.up * 0.9f;

        // Straight back to normal (no fade), e.g. when you leave the server mid-dance.
        public void ResetNow()
        {
            if (_showing == Emote.None) return;
            if (_body != null)
            {
                RestoreIfAnimatorSkipped();
                _body.localPosition = _basePos;
                _body.localRotation = _baseRot;
            }
            _showing = Emote.None;
            _active = false;
            _tracking = false;
        }

        // Call from LateUpdate, after the animator has posed the diver.
        public void Apply(float dt)
        {
            if (_showing == Emote.None) return;
            RestoreIfAnimatorSkipped();

            _time += dt;
            if (_active && _seconds > 0f && _time >= _seconds) _active = false;
            _weight = Mathf.MoveTowards(_weight, _active ? 1f : 0f, dt / FadeSeconds);
            if (_weight <= 0f && !_active)
            {
                _showing = Emote.None;
                _body.localPosition = _basePos;
                _body.localRotation = _baseRot;
                _tracking = false;
                return;
            }

            for (int i = 0; i < _bones.Length; i++) _before[i] = _bones[i].localRotation;
            _body.localPosition = _basePos; // emotes that don't move the body leave it where it belongs
            _body.localRotation = _baseRot;
            float w = Smooth(_weight);
            Pose(_showing, _time, w);
            for (int i = 0; i < _bones.Length; i++) _after[i] = _bones[i].localRotation;
            _tracking = true;
        }

        // The animator rewrites the bones every frame, so our turns never pile up. But when it skips a
        // frame (diver off screen) the bones still hold last frame's emote pose: put them back first.
        void RestoreIfAnimatorSkipped()
        {
            if (!_tracking) return;
            for (int i = 0; i < _bones.Length; i++)
                if (_bones[i].localRotation == _after[i]) _bones[i].localRotation = _before[i];
        }

        static float Smooth(float x) => x * x * (3f - 2f * x);

        // ---------- the emotes ----------

        void Pose(Emote e, float t, float w)
        {
            const float Tau = Mathf.PI * 2f;
            if (e == Emote.Party)
            {
                var now = PartyNow?.Invoke();
                if (now == null || now.Value.clip == null) { _active = false; return; }
                ClipPose(now.Value.clip, now.Value.time, w);
                return;
            }
            if (_clip != null) { ClipPose(_clip, t, w); return; }
            switch (e)
            {
                case Emote.Wave:
                    Arm(true, V(0.75f, 0.55f, 0.15f), V(0.45f * Mathf.Sin(t * 9f), 1f, 0.1f), w);
                    Head(-4f, 8f * Mathf.Sin(t * 2f), 0f, w);
                    break;

                case Emote.Point:
                    Arm(true, V(0.15f, 0.15f, 1f), V(0.1f, 0.15f, 1f), w);
                    Head(0f, -6f, 0f, w);
                    break;

                case Emote.Cheer:
                {
                    float pump = Mathf.Abs(Mathf.Sin(t * 6f));
                    BodyOffset(V(0f, 0.08f * pump, 0f), Quaternion.identity, w);
                    Arm(true, V(0.35f, 1f, 0.1f), V(0.1f, 1f, 0.35f * pump), w);
                    Arm(false, V(0.35f, 1f, 0.1f), V(0.1f, 1f, 0.35f * pump), w);
                    Head(-15f, 0f, 0f, w);
                    break;
                }

                case Emote.Dance:
                {
                    float beat = t * 7f;
                    float swap = (1f + Mathf.Sin(t * 3.5f)) * 0.5f;
                    BodyOffset(V(0f, 0.06f * Mathf.Abs(Mathf.Sin(beat)), 0f),
                        Quaternion.Euler(0f, 18f * Mathf.Sin(t * 3.5f), 7f * Mathf.Sin(beat)), w);
                    Chest(0f, 10f * Mathf.Sin(beat), 0f, w);
                    var up = V(0.7f, 0.8f, 0.2f);
                    var down = V(0.3f, -0.6f, 0.6f);
                    var r = Vector3.Lerp(down, up, swap);
                    var l = Vector3.Lerp(up, down, swap);
                    Arm(true, r, r + V(0f, 0.4f, 0.3f), w);
                    Arm(false, l, l + V(0f, 0.4f, 0.3f), w);
                    Head(6f * Mathf.Sin(beat), 0f, 8f * Mathf.Sin(t * 3.5f), w);
                    break;
                }

                case Emote.Laugh:
                {
                    float shake = Mathf.Sin(t * 25f);
                    BodyOffset(V(0f, 0.015f * shake, 0f), Quaternion.identity, w);
                    Chest(-8f, 0f, 0f, w);
                    Arm(true, V(0.35f, -0.7f, 0.35f), V(-0.6f, 0.1f, 0.6f), w);
                    Arm(false, V(0.35f, -0.7f, 0.35f), V(-0.6f, 0.1f, 0.6f), w);
                    Head(-22f + 4f * shake, 0f, 0f, w);
                    break;
                }

                case Emote.Nod:
                    Head(5f + 18f * Mathf.Sin(t * Tau * 2f), 0f, 0f, w);
                    break;

                case Emote.No:
                    Head(3f, 28f * Mathf.Sin(t * Tau * 2f), 0f, w);
                    break;

                case Emote.Salute:
                    Arm(true, V(0.85f, 0.25f, 0.35f), ToFace(true, V(0.07f, 0.06f, 0.12f), V(-0.6f, 0.6f, 0.35f)), w);
                    Head(-5f, 0f, 0f, w);
                    break;

                case Emote.Facepalm:
                    Head(20f, 6f * Mathf.Sin(t * 6f), 0f, w);
                    Arm(true, V(0.25f, -0.2f, 0.9f), ToFace(true, V(0f, 0f, 0.13f), V(-0.3f, 0.8f, 0.4f)), w);
                    break;

                case Emote.Shrug:
                    BodyOffset(V(0f, 0.03f, 0f), Quaternion.identity, w);
                    Arm(true, V(0.45f, -0.8f, 0.25f), V(0.75f, 0.25f, 0.65f), w);
                    Arm(false, V(0.45f, -0.8f, 0.25f), V(0.75f, 0.25f, 0.65f), w);
                    Head(-3f, 0f, 12f, w);
                    break;

                case Emote.Clap:
                {
                    float gap = 0.03f + 0.09f * (0.5f + 0.5f * Mathf.Sin(t * 14f));
                    Arm(true, V(0.25f, -0.35f, 0.9f), InFront(true, gap, V(-0.8f, 0.2f, 0.5f)), w);
                    Arm(false, V(0.25f, -0.35f, 0.9f), InFront(false, gap, V(-0.8f, 0.2f, 0.5f)), w);
                    Head(-5f, 0f, 0f, w);
                    break;
                }

                case Emote.Flip:
                {
                    float p = Smooth(Mathf.Clamp01(t / (_seconds - 0.1f)));
                    Turn(Quaternion.AngleAxis(-360f * p, Vector3.right), V(0f, 0.35f * Mathf.Sin(Mathf.PI * p), 0f), 1f);
                    break;
                }

                case Emote.Spin:
                {
                    float p = Smooth(Mathf.Clamp01(t / (_seconds - 0.1f)));
                    Turn(Quaternion.AngleAxis(720f * p, Vector3.up), Vector3.zero, 1f);
                    Arm(true, V(1f, 0.1f, 0f), V(1f, 0.1f, 0f), w);
                    Arm(false, V(1f, 0.1f, 0f), V(1f, 0.1f, 0f), w);
                    break;
                }

                case Emote.Chill:
                {
                    // floating on your back, hands behind your head
                    Turn(Quaternion.AngleAxis(-65f, Vector3.right), V(0f, 0.03f * Mathf.Sin(t * 1.5f), 0f), w);
                    Arm(true, V(0.75f, 0.6f, -0.2f), ToFace(true, V(0.04f, 0.02f, -0.12f), V(-0.8f, 0.3f, -0.3f)), w);
                    Arm(false, V(0.75f, 0.6f, -0.2f), ToFace(false, V(0.04f, 0.02f, -0.12f), V(-0.8f, 0.3f, -0.3f)), w);
                    Head(-10f, 0f, 0f, w);
                    break;
                }
            }
        }

        // A frame of a clip: turn the whole body like the dancer's torso (around the hips), move the hips,
        // then point every arm and leg bone the way the dancer's were pointing.
        void ClipPose(EmoteClips.Clip clip, float t, float w)
        {
            EmoteClips.Sample(clip, t, ref _pose);
            var turn = Quaternion.Slerp(Quaternion.identity, _pose.Torso, w);
            _body.localRotation = turn * _baseRot;
            _body.localPosition = _hipPivot + turn * (_basePos - _hipPivot) + _pose.Hips * (_legLength * w);

            var face = _body.parent; // the way the player faces: the clip's directions are in that space
            Vector3 Dir(int k) => face != null ? face.TransformDirection(_pose.Dirs[k]) : _pose.Dirs[k];
            Aim(_upperL, _lowerL, Dir(EmoteClips.ArmUpL), w);
            Aim(_lowerL, _handL, Dir(EmoteClips.ArmLowL), w);
            Aim(_upperR, _lowerR, Dir(EmoteClips.ArmUpR), w);
            Aim(_lowerR, _handR, Dir(EmoteClips.ArmLowR), w);
            Aim(_thighL, _kneeL, Dir(EmoteClips.LegUpL), w);
            Aim(_kneeL, _footL, Dir(EmoteClips.LegLowL), w);
            Aim(_thighR, _kneeR, Dir(EmoteClips.LegUpR), w);
            Aim(_kneeR, _footR, Dir(EmoteClips.LegLowR), w);
        }

        // ---------- building blocks ----------

        // A direction in the diver's own space: x = their right, y = up, z = forward.
        static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

        Vector3 World(Vector3 local) => _body.TransformDirection(local);

        // Points the upper arm and forearm (given for the right arm; mirrored for the left).
        void Arm(bool right, Vector3 upper, Vector3 lower, float w)
        {
            var s = right ? 1f : -1f;
            var a = right ? _upperR : _upperL;
            var b = right ? _lowerR : _lowerL;
            var c = right ? _handR : _handL;
            Aim(a, b, World(new Vector3(upper.x * s, upper.y, upper.z)), w);
            Aim(b, c, World(new Vector3(lower.x * s, lower.y, lower.z)), w);
        }

        // Turns `bone` so the line from it to `child` points along `dir`.
        static void Aim(Transform bone, Transform child, Vector3 dir, float w)
        {
            if (bone == null || child == null || w <= 0f) return;
            var current = child.position - bone.position;
            if (current.sqrMagnitude < 1e-6f || dir.sqrMagnitude < 1e-6f) return;
            var turn = Quaternion.FromToRotation(current, dir);
            bone.rotation = Quaternion.Slerp(Quaternion.identity, turn, w) * bone.rotation;
        }

        // Forearm direction reaching a spot near the face (offset in diver space, x toward that arm's side).
        // Without a head bone, `fallback` is used as the forearm direction.
        Vector3 ToFace(bool right, Vector3 offset, Vector3 fallback)
        {
            var s = right ? 1f : -1f;
            var elbow = right ? _lowerR : _lowerL;
            if (_head == null || elbow == null) return fallback;
            var spot = _head.position + World(new Vector3(offset.x * s, offset.y, offset.z));
            var local = _body.InverseTransformDirection(spot - elbow.position);
            return new Vector3(local.x * s, local.y, local.z); // Arm() mirrors it back
        }

        // Forearm direction for hands meeting in front of the chest, `gap` apart.
        Vector3 InFront(bool right, float gap, Vector3 fallback)
        {
            var s = right ? 1f : -1f;
            var elbow = right ? _lowerR : _lowerL;
            if (_chest == null || elbow == null) return fallback;
            var spot = _chest.position + World(new Vector3(gap * s, 0.05f, 0.45f));
            var local = _body.InverseTransformDirection(spot - elbow.position);
            return new Vector3(local.x * s, local.y, local.z);
        }

        // Head turn in degrees: pitch (+ = look down), yaw (+ = right), roll (+ = tilt right).
        void Head(float pitch, float yaw, float roll, float w) => TurnBone(_head, pitch, yaw, roll, w);
        void Chest(float pitch, float yaw, float roll, float w) => TurnBone(_chest, pitch, yaw, roll, w);

        void TurnBone(Transform bone, float pitch, float yaw, float roll, float w)
        {
            if (bone == null) return;
            var turn = Quaternion.AngleAxis(yaw * w, _body.up) * Quaternion.AngleAxis(pitch * w, _body.right) * Quaternion.AngleAxis(-roll * w, _body.forward);
            bone.rotation = turn * bone.rotation;
        }

        // Moves / turns the whole body (in its parent's space, which faces the way the player looks).
        void BodyOffset(Vector3 move, Quaternion turn, float w)
        {
            _body.localPosition = _basePos + move * w;
            _body.localRotation = Quaternion.Slerp(Quaternion.identity, turn, w) * _baseRot;
        }

        // Turns the whole body around its middle (flips, spins, lying back).
        void Turn(Quaternion turn, Vector3 move, float w)
        {
            turn = Quaternion.Slerp(Quaternion.identity, turn, w);
            _body.localRotation = turn * _baseRot;
            _body.localPosition = _pivot + turn * (_basePos - _pivot) + move * w;
        }
    }
}
