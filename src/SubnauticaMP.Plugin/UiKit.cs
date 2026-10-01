using System;
using System.Collections;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Events;

namespace SubnauticaMP
{
    // Helpers for re-using Subnautica's own menu pieces (copying a real panel/button/text box and changing
    // what it says and does). The game's UI classes (Button, TextMeshPro, input fields) are reached by name.
    internal static class UiKit
    {
        public static readonly Type Button = Type.GetType("UnityEngine.UI.Button, UnityEngine.UI");
        public static readonly Type Selectable = Type.GetType("UnityEngine.UI.Selectable, UnityEngine.UI");
        public static readonly Type Image = Type.GetType("UnityEngine.UI.Image, UnityEngine.UI");
        public static readonly Type RawImage = Type.GetType("UnityEngine.UI.RawImage, UnityEngine.UI");
        public static readonly Type CanvasScaler = Type.GetType("UnityEngine.UI.CanvasScaler, UnityEngine.UI");
        public static readonly Type GraphicRaycaster = Type.GetType("UnityEngine.UI.GraphicRaycaster, UnityEngine.UI");
        public static readonly Type LayoutElement = Type.GetType("UnityEngine.UI.LayoutElement, UnityEngine.UI");
        public static readonly Type TmpText = Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro");
        public static readonly Type TmpInput = Type.GetType("TMPro.TMP_InputField, Unity.TextMeshPro");

        static GameObject _holder;

        // An inactive parent: copies made inside it don't wake up until we move them out.
        public static Transform Holder
        {
            get
            {
                if (_holder == null)
                {
                    _holder = new GameObject("SubnauticaMP_UiTemplates");
                    _holder.SetActive(false);
                    Game.KeepAlive(_holder);
                }
                return _holder.transform;
            }
        }

        // A sleeping copy of a piece of the game's UI.
        public static GameObject Copy(GameObject source, string name)
        {
            var copy = UnityEngine.Object.Instantiate(source, Holder, false);
            copy.name = name;
            return copy;
        }

        public static GameObject Place(GameObject copy, Transform parent, bool active = true)
        {
            copy.transform.SetParent(parent, false);
            copy.SetActive(active);
            return copy;
        }

        public static void RemoveComponents(GameObject go, params string[] typeNames)
        {
            foreach (var c in go.GetComponentsInChildren<Component>(true))
                if (c != null && typeNames.Contains(c.GetType().Name)) UnityEngine.Object.DestroyImmediate(c);
        }

        // ---------- text ----------

        public static Component FirstText(GameObject go) => TmpText == null || go == null ? null : go.GetComponentInChildren(TmpText, true);

        // Sets the (first) text in this object. The game re-translates labels on its own, so that's switched off.
        public static void SetText(GameObject go, string text) => SetText(FirstText(go), text);

        public static void SetText(Component tmp, string text)
        {
            if (tmp == null) return;
            foreach (var c in tmp.GetComponents<Component>())
                if (c != null && c.GetType().Name == "TranslationLiveUpdate") UnityEngine.Object.Destroy(c);
            Game.Set(tmp.GetType(), tmp, "text", text);
        }

        public static void StopTranslating(GameObject go) => RemoveComponents(go, "TranslationLiveUpdate");

        // ---------- buttons ----------

        public static Component FindButton(GameObject go) => Button == null || go == null ? null : go.GetComponent(Button) ?? go.GetComponentInChildren(Button, true);

        // Replaces whatever the button did with our action.
        public static void OnClick(GameObject go, Action action) => OnClick(FindButton(go), action);

        public static void OnClick(Component button, Action action)
        {
            if (button == null) return;
            var prop = button.GetType().GetProperty("onClick");
            if (prop == null) return;
            var evt = (UnityEventBase)Activator.CreateInstance(prop.PropertyType);
            ((UnityEvent)evt).AddListener(() =>
            {
                try { action(); }
                catch (Exception e) { Plugin.Log.LogError("Menu button failed: " + e); }
            });
            prop.SetValue(button, evt, null);
        }

        // What the button was wired to in the game (e.g. "OnButtonSurvival").
        public static string ClickTarget(Component button)
        {
            var evt = button?.GetType().GetProperty("onClick")?.GetValue(button, null) as UnityEventBase;
            return evt != null && evt.GetPersistentEventCount() > 0 ? evt.GetPersistentMethodName(0) : "";
        }

        public static void SetInteractable(GameObject go, bool on)
        {
            var sel = Selectable != null ? go.GetComponentInChildren(Selectable, true) : null;
            if (sel != null) Game.Set(Selectable, sel, "interactable", on);
        }

        // ---------- text boxes ----------

        public static Component FindInput(GameObject go) => TmpInput == null || go == null ? null : go.GetComponentInChildren(TmpInput, true);

        public static string GetInput(Component field) => field == null ? "" : Game.Get(TmpInput, field, "text") as string ?? "";

        public static void SetInput(Component field, string text)
        {
            if (field != null) Game.Set(TmpInput, field, "text", text ?? "");
        }

