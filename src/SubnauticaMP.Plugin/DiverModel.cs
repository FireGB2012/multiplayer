using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace SubnauticaMP
{
    // Real diver bodies for other players, the way Nitrox does it: copy the local player's own
    // "body" (dive suit model + animator), strip out every script that would act like a second
    // player, and keep just the mesh, the animator and the lighting helper.
    internal static class DiverModel
    {
        static GameObject _holder;     // inactive parent: things inside it never wake up
        static GameObject _prototype;
        static bool _failed;

        public const string AttachPoint = "player_view/export_skeleton/head_rig/neck/chest/clav_R/clav_R_aim/shoulder_R/elbow_R/hand_R/attach1";
        static readonly HashSet<string> Keep = new HashSet<string> { "SkyApplier" }; // makes the suit lit like the world

        // A fresh diver body, or null if we can't make one (then the capsule stays).
        public static GameObject Create(Transform parent)
        {
            var proto = Prototype();
            if (proto == null) return null;
            var body = UnityEngine.Object.Instantiate(proto, parent, false);
            body.name = "DiverBody";
            body.SetActive(true);
            return body;
        }

        static GameObject Prototype()
        {
            if (_prototype != null) return _prototype;
            if (_failed) return null;
            var player = Game.LocalPlayer;
            if (player == null) return null;
            var source = player.transform.Find("body");
            if (source == null)
            {
                _failed = true;
                Plugin.Log.LogWarning("Player 'body' not found; other players stay as capsules");
                return null;
            }

            try
            {
                if (_holder == null)
                {
                    _holder = new GameObject("SubnauticaMP_DiverPrototype");
                    _holder.SetActive(false);
                    Game.KeepAlive(_holder);
                }

                // copying into an inactive parent means none of the player's scripts ever run on the copy
                var clone = UnityEngine.Object.Instantiate(source.gameObject, _holder.transform, false);
                clone.name = "DiverPrototype";
                clone.transform.localPosition = source.localPosition;
                clone.transform.localRotation = source.localRotation;

                // tools / items the local player is holding right now
                var hand = clone.transform.Find(AttachPoint);
                if (hand != null)
                    foreach (Transform child in hand.Cast<Transform>().ToList())
                        if (!child.name.Contains("attach1_")) UnityEngine.Object.DestroyImmediate(child.gameObject);

                StripScripts(clone);

                foreach (var c in clone.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(c);
                foreach (var c in clone.GetComponentsInChildren<Camera>(true)) UnityEngine.Object.DestroyImmediate(c);
                foreach (var c in clone.GetComponentsInChildren<AudioListener>(true)) UnityEngine.Object.DestroyImmediate(c);

                // first-person hides your own head (shadow only); other people should see it
                foreach (var r in clone.GetComponentsInChildren<Renderer>(true))
                {
                    r.shadowCastingMode = ShadowCastingMode.On;
                    r.enabled = true;
                }

                _prototype = clone;
                Plugin.Log.LogInfo("Diver model ready for other players");
            }
            catch (Exception e)
            {
                _failed = true;
                Plugin.Log.LogWarning("Couldn't copy the diver model, using capsules: " + e.GetBaseException().Message);
            }
            return _prototype;
        }

        // Scripts can depend on each other, so keep removing until nothing more goes.
        public static Transform Holder
        {
            get
            {
                if (_holder == null)
                {
                    _holder = new GameObject("SubnauticaMP_DiverPrototype");
                    _holder.SetActive(false);
                    Game.KeepAlive(_holder);
                }
                return _holder.transform;
            }
        }

        // Makes a lifeless copy of a tool/item: just the looks, nothing that acts.
        public static GameObject MakeProp(GameObject prefab)
        {
            var copy = UnityEngine.Object.Instantiate(prefab, Holder, false); // inactive parent: nothing wakes up
            StripScripts(copy);
            foreach (var c in copy.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(c);
            foreach (var c in copy.GetComponentsInChildren<Rigidbody>(true)) UnityEngine.Object.DestroyImmediate(c);
            return copy;
        }

        public static void StripScripts(GameObject go)
        {
            for (int pass = 0; pass < 5; pass++)
            {
                bool removed = false;
                foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (mb == null || Keep.Contains(mb.GetType().Name)) continue;
                    try { UnityEngine.Object.DestroyImmediate(mb); removed = true; }
                    catch { } // something else still needs it; next pass
                }
                if (!removed) break;
            }
        }

        public static void Forget()
        {
            if (_prototype != null) UnityEngine.Object.Destroy(_prototype);
            _prototype = null;
            _failed = false;
            PlayerLooks.Forget();
        }
    }

    // Plays the game's own swim/walk animations on a copied diver body from how fast it moves.
    internal sealed class DiverAnimator
    {
        readonly Animator _animator;
        readonly HashSet<string> _params;
        Vector3 _smoothed;

        public DiverAnimator(GameObject body)
        {
            _animator = body.GetComponentInChildren<Animator>(true);
            _params = _animator != null
                ? new HashSet<string>(_animator.parameters.Select(p => p.name))
                : new HashSet<string>();
        }

        public bool Valid => _animator != null;

        public void Update(Transform root, Vector3 worldVelocity, bool underwater, float dt)
        {
            if (_animator == null) return;
            var local = Quaternion.Inverse(root.rotation) * worldVelocity;
            _smoothed = Vector3.Lerp(_smoothed, local, 1f - Mathf.Exp(-6f * dt));
            SetBool("is_underwater", underwater);
            SetFloat("move_speed", _smoothed.magnitude);
            SetFloat("move_speed_x", _smoothed.x);
            SetFloat("move_speed_y", _smoothed.y);
            SetFloat("move_speed_z", _smoothed.z);
            SetFloat("view_pitch", 0f);
        }

        // tool / PDA / builder switches from the other player's own animator
        public void SetToolAnims(string anims)
        {
            if (_animator == null) return;
            var on = new HashSet<string>((anims ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
            foreach (var p in _params)
                if (PlayerLooks.IsToolAnim(p)) SetBool(p, on.Contains(p));
        }

        void SetBool(string n, bool v) { if (_params.Contains(n)) _animator.SetBool(n, v); }
        void SetFloat(string n, float v) { if (_params.Contains(n)) _animator.SetFloat(n, v); }
    }
}
