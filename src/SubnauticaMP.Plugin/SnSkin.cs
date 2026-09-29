using System;
using System.Linq;
using UnityEngine;

namespace SubnauticaMP
{
    // Makes our windows look like Subnautica's own menus: its Aller font, dark see-through blue panels
    // with a thin glowing cyan edge, rounded corners, and cyan highlights on hover.
    internal static class SnSkin
    {
        public static readonly Color Cyan = new Color(0.40f, 0.86f, 0.96f);
        public static readonly Color Text = new Color(0.93f, 0.97f, 1f);
        public static readonly Color Muted = new Color(0.62f, 0.76f, 0.82f);
        public static readonly Color Warn = new Color(1f, 0.72f, 0.35f);

        static GUISkin _skin;
        static Font _font, _bold;

        public static GUIStyle Title, Header, Small, BigButton, Tab, SmallButton, DangerButton, Shadow, BigText, MidText;

        // Call inside OnGUI only.
        public static GUISkin Skin
        {
            get
            {
                if (_skin != null) return _skin;
                try { Build(); }
                catch (Exception e)
                {
                    Game.WarnOnce("skin", "Couldn't build the menu look, using plain Unity style: " + e.Message);
                    _skin = GUI.skin;
                }
                return _skin;
            }
        }

        static void FindFonts()
        {
            var fonts = Resources.FindObjectsOfTypeAll<Font>();
            Font Pick(params string[] names) =>
                names.Select(n => fonts.FirstOrDefault(f => f != null && f.name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0))
                     .FirstOrDefault(f => f != null);
            _font = Pick("Aller_Rg", "Aller Rg", "Aller");
            _bold = Pick("Aller_W_Bd", "Aller_Bd", "Aller Bd") ?? _font;
        }

        static void Build()
        {
            FindFonts();
            var baseSkin = GUI.skin;
            var skin = UnityEngine.Object.Instantiate(baseSkin);
            skin.name = "SubnauticaMP";
            if (_font != null) skin.font = _font;

            var panel = Rounded(new Color(0.02f, 0.09f, 0.14f, 0.93f), new Color(0.35f, 0.80f, 0.92f, 0.85f), 12, 2);
            var inner = Rounded(new Color(0.04f, 0.16f, 0.23f, 0.75f), new Color(0.30f, 0.70f, 0.82f, 0.35f), 8, 1);
            var btn = Rounded(new Color(0.06f, 0.25f, 0.33f, 0.95f), new Color(0.35f, 0.80f, 0.92f, 0.55f), 8, 1);
            var btnHover = Rounded(new Color(0.10f, 0.40f, 0.50f, 0.98f), new Color(0.55f, 0.92f, 1f, 0.95f), 8, 2);
            var btnDown = Rounded(new Color(0.30f, 0.75f, 0.86f, 1f), new Color(0.70f, 0.97f, 1f, 1f), 8, 2);
            var field = Rounded(new Color(0.01f, 0.05f, 0.08f, 0.95f), new Color(0.30f, 0.70f, 0.82f, 0.6f), 6, 1);
            var fieldFocus = Rounded(new Color(0.01f, 0.07f, 0.11f, 1f), new Color(0.55f, 0.92f, 1f, 1f), 6, 2);
            var track = Rounded(new Color(0.02f, 0.08f, 0.12f, 0.8f), new Color(0, 0, 0, 0), 5, 0);
            var thumb = Rounded(new Color(0.30f, 0.70f, 0.82f, 0.8f), new Color(0, 0, 0, 0), 5, 0);

            skin.window = new GUIStyle(baseSkin.window)
            {
                normal = { background = panel, textColor = Cyan },
                onNormal = { background = panel, textColor = Cyan },
                border = new RectOffset(14, 14, 14, 14),
                padding = new RectOffset(18, 18, 16, 16),
                fontSize = 16,
                fontStyle = FontStyle.Bold,
            };
            skin.box = new GUIStyle(baseSkin.box)
            {
                normal = { background = inner, textColor = Text },
                border = new RectOffset(10, 10, 10, 10),
                padding = new RectOffset(10, 10, 8, 8),
                margin = new RectOffset(0, 0, 3, 3),
            };
            skin.label = new GUIStyle(baseSkin.label) { fontSize = 15, wordWrap = true, normal = { textColor = Text } };
            skin.button = Button(baseSkin.button, btn, btnHover, btnDown, 15);
            skin.toggle = new GUIStyle(skin.button);
            skin.textField = new GUIStyle(baseSkin.textField)
            {
                normal = { background = field, textColor = Text },
                hover = { background = field, textColor = Text },
                focused = { background = fieldFocus, textColor = Color.white },
                onNormal = { background = field, textColor = Text },
                onFocused = { background = fieldFocus, textColor = Color.white },
                border = new RectOffset(8, 8, 8, 8),
                padding = new RectOffset(10, 10, 7, 7),
                fontSize = 15,
            };
            skin.textArea = new GUIStyle(skin.textField) { wordWrap = true };
            skin.verticalScrollbar = new GUIStyle(baseSkin.verticalScrollbar) { normal = { background = track }, border = new RectOffset(5, 5, 5, 5), fixedWidth = 10 };
            skin.verticalScrollbarThumb = new GUIStyle(baseSkin.verticalScrollbarThumb) { normal = { background = thumb }, border = new RectOffset(5, 5, 5, 5), fixedWidth = 10 };
            skin.horizontalScrollbar = new GUIStyle(baseSkin.horizontalScrollbar) { normal = { background = track }, border = new RectOffset(5, 5, 5, 5), fixedHeight = 10 };
            skin.horizontalScrollbarThumb = new GUIStyle(baseSkin.horizontalScrollbarThumb) { normal = { background = thumb }, border = new RectOffset(5, 5, 5, 5), fixedHeight = 10 };
            skin.scrollView = new GUIStyle(baseSkin.scrollView);
            skin.settings.cursorColor = Cyan;
            skin.settings.selectionColor = new Color(0.35f, 0.80f, 0.92f, 0.4f);

            Title = new GUIStyle(skin.label) { fontSize = 28, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false, normal = { textColor = Cyan } };
            if (_bold != null) Title.font = _bold;
            Header = new GUIStyle(skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, normal = { textColor = Cyan }, margin = new RectOffset(4, 4, 10, 2) };
            if (_bold != null) Header.font = _bold;
            Small = new GUIStyle(skin.label) { fontSize = 13, normal = { textColor = Muted } };
            BigButton = new GUIStyle(skin.button) { fontSize = 18, fixedHeight = 46 };
            if (_bold != null) BigButton.font = _bold;
            SmallButton = new GUIStyle(skin.button) { fontSize = 13, padding = new RectOffset(8, 8, 4, 4) };
            DangerButton = Button(skin.button,
                Rounded(new Color(0.30f, 0.08f, 0.06f, 0.95f), new Color(1f, 0.45f, 0.35f, 0.6f), 8, 1),
                Rounded(new Color(0.50f, 0.12f, 0.08f, 0.98f), new Color(1f, 0.60f, 0.45f, 1f), 8, 2),
                Rounded(new Color(0.80f, 0.25f, 0.15f, 1f), new Color(1f, 0.75f, 0.60f, 1f), 8, 2), 13);
            DangerButton.padding = new RectOffset(8, 8, 4, 4);
            Tab = Button(skin.button, btn, btnHover, btnDown, 15);
            Tab.onNormal.background = btnDown;
            Tab.onNormal.textColor = new Color(0.02f, 0.10f, 0.15f);
            Tab.onHover.background = btnDown;
            Tab.onHover.textColor = new Color(0.02f, 0.10f, 0.15f);
            Shadow = new GUIStyle(skin.label) { wordWrap = false, normal = { textColor = new Color(0, 0, 0, 0.8f) } };
            BigText = new GUIStyle(Title) { fontSize = 44 };
            MidText = new GUIStyle(skin.label) { fontSize = 20, alignment = TextAnchor.MiddleCenter };

            _skin = skin;
        }