        public static void SetupInput(Component field, string placeholder, bool password, int maxLength)
        {
            if (field == null) return;
            var placeholderGraphic = Game.Get(TmpInput, field, "placeholder") as Component;
            if (placeholderGraphic != null) SetText(placeholderGraphic, placeholder);
            Game.Set(TmpInput, field, "characterLimit", maxLength);
            var contentType = TmpInput.GetNestedType("ContentType");
            if (contentType != null)
                Game.Set(TmpInput, field, "contentType", Enum.Parse(contentType, password ? "Password" : "Standard"));
            if (field.GetType().GetField("uppercase") != null) Game.Set(field.GetType(), field, "uppercase", false);
            // the email box on the main menu sends mail when you press enter: don't
            var submit = Game.Get(TmpInput, field, "onSubmit") as UnityEventBase;
            if (submit != null) Game.Set(TmpInput, field, "onSubmit", Activator.CreateInstance(submit.GetType()));
            var end = Game.Get(TmpInput, field, "onEndEdit") as UnityEventBase;
            if (end != null) Game.Set(TmpInput, field, "onEndEdit", Activator.CreateInstance(end.GetType()));
        }

        public static void Focus(Component field)
        {
            if (field == null) return;
            Game.Call(TmpInput, field, "ActivateInputField");
        }

        static readonly Type EventSystem = Type.GetType("UnityEngine.EventSystems.EventSystem, UnityEngine.UI");

        // True while the player is typing into some text box (a sign, the Cyclops name, our chat...).
        public static bool Typing()
        {
            var current = EventSystem != null ? EventSystem.GetProperty("current")?.GetValue(null, null) : null;
            var selected = current != null ? EventSystem.GetProperty("currentSelectedGameObject")?.GetValue(current, null) as GameObject : null;
            return selected != null && FindInput(selected) != null;
        }

        // Keeps only Unity's own UI parts on a copy (drops game scripts that expect their original menu).
        public static void KeepOnlyUi(GameObject go)
        {
            for (int pass = 0; pass < 4; pass++)
            {
                bool removed = false;
                foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (mb == null) continue;
                    var ns = mb.GetType().Namespace ?? "";
                    if (ns.StartsWith("UnityEngine") || ns.StartsWith("TMPro")) continue;
                    try { UnityEngine.Object.DestroyImmediate(mb); removed = true; } catch { }
                }
                if (!removed) break;
            }
        }

        // Inside the game's layouts a copied text box has no height of its own: give it one.
        public static void GiveHeight(GameObject go, float height)
        {
            if (LayoutElement == null) return;
            var le = go.GetComponent(LayoutElement) ?? go.AddComponent(LayoutElement);
            Game.Set(LayoutElement, le, "minHeight", height);
            Game.Set(LayoutElement, le, "preferredHeight", height);
            Game.Set(LayoutElement, le, "flexibleWidth", 1f);
        }

