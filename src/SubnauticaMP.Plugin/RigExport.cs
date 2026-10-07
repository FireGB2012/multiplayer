using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace SubnauticaMP
{
    // Dev tool for making your own animations: press the RigExportKey (F10) in a world and it writes the live
    // diver (the same "body" the mod copies for other players: first-person arms + body skeleton) to
    //   BepInEx\plugins\SubnauticaMP\RigExport\
    //     diver_rig.glb             mesh + skeleton + textures, no animations (small, open this in Blender first)
    //     diver_with_anims.glb      same, plus every animation clip on the diver's animator, sampled at 30 fps
    //     textures\*.png            the textures used, on their own
    //     rig_info.txt              bone paths, mesh / clip list, animator parameters, anything that was skipped
    // glTF is right-handed, Unity left-handed: positions / normals / rotations are mirrored on X and the
    // triangle winding flipped so it shows up the right way round in Blender.
    internal static class RigExport
    {
        // player = the local Player component, outDir = where the files go, say = shows a message to the user.
        // (Also compiled into the standalone tools/rigdump plugin, so it can't lean on the rest of the mod.)
        public static void Run(Component player, string outDir, string version, Action<string> say)
        {
            try
            {
                var body = player != null ? player.transform.Find("body") : null;
                if (body == null) { say("Rig export: load into a world first (couldn't find the player's body)."); return; }

                var dir = outDir;
                Directory.CreateDirectory(dir);
                var info = new StringBuilder();
                info.AppendLine("Subnautica rig export " + version);
                info.AppendLine("Root: " + body.name + " (everything below is relative to it, units = meters)");
                info.AppendLine();

                var glb = new GlbBuilder(body, info);
                glb.AddRenderers();
                File.WriteAllBytes(Path.Combine(dir, "diver_rig.glb"), glb.ToGlb());

                if (glb.PngFiles.Count > 0)
                {
                    var texDir = Path.Combine(dir, "textures");
                    Directory.CreateDirectory(texDir);
                    foreach (var kv in glb.PngFiles) File.WriteAllBytes(Path.Combine(texDir, kv.Key), kv.Value);
                }

                int clips = glb.AddAnimations();
                File.WriteAllBytes(Path.Combine(dir, "diver_with_anims.glb"), glb.ToGlb());

                glb.WriteBoneList();
                File.WriteAllText(Path.Combine(dir, "rig_info.txt"), info.ToString());

                say($"Rig exported ({glb.MeshCount} meshes, {glb.NodeCount} bones/nodes, {clips} animations) to: {dir}");
            }
            catch (Exception e)
            {
                say("Rig export failed: " + e.GetBaseException().Message + " (see the BepInEx log)");
                UnityEngine.Debug.LogError("Rig export failed: " + e);
            }
        }

        sealed class GlbBuilder
        {
            const int Fps = 30;
            const int MaxFrames = 900;      // 30 s per clip
            const int MaxTexture = 2048;

            readonly Transform _root;
            readonly StringBuilder _info;
            readonly List<Transform> _nodes = new List<Transform>();
            readonly Dictionary<Transform, int> _index = new Dictionary<Transform, int>();
            readonly MemoryStream _bin = new MemoryStream();
            readonly List<string> _views = new List<string>();
            readonly List<string> _accessors = new List<string>();
            readonly List<string> _meshes = new List<string>();
            readonly List<string> _skins = new List<string>();
            readonly List<string> _materials = new List<string>();
            readonly List<string> _textures = new List<string>();
            readonly List<string> _images = new List<string>();
            readonly List<string> _animations = new List<string>();
            readonly Dictionary<int, int> _meshOfNode = new Dictionary<int, int>();
            readonly Dictionary<int, int> _skinOfNode = new Dictionary<int, int>();
            readonly Dictionary<Material, int> _materialIndex = new Dictionary<Material, int>();
            readonly Dictionary<Texture, int> _textureIndex = new Dictionary<Texture, int>();
            readonly SortedSet<int> _joints = new SortedSet<int>();

            public readonly Dictionary<string, byte[]> PngFiles = new Dictionary<string, byte[]>();
            public int MeshCount => _meshes.Count;
            public int NodeCount => _nodes.Count;

            public GlbBuilder(Transform root, StringBuilder info)
            {
                _root = root;
                _info = info;
                Collect(root);
            }

            void Collect(Transform t)
            {
                _index[t] = _nodes.Count;
                _nodes.Add(t);
                for (int i = 0; i < t.childCount; i++) Collect(t.GetChild(i));
            }

            // ---------- meshes ----------

            public void AddRenderers()
            {
                int sectionStart = _info.Length;
                _info.AppendLine("== Meshes ==");
                foreach (var smr in _root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    AddMesh(smr, smr.sharedMesh, smr);
                foreach (var mr in _root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var mf = mr.GetComponent<MeshFilter>();
                    if (mf != null) AddMesh(mr, mf.sharedMesh, null);
                }
                if (_skins.Count == 0) AddPlaceholderSkin();
                _info.AppendLine();
                UnityEngine.Debug.Log("[RigExport] " + _info.ToString(sectionStart, _info.Length - sectionStart)); // lands in LogOutput.log
            }

            // No readable skinned mesh came out (the game strips vertex data from most meshes). Blender only builds an
            // armature (real bones you can pose / animate) when a skinned mesh uses the skeleton, so add a tiny
            // one-triangle mesh weighted to the root and a skin that lists every node as a joint.
            void AddPlaceholderSkin()
            {
                _info.AppendLine("No readable skinned mesh: added a tiny placeholder triangle skinned to every bone so Blender builds an armature.");
                var pos = new float[] { 0, 0, 0, 0.001f, 0, 0, 0, 0.001f, 0 };
                var attrs = "\"POSITION\":" + Acc(View(Bytes(pos), 34962), 5126, 3, "VEC3", "[0,0,0]", "[0.001,0.001,0]")
                          + ",\"JOINTS_0\":" + Acc(View(Bytes(new ushort[12]), 34962), 5123, 3, "VEC4")
                          + ",\"WEIGHTS_0\":" + Acc(View(Bytes(new float[] { 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0 }), 34962), 5126, 3, "VEC4");
                int indices = Acc(View(Bytes(new uint[] { 0, 1, 2 }), 34963), 5125, 3, "SCALAR");
                _meshes.Add("{\"name\":\"RigPlaceholder\",\"primitives\":[{\"attributes\":{" + attrs + "},\"indices\":" + indices + "}]}");

                var jointIdx = new List<int>();
                var ibm = new float[_nodes.Count * 16];
                for (int n = 0; n < _nodes.Count; n++)
                {
                    jointIdx.Add(n);
                    _joints.Add(n);
                    var m = (_root.worldToLocalMatrix * _nodes[n].localToWorldMatrix).inverse; // bone -> root space, inverted
                    for (int c = 0; c < 4; c++)
                        for (int row = 0; row < 4; row++)
                        {
                            float v = m[row, c];
                            if ((row == 0) != (c == 0)) v = -v; // mirror X: M' = S M S
                            ibm[n * 16 + c * 4 + row] = v;
                        }
                }
                int ibmAcc = Acc(View(Bytes(ibm)), 5126, _nodes.Count, "MAT4");
                _skins.Add("{\"name\":\"RigPlaceholder\",\"joints\":[" + string.Join(",", jointIdx.Select(j => j.ToString()).ToArray()) + "],\"inverseBindMatrices\":" + ibmAcc + "}");
                _meshOfNode[0] = _meshes.Count - 1;
                _skinOfNode[0] = _skins.Count - 1;
            }

            void AddMesh(Renderer r, Mesh mesh, SkinnedMeshRenderer smr)
            {
                var path = PathOf(r.transform);
                if (mesh == null) return;
                if (!mesh.isReadable)
                {
                    _info.AppendLine($"SKIPPED  {path}  mesh '{mesh.name}' isn't readable (the game compiled it without Read/Write) - grab this one with AssetRipper");
                    return;
                }
                int vc = mesh.vertexCount;
                var verts = mesh.vertices;
                var normals = mesh.normals;
                var uvs = mesh.uv;
                var pos = new float[vc * 3];
                float[] min = { float.MaxValue, float.MaxValue, float.MaxValue }, max = { float.MinValue, float.MinValue, float.MinValue };
                for (int i = 0; i < vc; i++)
                {
                    float x = -verts[i].x, y = verts[i].y, z = verts[i].z;
                    pos[i * 3] = x; pos[i * 3 + 1] = y; pos[i * 3 + 2] = z;
                    min[0] = Mathf.Min(min[0], x); min[1] = Mathf.Min(min[1], y); min[2] = Mathf.Min(min[2], z);
                    max[0] = Mathf.Max(max[0], x); max[1] = Mathf.Max(max[1], y); max[2] = Mathf.Max(max[2], z);
                }
                var attrs = new StringBuilder();
                attrs.Append("\"POSITION\":" + Acc(View(Bytes(pos), 34962), 5126, vc, "VEC3", Arr(min), Arr(max)));

                if (normals.Length == vc)
                {
                    var nrm = new float[vc * 3];
                    for (int i = 0; i < vc; i++) { nrm[i * 3] = -normals[i].x; nrm[i * 3 + 1] = normals[i].y; nrm[i * 3 + 2] = normals[i].z; }
                    attrs.Append(",\"NORMAL\":" + Acc(View(Bytes(nrm), 34962), 5126, vc, "VEC3"));
                }
                if (uvs.Length == vc)
                {
                    var uv = new float[vc * 2];
                    for (int i = 0; i < vc; i++) { uv[i * 2] = uvs[i].x; uv[i * 2 + 1] = 1f - uvs[i].y; }
                    attrs.Append(",\"TEXCOORD_0\":" + Acc(View(Bytes(uv), 34962), 5126, vc, "VEC2"));
                }

                int skin = -1;
                if (smr != null && smr.bones.Length > 0 && mesh.boneWeights.Length == vc && mesh.bindposes.Length == smr.bones.Length)
                {
                    var bw = mesh.boneWeights;
                    var joints = new ushort[vc * 4];
                    var weights = new float[vc * 4];
                    for (int i = 0; i < vc; i++)
                    {
                        float w0 = bw[i].weight0, w1 = bw[i].weight1, w2 = bw[i].weight2, w3 = bw[i].weight3;
                        float sum = w0 + w1 + w2 + w3;
                        joints[i * 4] = (ushort)bw[i].boneIndex0; joints[i * 4 + 1] = (ushort)bw[i].boneIndex1;
                        joints[i * 4 + 2] = (ushort)bw[i].boneIndex2; joints[i * 4 + 3] = (ushort)bw[i].boneIndex3;
                        if (sum <= 1e-6f) { weights[i * 4] = 1f; joints[i * 4] = 0; continue; } // unweighted vertex: stick it to bone 0
                        weights[i * 4] = w0 / sum; weights[i * 4 + 1] = w1 / sum; weights[i * 4 + 2] = w2 / sum; weights[i * 4 + 3] = w3 / sum;
                    }
                    attrs.Append(",\"JOINTS_0\":" + Acc(View(Bytes(joints), 34962), 5123, vc, "VEC4"));
                    attrs.Append(",\"WEIGHTS_0\":" + Acc(View(Bytes(weights), 34962), 5126, vc, "VEC4"));

                    var bones = smr.bones;
                    var jointIdx = new List<int>();
                    foreach (var b in bones)
                    {
                        int ji = b != null && _index.TryGetValue(b, out var found) ? found : 0;
                        jointIdx.Add(ji);
                        _joints.Add(ji);
                    }
                    var ibm = new float[bones.Length * 16];
                    for (int b = 0; b < bones.Length; b++)
                    {
                        var m = mesh.bindposes[b];
                        for (int c = 0; c < 4; c++)
                            for (int row = 0; row < 4; row++)
                            {
                                float v = m[row, c];
                                if ((row == 0) != (c == 0)) v = -v; // mirror X: M' = S M S
                                ibm[b * 16 + c * 4 + row] = v;
                            }
                    }
                    int ibmAcc = Acc(View(Bytes(ibm)), 5126, bones.Length, "MAT4");
                    _skins.Add("{\"name\":\"" + Esc(smr.name) + "\",\"joints\":[" + string.Join(",", jointIdx.Select(j => j.ToString()).ToArray()) + "],\"inverseBindMatrices\":" + ibmAcc + "}");
                    skin = _skins.Count - 1;
                }

                var mats = r.sharedMaterials;
                var prims = new List<string>();
                for (int sm = 0; sm < mesh.subMeshCount; sm++)
                {
                    if (mesh.GetTopology(sm) != MeshTopology.Triangles) continue;
                    var tris = mesh.GetTriangles(sm);
                    if (tris.Length == 0) continue;
                    var idx = new uint[tris.Length];
                    for (int i = 0; i + 2 < tris.Length; i += 3) { idx[i] = (uint)tris[i]; idx[i + 1] = (uint)tris[i + 2]; idx[i + 2] = (uint)tris[i + 1]; } // mirrored: flip winding
                    var prim = "{\"attributes\":{" + attrs + "},\"indices\":" + Acc(View(Bytes(idx), 34963), 5125, idx.Length, "SCALAR");
                    if (mats.Length > 0)
                    {
                        int mi = MaterialIndex(mats[Mathf.Min(sm, mats.Length - 1)]);
                        if (mi >= 0) prim += ",\"material\":" + mi;
                    }
                    prims.Add(prim + "}");
                }
                if (prims.Count == 0) return;

                _meshes.Add("{\"name\":\"" + Esc(mesh.name) + "\",\"primitives\":[" + string.Join(",", prims.ToArray()) + "]}");
                int node = _index[r.transform];
                _meshOfNode[node] = _meshes.Count - 1;
                if (skin >= 0) _skinOfNode[node] = skin;
                _info.AppendLine($"{(skin >= 0 ? "SKINNED " : "static  ")}  {path}  mesh '{mesh.name}' {vc} verts, {prims.Count} submeshes{(r.enabled && r.gameObject.activeInHierarchy ? "" : "  [hidden in game]")}");
            }

            int MaterialIndex(Material m)
            {
                if (m == null) return -1;
                if (_materialIndex.TryGetValue(m, out var existing)) return existing;
                var pbr = new StringBuilder("{");
                var color = m.HasProperty("_Color") ? m.color.linear : Color.white;
                pbr.Append($"\"baseColorFactor\":[{F(color.r)},{F(color.g)},{F(color.b)},{F(color.a)}],\"metallicFactor\":0,\"roughnessFactor\":1");
                var tex = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
                int ti = tex != null ? TextureIndex(tex) : -1;
                if (ti >= 0) pbr.Append(",\"baseColorTexture\":{\"index\":" + ti + "}");
                pbr.Append("}");
                _materials.Add("{\"name\":\"" + Esc(m.name) + "\",\"doubleSided\":true,\"pbrMetallicRoughness\":" + pbr + "}");
                _materialIndex[m] = _materials.Count - 1;
                return _materials.Count - 1;
            }

            int TextureIndex(Texture tex)
            {
                if (_textureIndex.TryGetValue(tex, out var existing)) return existing;
                int result = -1;
                try
                {
                    var png = ToPng(tex);
                    if (png != null)
                    {
                        var file = Safe(tex.name) + ".png";
                        for (int n = 2; PngFiles.ContainsKey(file); n++) file = Safe(tex.name) + "_" + n + ".png";
                        PngFiles[file] = png;
                        int view = View(png);
                        _images.Add("{\"name\":\"" + Esc(tex.name) + "\",\"bufferView\":" + view + ",\"mimeType\":\"image/png\"}");
                        _textures.Add("{\"sampler\":0,\"source\":" + (_images.Count - 1) + "}");
                        result = _textures.Count - 1;
                    }
                }
                catch (Exception e) { _info.AppendLine($"Texture '{tex.name}' couldn't be exported: {e.Message}"); }
                _textureIndex[tex] = result;
                return result;
            }

            // Works on textures the game compiled as non-readable too: draw it on the GPU, read the picture back.
            static byte[] ToPng(Texture tex)
            {
                float scale = Mathf.Min(1f, MaxTexture / (float)Mathf.Max(tex.width, tex.height));
                int w = Mathf.Max(1, Mathf.RoundToInt(tex.width * scale)), h = Mathf.Max(1, Mathf.RoundToInt(tex.height * scale));
                var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
                var previous = RenderTexture.active;
                try
                {
                    Graphics.Blit(tex, rt);
                    RenderTexture.active = rt;
                    var copy = new Texture2D(w, h, TextureFormat.RGBA32, false);
                    copy.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                    copy.Apply();
                    var png = copy.EncodeToPNG();
                    UnityEngine.Object.Destroy(copy);
                    return png;
                }
                finally
                {
                    RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(rt);
                }
            }

            // ---------- animations ----------

            // Plays every clip of the diver's animator frame by frame and records where each bone ends up.
            public int AddAnimations()
            {
                _info.AppendLine("== Animations ==");
                var animator = _root.GetComponentInChildren<Animator>(true);
                if (animator == null || animator.runtimeAnimatorController == null)
                {
                    _info.AppendLine("No animator on the diver, so no clips.");
                    _info.AppendLine();
                    return 0;
                }
                _info.AppendLine("Animator on: " + PathOf(animator.transform));
                _info.AppendLine("Parameters: " + string.Join(", ", animator.parameters.Select(p => p.name + " (" + p.type + ")").ToArray()));

                var joints = _joints.Count > 0 ? _joints.Where(j => j != 0).ToList() : Enumerable.Range(1, _nodes.Count - 1).ToList();
                var jt = joints.Select(j => _nodes[j]).ToArray();

                var savedPos = new Vector3[_nodes.Count];
                var savedRot = new Quaternion[_nodes.Count];
                var savedScale = new Vector3[_nodes.Count];
                for (int i = 0; i < _nodes.Count; i++)
                {
                    savedPos[i] = _nodes[i].localPosition; savedRot[i] = _nodes[i].localRotation; savedScale[i] = _nodes[i].localScale;
                }
                bool wasEnabled = animator.enabled;
                animator.enabled = false;
                var done = new HashSet<string>();
                int count = 0;
                try
                {
                    foreach (var clip in animator.runtimeAnimatorController.animationClips)
                    {
                        if (clip == null || clip.length <= 0f || !done.Add(clip.name)) continue;
                        try { count += AddClip(clip, animator.gameObject, joints, jt); }
                        catch (Exception e) { _info.AppendLine($"Clip '{clip.name}' failed: {e.Message}"); }
                    }
                }
                finally
                {
                    for (int i = 0; i < _nodes.Count; i++)
                    {
                        _nodes[i].localPosition = savedPos[i]; _nodes[i].localRotation = savedRot[i]; _nodes[i].localScale = savedScale[i];
                    }
                    animator.enabled = wasEnabled;
                }
                _info.AppendLine();
                return count;
            }

            int AddClip(AnimationClip clip, GameObject sampleRoot, List<int> joints, Transform[] jt)
            {
                int frames = Mathf.Min(MaxFrames, Mathf.Max(2, Mathf.CeilToInt(clip.length * Fps) + 1));
                var times = new float[frames];
                var pos = new float[jt.Length][];
                var rot = new float[jt.Length][];
                for (int j = 0; j < jt.Length; j++) { pos[j] = new float[frames * 3]; rot[j] = new float[frames * 4]; }

                for (int f = 0; f < frames; f++)
                {
                    float t = Mathf.Min(f / (float)Fps, clip.length);
                    times[f] = t;
                    clip.SampleAnimation(sampleRoot, t);
                    for (int j = 0; j < jt.Length; j++)
                    {
                        var p = jt[j].localPosition;
                        var q = jt[j].localRotation;
                        pos[j][f * 3] = -p.x; pos[j][f * 3 + 1] = p.y; pos[j][f * 3 + 2] = p.z;
                        float qx = q.x, qy = -q.y, qz = -q.z, qw = q.w;
                        if (f > 0 && qx * rot[j][(f - 1) * 4] + qy * rot[j][(f - 1) * 4 + 1] + qz * rot[j][(f - 1) * 4 + 2] + qw * rot[j][(f - 1) * 4 + 3] < 0f)
                        { qx = -qx; qy = -qy; qz = -qz; qw = -qw; } // keep neighbouring frames on the same side so Blender doesn't spin the long way
                        rot[j][f * 4] = qx; rot[j][f * 4 + 1] = qy; rot[j][f * 4 + 2] = qz; rot[j][f * 4 + 3] = qw;
                    }
                }

                int timeAcc = Acc(View(Bytes(times)), 5126, frames, "SCALAR", Arr(new[] { times[0] }), Arr(new[] { times[frames - 1] }));
                var samplers = new List<string>();
                var channels = new List<string>();
                for (int j = 0; j < jt.Length; j++)
                {
                    int node = joints[j];
                    if (Changes(pos[j], 3))
                    {
                        samplers.Add("{\"input\":" + timeAcc + ",\"output\":" + Acc(View(Bytes(pos[j])), 5126, frames, "VEC3") + ",\"interpolation\":\"LINEAR\"}");
                        channels.Add("{\"sampler\":" + (samplers.Count - 1) + ",\"target\":{\"node\":" + node + ",\"path\":\"translation\"}}");
                    }
                    if (Changes(rot[j], 4))
                    {
                        samplers.Add("{\"input\":" + timeAcc + ",\"output\":" + Acc(View(Bytes(rot[j])), 5126, frames, "VEC4") + ",\"interpolation\":\"LINEAR\"}");
                        channels.Add("{\"sampler\":" + (samplers.Count - 1) + ",\"target\":{\"node\":" + node + ",\"path\":\"rotation\"}}");
                    }
                }
                _info.AppendLine($"{clip.name}  {clip.length:0.00}s  {frames} frames  {channels.Count} channels{(clip.isLooping ? "  loops" : "")}{(frames >= MaxFrames ? "  (cut at 30 s)" : "")}");
                if (channels.Count == 0) return 0;
                _animations.Add("{\"name\":\"" + Esc(clip.name) + "\",\"samplers\":[" + string.Join(",", samplers.ToArray()) + "],\"channels\":[" + string.Join(",", channels.ToArray()) + "]}");
                return 1;
            }

            static bool Changes(float[] data, int stride)
            {
                for (int i = stride; i < data.Length; i++)
                    if (Mathf.Abs(data[i] - data[i % stride]) > 1e-5f) return true;
                return false;
            }

            // ---------- output ----------

            public void WriteBoneList()
            {
                _info.AppendLine("== Bones / nodes (index  path) ==");
                for (int i = 0; i < _nodes.Count; i++)
                    _info.AppendLine($"{i,4}  {(_joints.Contains(i) ? "bone" : "    ")}  {PathOf(_nodes[i])}");
            }

            public byte[] ToGlb()
            {
                var children = new List<int>[_nodes.Count];
                for (int i = 0; i < _nodes.Count; i++)
                {
                    children[i] = new List<int>();
                    for (int c = 0; c < _nodes[i].childCount; c++) children[i].Add(_index[_nodes[i].GetChild(c)]);
                }

                var sb = new StringBuilder();
                sb.Append("{\"asset\":{\"version\":\"2.0\",\"generator\":\"SubnauticaMP RigExport\"},\"scene\":0,\"scenes\":[{\"nodes\":[0]}]");
                sb.Append(",\"nodes\":[");
                for (int i = 0; i < _nodes.Count; i++)
                {
                    var t = _nodes[i];
                    if (i > 0) sb.Append(',');
                    sb.Append("{\"name\":\"" + Esc(t.name) + "\"");
                    if (children[i].Count > 0) sb.Append(",\"children\":[" + string.Join(",", children[i].Select(c => c.ToString()).ToArray()) + "]");
                    if (i > 0)
                    {
                        var p = t.localPosition; var q = t.localRotation; var s = t.localScale;
                        sb.Append($",\"translation\":[{F(-p.x)},{F(p.y)},{F(p.z)}],\"rotation\":[{F(q.x)},{F(-q.y)},{F(-q.z)},{F(q.w)}],\"scale\":[{F(s.x)},{F(s.y)},{F(s.z)}]");
                    }
                    if (_meshOfNode.TryGetValue(i, out var mesh)) sb.Append(",\"mesh\":" + mesh);
                    if (_skinOfNode.TryGetValue(i, out var skin)) sb.Append(",\"skin\":" + skin);
                    sb.Append('}');
                }
                sb.Append(']');
                Section(sb, "meshes", _meshes);
                Section(sb, "skins", _skins);
                Section(sb, "materials", _materials);
                Section(sb, "textures", _textures);
                Section(sb, "images", _images);
                if (_textures.Count > 0) sb.Append(",\"samplers\":[{\"magFilter\":9729,\"minFilter\":9987,\"wrapS\":10497,\"wrapT\":10497}]");
                Section(sb, "animations", _animations);
                Section(sb, "accessors", _accessors);
                Section(sb, "bufferViews", _views);
                if (_bin.Length > 0) sb.Append(",\"buffers\":[{\"byteLength\":" + Pad4(_bin.Length) + "}]");
                sb.Append('}');

                var json = Encoding.UTF8.GetBytes(sb.ToString());
                int jsonLen = (int)Pad4(json.Length);
                int binLen = (int)Pad4(_bin.Length);
                int total = 12 + 8 + jsonLen + (binLen > 0 ? 8 + binLen : 0);

                using (var ms = new MemoryStream(total))
                using (var w = new BinaryWriter(ms))
                {
                    w.Write(0x46546C67u); w.Write(2u); w.Write((uint)total);
                    w.Write((uint)jsonLen); w.Write(0x4E4F534Au);
                    w.Write(json);
                    for (int i = json.Length; i < jsonLen; i++) w.Write((byte)0x20);
                    if (binLen > 0)
                    {
                        w.Write((uint)binLen); w.Write(0x004E4942u);
                        w.Write(_bin.GetBuffer(), 0, (int)_bin.Length);
                        for (long i = _bin.Length; i < binLen; i++) w.Write((byte)0);
                    }
                    return ms.ToArray();
                }
            }

            // ---------- helpers ----------

            static void Section(StringBuilder sb, string name, List<string> items)
            {
                if (items.Count > 0) sb.Append(",\"" + name + "\":[" + string.Join(",", items.ToArray()) + "]");
            }

            static long Pad4(long n) => (n + 3) & ~3L;

            int View(byte[] data, int target = 0)
            {
                while (_bin.Length % 4 != 0) _bin.WriteByte(0);
                int offset = (int)_bin.Length;
                _bin.Write(data, 0, data.Length);
                _views.Add("{\"buffer\":0,\"byteOffset\":" + offset + ",\"byteLength\":" + data.Length + (target != 0 ? ",\"target\":" + target : "") + "}");
                return _views.Count - 1;
            }

            int Acc(int view, int componentType, int count, string type, string min = null, string max = null)
            {
                _accessors.Add("{\"bufferView\":" + view + ",\"componentType\":" + componentType + ",\"count\":" + count + ",\"type\":\"" + type + "\"" +
                               (min != null ? ",\"min\":" + min : "") + (max != null ? ",\"max\":" + max : "") + "}");
                return _accessors.Count - 1;
            }

            static byte[] Bytes(Array a)
            {
                var b = new byte[Buffer.ByteLength(a)];
                Buffer.BlockCopy(a, 0, b, 0, b.Length);
                return b;
            }

            static string F(float v) => float.IsNaN(v) || float.IsInfinity(v) ? "0" : v.ToString("R", CultureInfo.InvariantCulture);

            static string Arr(float[] v) => "[" + string.Join(",", v.Select(F).ToArray()) + "]";

            string PathOf(Transform t)
            {
                if (t == _root) return _root.name;
                var parts = new List<string>();
                for (var c = t; c != null && c != _root; c = c.parent) parts.Add(c.name);
                parts.Reverse();
                return string.Join("/", parts.ToArray());
            }

            static string Safe(string name)
            {
                var sb = new StringBuilder();
                foreach (var c in string.IsNullOrEmpty(name) ? "texture" : name)
                    sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.' ? c : '_');
                return sb.ToString();
            }

            static string Esc(string s)
            {
                if (s == null) return "";
                var sb = new StringBuilder();
                foreach (var c in s)
                {
                    if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                    else if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                }
                return sb.ToString();
            }
        }
    }
}
