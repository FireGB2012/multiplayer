using System;
using System.Collections.Generic;
using SubnauticaMP.Shared;
using UnityEngine;

namespace SubnauticaMP
{
    // Suit colors: only the orange / yellow stripes and panels of the dive suit change color; the grey and white
    // parts stay as they are. Done by making a recolored copy of the suit's texture (once per texture + color,
    // then shared by everyone wearing that color).
    internal static class SuitPaint
    {
        const int MaxSize = 1024; // plenty for a teammate a few meters away, and 4x quicker to paint
        static readonly Dictionary<(int tex, int color), Texture2D> Cache = new Dictionary<(int, int), Texture2D>();
        static readonly HashSet<int> Failed = new HashSet<int>();

        // Paints this copied diver body in `color` (0xRRGGBB). The standard color puts the original textures back.
        public static void Apply(GameObject body, int color)
        {
            if (body == null) return;
            foreach (var r in body.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                Material[] mats;
                try { mats = r.materials; } // this diver's own copies, so others aren't affected
                catch { continue; }
                foreach (var m in mats)
                {
                    if (m == null || !m.HasProperty("_MainTex")) continue;
                    var original = Original(m);
                    if (original == null) continue;
                    m.SetTexture("_MainTex", color == DiverColors.Default ? original : Recolored(original, color) ?? original);
                }
            }
        }

        // The texture the material came with (we remember it, since we swap it out).
        static readonly Dictionary<Material, Texture> Originals = new Dictionary<Material, Texture>();

        static Texture Original(Material m)
        {
            if (Originals.TryGetValue(m, out var t) && t != null) return t;
            t = m.GetTexture("_MainTex");
            if (t != null && !Recoloured(t)) Originals[m] = t;
            return t;
        }

        static readonly HashSet<int> Ours = new HashSet<int>();
        static bool Recoloured(Texture t) => Ours.Contains(t.GetInstanceID());

        static Texture2D Recolored(Texture source, int color)
        {
            int id = source.GetInstanceID();
            if (Failed.Contains(id)) return null;
            if (Cache.TryGetValue((id, color), out var done) && done != null) return done;
            try
            {
                var pixels = Read(source, out int w, out int h);
                if (pixels == null) { Failed.Add(id); return null; }
                if (!HasOrange(pixels)) { Failed.Add(id); return null; } // no orange on this one (helmet glass, tank...)

                Color.RGBToHSV(new Color(((color >> 16) & 0xFF) / 255f, ((color >> 8) & 0xFF) / 255f, (color & 0xFF) / 255f), out var th, out var ts, out var tv);
                for (int i = 0; i < pixels.Length; i++)
                {
                    var p = pixels[i];
                    Color.RGBToHSV(p, out var hh, out var ss, out var vv);
                    float w8 = OrangeWeight(hh, ss, vv);
                    if (w8 <= 0f) continue;
                    // keep the stripe's shading (its brightness), take the new color's hue / saturation / darkness
                    var c = Color.HSVToRGB(th, ts * Mathf.Clamp01(ss * 1.15f), vv * (0.3f + 0.7f * tv));
                    c.a = p.a;
                    pixels[i] = Color.Lerp(p, c, w8);
                }
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = source.name + "_snmp_" + DiverColors.Hex(color) };
                tex.SetPixels(pixels);
                tex.Apply(true, true); // mipmaps, then free the CPU copy
                Ours.Add(tex.GetInstanceID());
                Cache[(id, color)] = tex;
                return tex;
            }
            catch (Exception e)
            {
                Failed.Add(id);
                Game.WarnOnce("suitpaint", "Couldn't recolor the suit, using its normal colors: " + e.GetBaseException().Message);
                return null;
            }
        }

        // How "suit orange" a pixel is: yellow-orange hues, clearly colored, not too dark.
        static float OrangeWeight(float h, float s, float v)
        {
            const float lo = 15f / 360f, hi = 58f / 360f, soft = 8f / 360f;
            float hue = h < lo - soft || h > hi + soft ? 0f : Mathf.Min(Mathf.Clamp01((h - (lo - soft)) / soft), Mathf.Clamp01(((hi + soft) - h) / soft));
            return hue * Mathf.Clamp01((s - 0.28f) / 0.15f) * Mathf.Clamp01((v - 0.18f) / 0.12f);
        }

        static bool HasOrange(Color[] px)
        {
            int hits = 0, step = Math.Max(1, px.Length / 4000);
            for (int i = 0; i < px.Length; i += step)
            {
                Color.RGBToHSV(px[i], out var h, out var s, out var v);
                if (OrangeWeight(h, s, v) > 0.5f && ++hits > 20) return true;
            }
            return false;
        }

        // Game textures can't be read directly: draw them into a render texture and read that back.
        static Color[] Read(Texture source, out int w, out int h)
        {
            w = source.width; h = source.height;
            while (w > MaxSize || h > MaxSize) { w /= 2; h /= 2; }
            if (w < 8 || h < 8) return null;
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                var tmp = new Texture2D(w, h, TextureFormat.RGBA32, false);
                tmp.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tmp.Apply();
                var px = tmp.GetPixels();
                UnityEngine.Object.Destroy(tmp);
                return px;
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
            }
        }
    }
}
