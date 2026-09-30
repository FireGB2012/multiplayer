using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace SubnauticaMP
{
    // The dance / emote clips baked into the mod (Emotes.bin, made by tools/emotes/make_emotes.py from the CMU
    // motion capture library + a few hand-made ones). A frame says how the torso is turned, how far the hips
    // moved, and which way each limb points, so it fits the diver's skeleton without matching bone for bone.
    internal static class EmoteClips
    {
        public const int ArmUpL = 0, ArmLowL = 1, ArmUpR = 2, ArmLowR = 3, LegUpL = 4, LegLowL = 5, LegUpR = 6, LegLowR = 7, Head = 8, Segments = 9;

        public sealed class Clip
        {
            public string Name;
            public float Fps;
            public bool Loops;
            public Quaternion[] Torso;
            public Vector3[] Hips;       // in leg lengths
            public Vector3[][] Dirs;     // [frame][segment]
            public float Seconds => Torso.Length / Fps;
        }

        public struct Pose
        {
            public Quaternion Torso;
            public Vector3 Hips;
            public Vector3[] Dirs;
        }

        static Dictionary<string, Clip> _clips;

        public static Clip Get(string name)
        {
            if (_clips == null) Load();
            return name != null && _clips.TryGetValue(name, out var c) ? c : null;
        }

        public static int Count { get { if (_clips == null) Load(); return _clips.Count; } }

        static void Load()
        {
            _clips = new Dictionary<string, Clip>();
            try
            {
                var asm = typeof(EmoteClips).Assembly;
                var resName = System.Array.Find(asm.GetManifestResourceNames(), n => n.EndsWith("Emotes.bin")) ?? "SubnauticaMP.Emotes.bin";
                using (var res = asm.GetManifestResourceStream(resName))
                {
                    if (res == null) { Plugin.Log.LogWarning("Emote clips missing from the mod; dances won't play"); return; }
                    using (var z = new DeflateStream(SkipZlibHeader(res), CompressionMode.Decompress))
                    using (var r = new BinaryReader(z))
                    {
                        if (System.Text.Encoding.ASCII.GetString(r.ReadBytes(4)) != "SNEM") throw new InvalidDataException("bad header");
                        r.ReadByte(); // version
                        int count = r.ReadUInt16();
                        for (int c = 0; c < count; c++)
                        {
                            var name = System.Text.Encoding.ASCII.GetString(r.ReadBytes(r.ReadByte()));
                            var clip = new Clip { Name = name, Fps = r.ReadByte() };
                            int n = r.ReadUInt16();
                            clip.Loops = r.ReadByte() != 0;
                            clip.Torso = new Quaternion[n];
                            clip.Hips = new Vector3[n];
                            clip.Dirs = new Vector3[n][];
                            for (int i = 0; i < n; i++)
                            {
                                clip.Torso[i] = Quaternion.Normalize(new Quaternion(S(r), S(r), S(r), S(r)));
                                clip.Hips[i] = new Vector3(r.ReadInt16() / 1000f, r.ReadInt16() / 1000f, r.ReadInt16() / 1000f);
                                var d = new Vector3[Segments];
                                for (int k = 0; k < Segments; k++) d[k] = new Vector3(S(r), S(r), S(r)).normalized;
                                clip.Dirs[i] = d;
                            }
                            _clips[name] = clip;
                        }
                    }
                }
                Plugin.Log.LogInfo($"Loaded {_clips.Count} emote animations");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Couldn't load emote animations: " + e.GetBaseException().Message);
            }
        }

        static float S(BinaryReader r) => r.ReadSByte() / 127f;

        // python's zlib.compress adds a 2-byte header that .NET's DeflateStream doesn't want
        static Stream SkipZlibHeader(Stream s) { s.ReadByte(); s.ReadByte(); return s; }

        // The pose at `time` seconds (loops wrap around, one-shots hold the last frame).
        public static void Sample(Clip clip, float time, ref Pose pose)
        {
            if (pose.Dirs == null) pose.Dirs = new Vector3[Segments];
            int n = clip.Torso.Length;
            float f = time * clip.Fps;
            if (clip.Loops) f = Mathf.Repeat(f, n);
            else f = Mathf.Clamp(f, 0f, n - 1);
            int a = Mathf.Min((int)f, n - 1);
            int b = clip.Loops ? (a + 1) % n : Mathf.Min(a + 1, n - 1);
            float t = f - a;
            pose.Torso = Quaternion.Slerp(clip.Torso[a], clip.Torso[b], t);
            pose.Hips = Vector3.Lerp(clip.Hips[a], clip.Hips[b], t);
            for (int k = 0; k < Segments; k++) pose.Dirs[k] = Vector3.Slerp(clip.Dirs[a][k], clip.Dirs[b][k], t);
        }
    }
}
