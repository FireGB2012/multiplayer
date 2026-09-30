using System;
using System.Collections.Generic;
using System.Linq;
using SubnauticaMP.Shared;
using UnityEngine;

namespace SubnauticaMP
{
    // A real physics ragdoll on a copied diver body: rigidbodies + colliders + joints on the main bones, the
    // animator switched off, a shove, and physics does the rest (falls on the floor in bases, floats limp in water).
    // Then the physics comes off and the pose blends back into the normal animation: standing back up.
    internal sealed class Ragdoll
    {
        const float GetUpSeconds = 0.9f;

        // bone, the bone it points to (for the capsule), thickness (m), mass (kg). The diver's rig hangs from the
        // head down: head_rig > neck > chest > spine_3 > spine_2 > spine_1 > hips > thighs.
        static readonly (string bone, string child, float radius, float mass)[] Parts =
        {
            ("head_rig", null, 0.13f, 5f),
            ("chest", "spine_2", 0.17f, 15f),
            ("spine_1", "hips", 0.16f, 12f),
            ("shoulder_L", "elbow_L", 0.06f, 3f), ("elbow_L", "hand_L", 0.05f, 2f),
            ("shoulder_R", "elbow_R", 0.06f, 3f), ("elbow_R", "hand_R", 0.05f, 2f),
            ("thigh_L", "calf_L", 0.08f, 7f), ("calf_L", "ankle_L", 0.065f, 4f),
            ("thigh_R", "calf_R", 0.08f, 7f), ("calf_R", "ankle_R", 0.065f, 4f),
        };

        readonly GameObject _body;
        readonly Transform _root;
        readonly Animator _animator;
        readonly List<Component> _added = new List<Component>();
        readonly Transform[] _all;
        Vector3[] _savedPos;
        readonly Vector3[] _standPos; // where each bone sits when standing (the animator may not put positions back)
        Quaternion[] _savedRot;
        readonly Transform _chest;
        readonly Vector3 _chestOffset; // chest relative to the root while standing
        float _time;
        readonly float _lieSeconds;
        bool _gettingUp;
        float _upTime;

        public Transform Focus => _chest != null ? _chest : _body.transform;

        Ragdoll(GameObject body, Transform root, float totalSeconds)
        {
            _body = body;
            _root = root;
            _animator = body.GetComponentInChildren<Animator>(true);
            _all = body.GetComponentsInChildren<Transform>(true).Where(t => t != body.transform).ToArray(); // bones only
            _standPos = _all.Select(t => t.localPosition).ToArray();
            _chest = _all.FirstOrDefault(t => t.name == "chest");
            _chestOffset = _chest != null ? Quaternion.Inverse(root.rotation) * (_chest.position - root.position) : Vector3.up * 0.9f;
            _lieSeconds = Mathf.Max(0.5f, totalSeconds - GetUpSeconds);
        }

        // Knocks `body` over with `velocity` (m/s, world). Null if its skeleton isn't one we can ragdoll.
        public static Ragdoll Start(GameObject body, Transform root, Vector3 velocity, bool underwater, float totalSeconds = Emotes.KnockedSeconds)
        {
            if (body == null) return null;
            var r = new Ragdoll(body, root, totalSeconds);
            try
            {
                if (!r.Build(velocity, underwater)) { r.Remove(); return null; }
                return r;
            }
            catch (Exception e)
            {
                Game.WarnOnce("ragdoll", "Ragdoll didn't work, using the animated fall: " + e.GetBaseException().Message);
                r.Remove();
                return null;
            }
        }

