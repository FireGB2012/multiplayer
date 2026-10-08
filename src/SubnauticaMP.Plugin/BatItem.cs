using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace SubnauticaMP
{
    // The Titanium Bat: a new item (4 Titanium in the Fabricator, Personal > Tools), made with Nautilus.
    // It's the Survival Knife underneath (so it equips, sits in the hand, drops and saves like any tool) with
    // the knife model swapped for Gabriel's bat (Bat.bin, made by tools/bat/make_bat.py) and the stabbing off.
    // BatSync does the swinging; hits launch other players (no damage).
    //
    // Nautilus is only there at runtime (the launcher installs it), and its methods take game types we don't
    // compile against, so it's all done by name. Without Nautilus there's simply no bat.
    internal static class BatItem
    {
        public const string TechName = "TitaniumBat";
        const string DisplayName = "Titanium Bat";
        const string Description = "A heavy titanium bat. Swing it at a friend to send them flying. Look up for a home run.";

        public static bool Registered { get; private set; }

        // Model data from Bat.bin (model space: grip at 0, bat pointing +Y)
        static Vector3[] _verts, _normals;
        static int[][] _tris;
        static Color[] _colors;
        static bool[] _glow;
        static float[] _smooth;
        static string[] _parts;
        public static float TipY { get; private set; } = 0.68f;
        public static float ButtY { get; private set; } = -0.16f;
        static float _radius = 0.036f;
        static byte[] _icon;
        static int _iconW, _iconH;

        static Mesh _mesh;
        static Material[] _materials;

        public static bool IsBat(string tech) => tech == TechName;

        public static void Register()
        {
            if (!NautilusCompat.NautilusLoaded)
            {
                Plugin.Log.LogInfo("Nautilus isn't installed: no Titanium Bat (the launcher installs it when you press PLAY)");
                return;
            }
            try
            {
                Load();
                RegisterWithNautilus();
                Registered = true;
                Plugin.Log.LogInfo("Titanium Bat added (Fabricator > Personal > Tools, 4 Titanium)");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Couldn't add the Titanium Bat: " + e.GetBaseException());
            }
        }

        // ---------- Bat.bin ----------

        static void Load()
        {
            var asm = typeof(BatItem).Assembly;
            var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("Bat.bin")) ?? "SubnauticaMP.Bat.bin";
            using (var res = asm.GetManifestResourceStream(name))
            {
                if (res == null) throw new FileNotFoundException("Bat.bin missing from the mod");
                res.ReadByte(); res.ReadByte(); // zlib header
                using (var z = new DeflateStream(res, CompressionMode.Decompress))
                using (var r = new BinaryReader(z))
                {
                    if (System.Text.Encoding.ASCII.GetString(r.ReadBytes(4)) != "SNBT") throw new InvalidDataException("bad Bat.bin");
                    r.ReadByte(); // version
                    int n = (int)r.ReadUInt32();
                    _verts = new Vector3[n];
                    _normals = new Vector3[n];
                    float butt = 0f;
                    for (int i = 0; i < n; i++)
                    {
                        _verts[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                        _normals[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                        butt = Mathf.Min(butt, _verts[i].y);
                    }
                    ButtY = butt;
                    TipY = r.ReadSingle();
                    r.ReadSingle(); // half the grip's length
                    _radius = r.ReadSingle();
                    int subs = r.ReadByte();
                    _tris = new int[subs][]; _colors = new Color[subs]; _glow = new bool[subs]; _smooth = new float[subs]; _parts = new string[subs];
                    for (int s = 0; s < subs; s++)
                    {
                        _parts[s] = System.Text.Encoding.ASCII.GetString(r.ReadBytes(r.ReadByte()));
                        _colors[s] = new Color(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), 1f);
                        _glow[s] = r.ReadByte() != 0;
                        _smooth[s] = r.ReadSingle();
                        int count = (int)r.ReadUInt32();
                        var idx = new int[count];
                        for (int i = 0; i < count; i++) idx[i] = r.ReadUInt16();
                        _tris[s] = idx;
                    }
                    _iconW = r.ReadUInt16();
                    _iconH = r.ReadUInt16();
                    _icon = r.ReadBytes(_iconW * _iconH * 4);
                }
            }
        }

        static Mesh BatMesh()
        {
            if (_mesh != null) return _mesh;
            _mesh = new Mesh { name = "TitaniumBat" };
            _mesh.vertices = _verts;
            _mesh.normals = _normals;
            _mesh.uv = new Vector2[_verts.Length];
            _mesh.subMeshCount = _tris.Length;
            for (int s = 0; s < _tris.Length; s++) _mesh.SetTriangles(_tris[s], s);
            _mesh.RecalculateBounds();
            _mesh.UploadMeshData(false);
            return _mesh;
        }

        // The knife's own (Marmoset) material, recoloured per part: lit like everything else in the game.
        static Material[] Materials(Material knife)
        {
            if (_materials != null) return _materials;
            var shader = knife != null ? knife.shader : Shader.Find("MarmosetUBER");
            _materials = new Material[_tris.Length];
            for (int s = 0; s < _tris.Length; s++)
            {
                var m = knife != null ? new Material(knife) : new Material(shader);
                m.name = "TitaniumBat_" + _parts[s];
                var c = _colors[s].gamma; // the model's colours are linear
                Tex(m, "_MainTex", Texture2D.whiteTexture);
                Col(m, "_Color", c);
                m.DisableKeyword("MARMO_NORMALMAP");
                Tex(m, "_BumpMap", null);
                // shine: chrome a lot, the rubber grip hardly at all
                float sm = _smooth[s];
                m.EnableKeyword("MARMO_SPECMAP");
                Tex(m, "_SpecTex", Texture2D.whiteTexture);
                Col(m, "_SpecColor", Color.Lerp(Color.white, c, 0.35f));
                Num(m, "_SpecInt", Mathf.Lerp(0.1f, 2.2f, sm * sm));
                Num(m, "_Shininess", Mathf.Lerp(2.5f, 7.5f, sm));
                Num(m, "_Fresnel", Mathf.Lerp(0.05f, 0.45f, sm));
                if (_glow[s])
                {
                    m.EnableKeyword("MARMO_EMISSION");
                    Num(m, "_EnableGlow", 1f);
                    Tex(m, "_Illum", Texture2D.whiteTexture);
                    Col(m, "_GlowColor", c);
                    Num(m, "_GlowStrength", 1.6f);
                    Num(m, "_GlowStrengthNight", 1.6f);
                    Num(m, "_EmissionLM", 0f);
                    Num(m, "_EmissionLMNight", 0f);
                }
                else
                {
                    m.DisableKeyword("MARMO_EMISSION");
                    Num(m, "_EnableGlow", 0f);
                    Num(m, "_GlowStrength", 0f);
                    Num(m, "_GlowStrengthNight", 0f);
                }
                _materials[s] = m;
            }
            return _materials;
        }

        static void Tex(Material m, string p, Texture t) { if (m.HasProperty(p)) m.SetTexture(p, t); }
        static void Col(Material m, string p, Color c) { if (m.HasProperty(p)) m.SetColor(p, c); }
        static void Num(Material m, string p, float v) { if (m.HasProperty(p)) m.SetFloat(p, v); }

        static Sprite Icon()
        {
            var tex = new Texture2D(_iconW, _iconH, TextureFormat.RGBA32, false) { name = "TitaniumBatIcon", wrapMode = TextureWrapMode.Clamp };
            tex.LoadRawTextureData(_icon);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, _iconW, _iconH), new Vector2(0.5f, 0.5f));
        }

        // ---------- Nautilus ----------

        static void RegisterWithNautilus()
        {
            var naut = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Nautilus")
                       ?? throw new InvalidOperationException("Nautilus.dll not loaded");
            Type T(Assembly a, string n) => a.GetType(n, true);
            var game = Game.TechType.Assembly;
            var prefabInfoT = T(naut, "Nautilus.Assets.PrefabInfo");
            var customPrefabT = T(naut, "Nautilus.Assets.CustomPrefab");
            var cloneT = T(naut, "Nautilus.Assets.PrefabTemplates.CloneTemplate");
            var gadgetsT = T(naut, "Nautilus.Assets.Gadgets.GadgetExtensions");
            var recipeT = T(naut, "Nautilus.Crafting.RecipeData");
            var ingredientT = T(game, "Ingredient");
            var craftTreeTypeT = T(game, "CraftTree+Type");
            var equipmentT = T(game, "EquipmentType");

            // PrefabInfo.WithTechType(classId, name, description, language, unlockAtStart, owner)
            var withTech = prefabInfoT.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == "WithTechType" && m.GetParameters().Length == 6);
            var info = withTech.Invoke(null, new object[] { TechName, DisplayName, Description, "English", true, typeof(BatItem).Assembly });
            info = prefabInfoT.GetMethod("WithIcon").Invoke(info, new object[] { Icon() });
            try
            {
                var sizeT = T(game, "Vector2int");
                var size = Activator.CreateInstance(sizeT, 1, 2); // a bat is long: two slots tall
                info = prefabInfoT.GetMethod("WithSizeInInventory").Invoke(info, new[] { size });
            }
            catch (Exception e) { Game.WarnOnce("bat size", "Bat stays 1x1 in the inventory: " + e.GetBaseException().Message); }

            var techType = prefabInfoT.GetProperty("TechType").GetValue(info, null);
            var prefab = Activator.CreateInstance(customPrefabT, info);

            // the knife, with our model in it
            var knife = Enum.Parse(Game.TechType, "Knife");
            var clone = cloneT.GetConstructor(new[] { prefabInfoT, Game.TechType }).Invoke(new[] { info, knife });
            cloneT.GetProperty("ModifyPrefab").SetValue(clone, (Action<GameObject>)BuildPrefab, null);
            customPrefabT.GetMethods().First(m => m.Name == "SetGameObject" && m.GetParameters()[0].ParameterType.IsAssignableFrom(cloneT))
                .Invoke(prefab, new[] { clone });

            // 4 Titanium in the Fabricator, next to the knife
            var titanium = Enum.Parse(Game.TechType, "Titanium");
            var ingredients = Array.CreateInstance(ingredientT, 1);
            ingredients.SetValue(Activator.CreateInstance(ingredientT, titanium, 4), 0);
            var recipe = recipeT.GetConstructor(new[] { ingredients.GetType() }).Invoke(new object[] { ingredients });
            var crafting = gadgetsT.GetMethod("SetRecipe").Invoke(null, new[] { prefab, recipe });
            var craftingT = crafting.GetType();
            craftingT.GetMethod("WithFabricatorType").Invoke(crafting, new[] { Enum.Parse(craftTreeTypeT, "Fabricator") });
            craftingT.GetMethod("WithStepsToFabricatorTab").Invoke(crafting, new object[] { new[] { "Personal", "Tools" } });
            craftingT.GetMethod("WithCraftingTime").Invoke(crafting, new object[] { 3f });

            // goes in your hands / quick slots like the knife
            gadgetsT.GetMethod("SetEquipment").Invoke(null, new[] { prefab, Enum.Parse(equipmentT, "Hand") });

            customPrefabT.GetMethod("Register").Invoke(prefab, null);
            Plugin.Log.LogInfo("Titanium Bat is TechType " + techType);
        }

        // Turns a copy of the knife into the bat. Runs whenever the game builds the item.
        static void BuildPrefab(GameObject go)
        {
            var root = go.transform;
            var toRoot = root.worldToLocalMatrix;

            // which way the knife points out of the hand: its longest side, toward the far end (the blade)
            var renderers = go.GetComponentsInChildren<Renderer>(true).Where(r => r is MeshRenderer || r is SkinnedMeshRenderer).ToArray();
            Material knifeMat = null;
            Vector3 min = Vector3.one * float.MaxValue, max = Vector3.one * float.MinValue;
            foreach (var r in renderers)
            {
                if (knifeMat == null && r.sharedMaterial != null) knifeMat = r.sharedMaterial;
                Bounds b;
                if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null) b = smr.sharedMesh.bounds;
                else if (r.GetComponent<MeshFilter>() is MeshFilter mf && mf.sharedMesh != null) b = mf.sharedMesh.bounds;
                else continue;
                var m = toRoot * r.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var p = m.MultiplyPoint3x4(new Vector3(i % 2 == 0 ? b.min.x : b.max.x, (i / 2) % 2 == 0 ? b.min.y : b.max.y, i / 4 == 0 ? b.min.z : b.max.z));
                    min = Vector3.Min(min, p); max = Vector3.Max(max, p);
                }
            }
            var axis = Vector3.forward;
            if (min.x <= max.x)
            {
                var size = max - min;
                int a = size.x > size.y ? (size.x > size.z ? 0 : 2) : (size.y > size.z ? 1 : 2);
                axis = Vector3.zero;
                axis[a] = Mathf.Abs(max[a]) >= Mathf.Abs(min[a]) ? 1f : -1f;
            }

            // where the fabricator hologram lives (the knife's model object): the bat goes under it too
            var vfxT = Game.Find("VFXFabricating");
            var vfx = vfxT != null ? go.GetComponentInChildren(vfxT, true) : null;

            foreach (var r in renderers)
            {
                var mf = r.GetComponent<MeshFilter>();
                UnityEngine.Object.DestroyImmediate(r);
                if (mf != null) UnityEngine.Object.DestroyImmediate(mf);
            }
            foreach (var lod in go.GetComponentsInChildren<LODGroup>(true)) UnityEngine.Object.DestroyImmediate(lod);
            foreach (var col in go.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(col);

            var model = new GameObject("TitaniumBatModel");
            model.transform.SetParent(root, false);
            model.transform.localRotation = Quaternion.FromToRotation(Vector3.up, axis);
            if (vfx != null) model.transform.SetParent(vfx.transform, true);
            model.AddComponent<MeshFilter>().sharedMesh = BatMesh();
            var mr = model.AddComponent<MeshRenderer>();
            mr.sharedMaterials = Materials(knifeMat);

            var grip = new GameObject("BatGrip").transform;
            grip.SetParent(model.transform, false);
            var tip = new GameObject("BatTip").transform;
            tip.SetParent(model.transform, false);
            tip.localPosition = new Vector3(0f, TipY, 0f);

            var cap = model.AddComponent<CapsuleCollider>();
            cap.direction = 1;
            cap.radius = _radius + 0.008f;
            cap.height = TipY - ButtY;
            cap.center = new Vector3(0f, (TipY + ButtY) / 2f, 0f);

            // lit by the world like the other tools
            var skyT = Game.Find("SkyApplier");
            if (skyT != null)
                foreach (var sky in go.GetComponentsInChildren(skyT, true))
                    Game.TryDo("bat sky", () => Game.Set(skyT, sky, "renderers", new Renderer[] { mr }));

            // fabricator hologram: sweep over the bat's height
            if (vfx != null)
            {
                var toVfx = vfx.transform.worldToLocalMatrix * model.transform.localToWorldMatrix;
                float lo = float.MaxValue, hi = float.MinValue;
                foreach (var y in new[] { ButtY, TipY })
                    foreach (var x in new[] { -_radius, _radius })
                    {
                        var p = toVfx.MultiplyPoint3x4(new Vector3(x, y, 0f));
                        lo = Mathf.Min(lo, p.y); hi = Mathf.Max(hi, p.y);
                    }
                Game.TryDo("bat hologram", () => { Game.Set(vfxT, vfx, "localMinY", lo - 0.02f); Game.Set(vfxT, vfx, "localMaxY", hi + 0.02f); });
            }

            // no stabbing: the bat launches players instead (BatSync)
            var knifeT = Game.Knife;
            var knife = knifeT != null ? go.GetComponent(knifeT) : null;
            if (knife != null)
            {
                Game.TryDo("bat damage", () => Game.Set(knifeT, knife, "damage", 0f));
                // its sounds: the bat uses them for the swing / the hit
                if (_sounds[0] == null)
                {
                    _sounds[(int)Sound.Hit] = Game.TryGet(knifeT, knife, "attackSound");
                    _sounds[(int)Sound.MissWater] = Game.TryGet(knifeT, knife, "underwaterMissSound");
                    _sounds[(int)Sound.MissAir] = Game.TryGet(knifeT, knife, "surfaceMissSound");
                }
            }
        }

        // ---------- sounds (the knife's, from the copy above) ----------

        public enum Sound { Hit, MissWater, MissAir }
        static readonly object[] _sounds = new object[3];
        static Type _fmod;

        public static void PlaySound(Sound which, Vector3 at)
        {
            var asset = _sounds[(int)which];
            if (asset == null) return;
            if (_fmod == null) _fmod = Game.Find("FMODUWE");
            if (_fmod == null) return;
            Game.TryDo("bat sound", () => Game.Call(_fmod, null, "PlayOneShot", asset, at));
        }
    }
}
