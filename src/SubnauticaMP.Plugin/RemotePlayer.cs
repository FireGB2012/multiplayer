using System;
using SubnauticaMP.Shared;
using UnityEngine;

namespace SubnauticaMP
{
    // Placeholder body for another diver: a colored capsule with a "visor" showing which way they face.
    // Smoothly chases the latest position from the network so 20Hz updates don't look choppy.
    public sealed class RemotePlayer : MonoBehaviour
    {
        const float SnapDistance = 30f; // teleports / respawns: jump instead of gliding across the map

        public int Id { get; private set; }
        public string PlayerName { get; private set; }

        Vector3 _targetPos;
        Quaternion _targetRot = Quaternion.identity;
        bool _hasTarget;
        Renderer[] _renderers;

        public static RemotePlayer Create(int id, string name)
        {
            var root = new GameObject("RemotePlayer_" + id);
            DontDestroyOnLoad(root);

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Destroy(body.GetComponent<Collider>()); // ghosts shouldn't block anyone
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = new Vector3(0.6f, 0.9f, 0.6f);

            var visor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(visor.GetComponent<Collider>());
            visor.transform.SetParent(root.transform, false);
            visor.transform.localPosition = new Vector3(0f, 0.55f, 0.28f);
            visor.transform.localScale = new Vector3(0.4f, 0.15f, 0.1f);
            visor.GetComponent<Renderer>().material.color = Color.yellow;

            var remote = root.AddComponent<RemotePlayer>();
            remote.Id = id;
            remote.PlayerName = name;
            body.GetComponent<Renderer>().material.color = ColorFor(id);
            remote._renderers = root.GetComponentsInChildren<Renderer>();
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

            // In a vehicle their body is inside the seamoth/prawn; hide the capsule so it doesn't poke out.
            bool visible = (state.Flags & PlayerFlags.InVehicle) == 0;
            foreach (var r in _renderers) r.enabled = visible;

            if (!_hasTarget || Vector3.Distance(transform.position, _targetPos) > SnapDistance)
            {
                transform.SetPositionAndRotation(_targetPos, _targetRot);
            }
            _hasTarget = true;
            if (!gameObject.activeSelf) gameObject.SetActive(true);
        }

        void Update()
        {
            if (!_hasTarget) return;
            float t = 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime);
            transform.position = Vector3.Lerp(transform.position, _targetPos, t);
            transform.rotation = Quaternion.Slerp(transform.rotation, _targetRot, t);
        }
    }
}