        // A text box taken from somewhere else (like the dev console) may carry that place's scripts: drop them.
        public static void KeepInputOnly(GameObject go)
        {
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                var ns = mb.GetType().Namespace ?? "";
                if (ns.StartsWith("UnityEngine") || ns.StartsWith("TMPro") || (TmpInput != null && TmpInput.IsInstanceOfType(mb))) continue;
                try { UnityEngine.Object.DestroyImmediate(mb); } catch { }
            }
        }

        public static string Describe(GameObject go) =>
            go == null ? "null" : go.name + " [" + string.Join(", ", go.GetComponentsInChildren<Component>(true).Where(c => c != null).Select(c => c.GetType().Name).Distinct().ToArray()) + "]";

        // Any text box in the loaded game (for the pause menu, when the main menu's copy isn't usable).
        public static GameObject SceneInput()
        {
            if (TmpInput == null) return null;
            foreach (var o in Resources.FindObjectsOfTypeAll(TmpInput))
                if (o is Component c && c != null && c.gameObject.scene.IsValid()) return c.gameObject;
            return null;
        }

        // ---------- canvases ----------

        // A screen overlay canvas that sorts above the game's HUD.
        public static Canvas MakeCanvas(string name, int order)
        {
            var go = new GameObject(name);
            Game.KeepAlive(go);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = order;
            if (CanvasScaler != null)
            {
                var scaler = go.AddComponent(CanvasScaler);
                Game.Set(CanvasScaler, scaler, "uiScaleMode", Enum.Parse(CanvasScaler.GetNestedType("ScaleMode"), "ScaleWithScreenSize"));
                Game.Set(CanvasScaler, scaler, "referenceResolution", new Vector2(1920, 1080));
                Game.Set(CanvasScaler, scaler, "matchWidthOrHeight", 0.5f);
            }
            if (GraphicRaycaster != null) go.AddComponent(GraphicRaycaster);
            return canvas;
        }

        public static RectTransform Rect(GameObject go)
        {
            var rt = go.GetComponent<RectTransform>();
            return rt != null ? rt : go.AddComponent<RectTransform>();
        }

        // Anchors a UI piece at a point on its parent (0..1), centered on that point.
        public static void Pin(GameObject go, Vector2 anchor, Vector2 offset, Vector2? size = null)
        {
            var rt = Rect(go);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = offset;
            if (size.HasValue) rt.sizeDelta = size.Value;
        }

        public static void Stretch(GameObject go)
        {
            var rt = Rect(go);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        static readonly Type TmpUgui = Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro");
        static readonly Type TmpAlign = Type.GetType("TMPro.TextAlignmentOptions, Unity.TextMeshPro");
        static readonly Type TmpStyles = Type.GetType("TMPro.FontStyles, Unity.TextMeshPro");
        static readonly Type RectMask = Type.GetType("UnityEngine.UI.RectMask2D, UnityEngine.UI");

        // True if the copy really is a working text box (copies of the game's UI can lose their scripts once
        // the scene they came from is gone: then they look right but you can't type in them).
        public static bool Works(GameObject go) => Intact(go) && FindInput(go) != null;

        public static bool Intact(GameObject go) => go != null && go.GetComponentsInChildren<Component>(true).All(c => c != null);

        // A text box built from scratch with Unity's own parts (sleeping, in Holder): never depends on a game
        // menu that may have been unloaded.
        public static GameObject MakeInput(string name)
        {
            if (TmpInput == null || TmpUgui == null) return null;
            Component font = null;
            foreach (var o in Resources.FindObjectsOfTypeAll(TmpText))
                if (o is Component c && c != null && c.gameObject.scene.IsValid() && Game.TryGet(TmpText, c, "font") != null) { font = c; break; }

            var root = SolidImage(Holder, name, new Color(0f, 0f, 0f, 0.45f));
            var area = new GameObject("Text Area");
            area.transform.SetParent(root.transform, false);
            Stretch(area);
            Rect(area).offsetMin = new Vector2(12f, 6f);
            Rect(area).offsetMax = new Vector2(-12f, -6f);
            if (RectMask != null) area.AddComponent(RectMask);

            Component MakeText(string n, Color color, bool italic)
            {
                var go = new GameObject(n);
                go.transform.SetParent(area.transform, false);
                Stretch(go);
                var t = go.AddComponent(TmpUgui);
                if (font != null) Game.Set(TmpText, t, "font", Game.TryGet(TmpText, font, "font"));
                Game.Set(TmpText, t, "fontSize", 22f);
                Game.Set(TmpText, t, "color", color);
                Game.Set(TmpText, t, "enableWordWrapping", false);
                Game.Set(TmpText, t, "richText", false);
                if (TmpAlign != null) Game.Set(TmpText, t, "alignment", Enum.Parse(TmpAlign, "MidlineLeft"));
                if (italic && TmpStyles != null) Game.Set(TmpText, t, "fontStyle", Enum.Parse(TmpStyles, "Italic"));
                return t;
            }
            var placeholder = MakeText("Placeholder", new Color(1f, 1f, 1f, 0.45f), true);
            var text = MakeText("Text", Color.white, false);

            var input = root.AddComponent(TmpInput);
            Game.Set(TmpInput, input, "textViewport", Rect(area));
            Game.Set(TmpInput, input, "textComponent", text);
            Game.Set(TmpInput, input, "placeholder", placeholder);
            if (Image != null) Game.Set(Selectable, input, "targetGraphic", root.GetComponent(Image));
            Game.Set(TmpInput, input, "caretColor", Color.white);
            Game.Set(TmpInput, input, "customCaretColor", true);
            Game.Set(TmpInput, input, "selectionColor", new Color(0.3f, 0.7f, 1f, 0.5f));
            return root;
        }

        // A working text box: a copy of `source` if that copy works, else one built from scratch.
        public static GameObject InputFrom(GameObject source, string name)
        {
            if (source != null)
            {
                var copy = Copy(source, name);
                if (Works(copy)) return copy;
                UnityEngine.Object.DestroyImmediate(copy);
            }
            return MakeInput(name);
        }

        public static GameObject SolidImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            Rect(go);
            if (Image != null)
            {
                var img = go.AddComponent(Image);
                Game.Set(Image, img, "color", color);
            }
            return go;
        }

        // ---------- finding things ----------

        public static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                var hit = FindDeep(child, name);
                if (hit != null) return hit;
            }
            return null;
        }

        // Writes a UI tree to the log (names + components), so layout problems can be fixed from a log file.
        public static void Dump(Transform root, string title, int maxDepth = 6)
        {
            if (root == null) return;
            var sb = new StringBuilder();
            sb.AppendLine("UI tree: " + title);
            DumpInto(sb, root, 0, maxDepth);
            Plugin.Log.LogInfo(sb.ToString());
        }

        static void DumpInto(StringBuilder sb, Transform t, int depth, int maxDepth)
        {
            sb.Append(' ', depth * 2).Append(t.name).Append(t.gameObject.activeSelf ? "" : " (off)").Append("  [");
            sb.Append(string.Join(", ", t.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType().Name).ToArray()));
            sb.AppendLine("]");
            if (depth >= maxDepth) return;
            foreach (Transform child in t) DumpInto(sb, child, depth + 1, maxDepth);
        }
    }
}