        static GUIStyle Button(GUIStyle from, Texture2D normal, Texture2D hover, Texture2D down, int size)
        {
            var s = new GUIStyle(from)
            {
                normal = { background = normal, textColor = Text },
                hover = { background = hover, textColor = Color.white },
                active = { background = down, textColor = new Color(0.02f, 0.10f, 0.15f) },
                focused = { background = normal, textColor = Text },
                onNormal = { background = down, textColor = new Color(0.02f, 0.10f, 0.15f) },
                onHover = { background = down, textColor = new Color(0.02f, 0.10f, 0.15f) },
                onActive = { background = down, textColor = new Color(0.02f, 0.10f, 0.15f) },
                border = new RectOffset(10, 10, 10, 10),
                padding = new RectOffset(14, 14, 8, 8),
                margin = new RectOffset(4, 4, 4, 4),
                fontSize = size,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
            if (_bold != null) s.font = _bold;
            return s;
        }

        // A small rounded-rectangle texture for 9-slice drawing.
        static Texture2D Rounded(Color fill, Color edge, int radius, int edgeWidth)
        {
            int size = radius * 2 + 4;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // distance outside the rounded rect's inner corner box
                    float cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
                    float cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                    float outside = d - radius;                      // > 0 = outside the shape
                    float alpha = Mathf.Clamp01(0.5f - outside);      // soft edge
                    var c = outside > -edgeWidth ? edge : fill;
                    if (edgeWidth == 0) c = fill;
                    c.a *= alpha;
                    px[y * size + x] = c;
                }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        // Text with a soft dark outline, readable over the game.
        public static void OutlinedLabel(Rect r, string text, GUIStyle style)
        {
            var shadow = new GUIStyle(style) { normal = { textColor = new Color(0, 0, 0, 0.85f) } };
            GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), text, shadow);
            GUI.Label(new Rect(r.x - 1, r.y + 1, r.width, r.height), text, shadow);
            GUI.Label(r, text, style);
        }
    }
}