        bool Build(Vector3 velocity, bool underwater)
        {
            Transform Find(string n) => n == null ? null : _all.FirstOrDefault(t => t.name == n);
            var bodies = new Dictionary<Transform, Rigidbody>();
            var colliders = new List<Collider>();

            foreach (var (boneName, childName, radius, mass) in Parts)
            {
                var bone = Find(boneName);
                if (bone == null) continue;
                float scale = Mathf.Max(0.001f, Mathf.Abs(bone.lossyScale.x));
                var child = Find(childName);
                Collider col;
                if (child != null)
                {
                    var local = bone.InverseTransformPoint(child.position);
                    int axis = Mathf.Abs(local.x) > Mathf.Abs(local.y) ? (Mathf.Abs(local.x) > Mathf.Abs(local.z) ? 0 : 2) : (Mathf.Abs(local.y) > Mathf.Abs(local.z) ? 1 : 2);
                    var cap = bone.gameObject.AddComponent<CapsuleCollider>();
                    cap.direction = axis;
                    cap.center = local * 0.5f;
                    cap.radius = radius / scale;
                    cap.height = Mathf.Max(local.magnitude, 2f * cap.radius);
                    col = cap;
                }
                else
                {
                    var sphere = bone.gameObject.AddComponent<SphereCollider>();
                    sphere.radius = radius / scale;
                    col = sphere;
                }
                _added.Add(col);
                colliders.Add(col);

                var rb = bone.gameObject.AddComponent<Rigidbody>();
                rb.mass = mass;
                rb.useGravity = !underwater;
                rb.drag = underwater ? 2.5f : 0.1f;
                rb.angularDrag = underwater ? 2.5f : 0.3f;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                _added.Add(rb);
                bodies[bone] = rb;
            }
            if (bodies.Count < 7) return false; // not enough of a skeleton

            // joints: each part hangs off the nearest part above it
            foreach (var kv in bodies)
            {
                Rigidbody parent = null;
                for (var p = kv.Key.parent; p != null && parent == null; p = p.parent) bodies.TryGetValue(p, out parent);
                if (parent == null) continue; // the top one (head)
                var j = kv.Key.gameObject.AddComponent<CharacterJoint>();
                j.connectedBody = parent;
                j.enableProjection = true; // don't let limbs stretch off
                j.lowTwistLimit = new SoftJointLimit { limit = -25f };
                j.highTwistLimit = new SoftJointLimit { limit = 35f };
                j.swing1Limit = new SoftJointLimit { limit = 45f };
                j.swing2Limit = new SoftJointLimit { limit = 35f };
                _added.Insert(0, j); // joints go first when removing
            }

            for (int a = 0; a < colliders.Count; a++)
                for (int b = a + 1; b < colliders.Count; b++)
                    Physics.IgnoreCollision(colliders[a], colliders[b]);

            foreach (var smr in _body.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.updateWhenOffscreen = true;
            if (_animator != null) _animator.enabled = false;

            var spin = UnityEngine.Random.insideUnitSphere * 3f;
            foreach (var rb in bodies.Values)
            {
                rb.velocity = velocity;
                rb.angularVelocity = spin;
            }
            return true;
        }

        // Call every LateUpdate. False once they're back on their feet (and everything's cleaned up).
        public bool LateUpdate(float dt)
        {
            if (_body == null) return false;
            _time += dt;
            if (!_gettingUp)
            {
                if (_time < _lieSeconds) return true;
                StartGettingUp();
                return true;
            }

            // the animator has posed the diver standing again: slide from the lying pose into it
            _upTime += dt;
            float k = Mathf.Clamp01(_upTime / GetUpSeconds);
            k = k * k * (3f - 2f * k);
            for (int i = 0; i < _all.Length; i++)
            {
                var t = _all[i];
                if (t == null) continue;
                t.localRotation = Quaternion.Slerp(_savedRot[i], t.localRotation, k);
                t.localPosition = Vector3.Lerp(_savedPos[i], _standPos[i], k);
            }
            return _upTime < GetUpSeconds;
        }

        void StartGettingUp()
        {
            _gettingUp = true;
            // stand up where the body ended up, not where it started
            if (_chest != null && _root != null)
            {
                var landed = _chest.position;
                var target = landed - _root.rotation * _chestOffset;
                // keep the lying pose where it is while the root moves under it
                var before = _all.Select(t => (t.position, t.rotation)).ToArray();
                _root.position = target;
                for (int i = 0; i < _all.Length; i++) if (_all[i] != null) _all[i].SetPositionAndRotation(before[i].position, before[i].rotation);
            }
            _savedPos = _all.Select(t => t != null ? t.localPosition : Vector3.zero).ToArray();
            _savedRot = _all.Select(t => t != null ? t.localRotation : Quaternion.identity).ToArray();
            Remove();
            if (_animator != null) _animator.enabled = true;
        }

        // Takes all the physics back off (also if something went wrong).
        public void Remove()
        {
            foreach (var c in _added)
                if (c != null) UnityEngine.Object.DestroyImmediate(c);
            _added.Clear();
            if (_animator != null && !_gettingUp) _animator.enabled = true;
        }
    }
}
