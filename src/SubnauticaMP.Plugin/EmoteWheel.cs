using System;
using System.Collections.Generic;
using System.Linq;
using SubnauticaMP.Shared;
using UnityEngine;

namespace SubnauticaMP
{
    // Fortnite-style emote wheel.
    //  * Hold G: the wheel opens. Point the mouse at a slot and let go of G to play it.
    //    (Tap G instead and it stays open: click a slot.)
    //  * Hold the mouse on a slot for a moment: a bar with every emote opens (with search) to put a new one there.
    //  * The middle shows a diver doing the emote you're pointing at, facing you. Click the middle for the dance party.
    internal sealed class EmoteWheel
    {
        const int Slots = 8;
        const float HoldToEdit = 0.55f, TapSeconds = 0.25f;
        const float Outer = 330f, Inner = 150f; // at 1080p

        readonly Session _s;
        bool _open, _heldOpen;
        float _openedAt, _pressAt = -1f;
        int _hover = -1, _pressSlot = -1, _editSlot = -1;
        string _search = "", _category = "All";
        Vector2 _scroll;
        Emote _pickerHover = Emote.None;
        string[] _slots;
        Texture2D _wedge, _wedgeHot, _disc, _dim;
        EmotePreview _preview;
        GUIStyle _label, _small, _big, _slotNum;

        static readonly string[] Categories = { "All", "Dances", "Fun", "Gestures", "Poses" };

        public EmoteWheel(Session s) { _s = s; }

        public bool IsOpen => _open;

        // ---------- the 8 slots (saved in the config) ----------

        string[] SlotNames
        {
            get
            {
                if (_slots != null) return _slots;
                var saved = (Plugin.EmoteWheel.Value ?? "").Split(',').Select(x => x.Trim()).ToList();
                _slots = new string[Slots];
                for (int i = 0; i < Slots; i++)
                    _slots[i] = i < saved.Count && Emotes.Named(saved[i]) != null ? saved[i] : Emotes.DefaultWheel[i];
                return _slots;
            }
        }

        void SetSlot(int slot, Emotes.Info info)
        {
            SlotNames[slot] = info.Name;
            Plugin.EmoteWheel.Value = string.Join(",", SlotNames);
        }

        Emotes.Info SlotInfo(int i) => Emotes.Named(SlotNames[i]);

        // ---------- input (Update) ----------

        public void Update()
        {
            var key = Plugin.EmoteKey.Value;
            if (!_open)
            {
                if (Input.GetKeyDown(key) && CanOpen()) Open();
                return;
            }

            if (!_s.Joined || !Game.InWorld) { Close(); return; }
            if (Game.LockCursor) Game.SetLockCursor(false); // something grabbed the mouse again
            // the game ignores its own controls (looking around, swimming, tools) while this is up. Only its flag:
            // GameInput.ClearInput() also wipes Unity's key presses, and then the wheel never saw a click or Esc.
            Game.TryDo("block game input", () => Game.Set(Game.GameInput, null, "clearInputFrame", Time.frameCount + 1));

            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
            {
                if (_editSlot >= 0) _editSlot = -1; else Close();
                return;
            }
            if (_editSlot >= 0)
            {
                if (Input.GetKeyDown(key) && !Typing) Close();
                return; // the picker is clicked through OnGUI
            }

            _hover = SlotUnderMouse();

            if (Input.GetKeyUp(key))
            {
                if (Time.unscaledTime - _openedAt < TapSeconds && !_heldOpen) { _heldOpen = true; return; } // tapped: stay open
                if (!_heldOpen) { Pick(_hover); return; }
            }
            if (_heldOpen && Input.GetKeyDown(key)) { Close(); return; }

            // click = play, hold = change what's in that slot
            if (Input.GetMouseButtonDown(0)) { _pressAt = Time.unscaledTime; _pressSlot = _hover; }
            if (_pressAt >= 0f && _pressSlot >= 0 && Time.unscaledTime - _pressAt >= HoldToEdit)
            {
                OpenPicker(_pressSlot);
                _pressAt = -1f;
            }
            if (Input.GetMouseButtonUp(0) && _pressAt >= 0f)
            {
                _pressAt = -1f;
                if (_pressSlot == _hover) Pick(_hover);
            }

            // number keys 1-8 play a slot straight away
            for (int i = 0; i < Slots; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i)) { Pick(i); return; }
        }

        bool CanOpen() =>
            _s.Joined && Game.InWorld && !UiKit.Typing() && Game.LockCursor && !PdaOrMenuOpen();

        static bool PdaOrMenuOpen() => Cursor.lockState != CursorLockMode.Locked && !Game.LockCursor;

        bool Typing => GUIUtility.keyboardControl != 0;

        bool _prewarmed;

        // Loads the dance animations and builds the preview diver (needs the diver model), so opening the wheel is instant.
        public void Prewarm()
        {
            if (_prewarmed || !DiverModel.Prepare()) return;
            _prewarmed = true;
            int clips = EmoteClips.Count;
            try { _preview = _preview ?? new EmotePreview(); _preview.Show(true); _preview.Show(false); }
            catch (Exception e) { Game.WarnOnce("preview", "No emote preview: " + e.GetBaseException().Message); _preview = null; }
        }

        void Open()
        {
            _open = true;
            _heldOpen = false;
            _openedAt = Time.unscaledTime;
            _editSlot = -1;
            _hover = -1;
            _pressAt = -1f;
            Game.SetLockCursor(false); // free the mouse the way the game's own menus do
            try { _preview = _preview ?? new EmotePreview(); _preview.Show(true); }
            catch (Exception e) { Game.WarnOnce("preview", "No emote preview: " + e.GetBaseException().Message); _preview = null; }
        }

        public void Close()
        {
            if (!_open) return;
            _open = false;
            _editSlot = -1;
            GUIUtility.keyboardControl = 0;
            _preview?.Show(false);
            Game.SetLockCursor(true);
        }

        // -1 = none, 8 = the middle (party)
        int SlotUnderMouse()
        {
            float scale = Screen.height / 1080f;
            var m = new Vector2(Input.mousePosition.x - Screen.width / 2f, Input.mousePosition.y - Screen.height / 2f);
            float d = m.magnitude / scale;
            if (d < Inner * 0.8f) return Slots;
            if (d < Inner * 0.8f + 10f) return -1;
            float angle = Mathf.Atan2(m.x, m.y) * Mathf.Rad2Deg; // 0 = up, clockwise
            if (angle < 0) angle += 360f;
            return Mathf.RoundToInt(angle / (360f / Slots)) % Slots;
        }

        void Pick(int slot)
        {
            if (slot == Slots)
            {
                if (_s.Emoting.LeadingParty) _s.Emoting.EndParty();
                else _s.Emoting.Play(Emote.Party);
                Close();
                return;
            }
            if (slot < 0 || slot >= Slots) { if (!_heldOpen) Close(); return; }
            var info = SlotInfo(slot);
            Close();
            if (info != null) _s.Emoting.Play(info.Emote);
        }

        void OpenPicker(int slot)
        {
            if (slot < 0 || slot >= Slots) return;
            _editSlot = slot;
            _heldOpen = true;
            _search = "";
            _scroll = Vector2.zero;
        }

        // ---------- the diver in the middle (LateUpdate) ----------

        public void LateUpdate()
        {
            if (!_open || _preview == null) return;
            Emote show = Emote.None;
            if (_editSlot >= 0) show = _pickerHover != Emote.None ? _pickerHover : SlotInfo(_editSlot)?.Emote ?? Emote.None;
            else if (_hover == Slots) show = Emotes.Named("floss").Emote;
            else if (_hover >= 0) show = SlotInfo(_hover)?.Emote ?? Emote.None;
            try { _preview.Play(show, Time.unscaledDeltaTime); }
            catch (Exception e) { Game.WarnOnce("preview", "No emote preview: " + e.GetBaseException().Message); _preview.Show(false); _preview = null; }
        }

        // ---------- drawing (OnGUI) ----------

        public void OnGUI()
        {
            if (!_open || Event.current == null) return;
            MakeTextures();
            float scale = Screen.height / 1080f;
            var c = new Vector2(Screen.width / 2f, Screen.height / 2f);
            float outer = Outer * scale, inner = Inner * scale;

            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _dim);

            // wedges, rotated into place
            var wheelRect = new Rect(c.x - outer, c.y - outer, outer * 2, outer * 2);
            for (int i = 0; i < Slots; i++)
            {
                var m = GUI.matrix;
                GUIUtility.RotateAroundPivot(i * 360f / Slots, c);
                GUI.DrawTexture(wheelRect, i == _hover || i == _editSlot ? _wedgeHot : _wedge);
                GUI.matrix = m;
            }

            // labels on the wedges
            for (int i = 0; i < Slots; i++)
            {
                float a = i * Mathf.PI * 2f / Slots;
                float r = (inner + outer) * 0.5f;
                var p = new Vector2(c.x + Mathf.Sin(a) * r, c.y - Mathf.Cos(a) * r);
                var info = SlotInfo(i);
                GUI.Label(new Rect(p.x - 90 * scale, p.y - 26 * scale, 180 * scale, 30 * scale), info?.Label ?? "-", _label);
                GUI.Label(new Rect(p.x - 90 * scale, p.y + 2 * scale, 180 * scale, 22 * scale), $"{i + 1}  ·  {info?.Category}", _small);
            }

            // the middle: the diver doing it, facing you
            var discRect = new Rect(c.x - inner * 0.8f, c.y - inner * 0.8f, inner * 1.6f, inner * 1.6f);
            GUI.DrawTexture(discRect, _disc);
            if (_preview?.Texture != null)
                GUI.DrawTexture(new Rect(c.x - inner * 0.78f, c.y - inner * 0.9f, inner * 1.56f, inner * 1.56f), _preview.Texture, ScaleMode.ScaleToFit, true);

            string title = _hover == Slots ? PartyTitle()
                         : _hover >= 0 ? SlotInfo(_hover)?.Label ?? ""
                         : "Emotes";
            GUI.Label(new Rect(c.x - inner, c.y + inner * 0.52f, inner * 2, 32 * scale), title, _big);
            GUI.Label(new Rect(c.x - inner, c.y + inner * 0.52f + 30 * scale, inner * 2, 22 * scale),
                _hover == Slots ? "click" : _s.Emoting.PartyActive ? "party going: click the middle" : "middle = dance party", _small);

            GUI.Label(new Rect(0, c.y + outer + 18 * scale, Screen.width, 26 * scale),
                "Point + let go of " + Plugin.EmoteKey.Value + " to play   ·   click a slot   ·   HOLD click on a slot to change it   ·   Esc closes", _small);

            if (_editSlot >= 0) DrawPicker(scale);
        }

        string PartyTitle() =>
            _s.Emoting.LeadingParty ? "End your party" :
            _s.Emoting.PartyActive ? "Join the party! (" + _s.Emoting.PartyDanceName() + ")" : "Start a dance party";

        // The bar with every emote: search, categories, click one to put it in the slot.
        void DrawPicker(float scale)
        {
            var old = GUI.skin;
            GUI.skin = SnSkin.Skin;
            float w = 420 * scale, h = Screen.height - 120 * scale;
            var rect = new Rect(Screen.width - w - 40 * scale, 60 * scale, w, h);
            GUILayout.BeginArea(rect, GUI.skin.window);
            GUILayout.Label($"Slot {_editSlot + 1}: pick an emote", SnSkin.Header ?? GUI.skin.label);
            GUI.SetNextControlName("snmp_emote_search");
            _search = GUILayout.TextField(_search ?? "", 30);
            if (string.IsNullOrEmpty(GUI.GetNameOfFocusedControl())) GUI.FocusControl("snmp_emote_search");
            GUILayout.BeginHorizontal();
            foreach (var cat in Categories)
                if (GUILayout.Toggle(_category == cat, cat, GUI.skin.button)) _category = cat;
            GUILayout.EndHorizontal();

            _scroll = GUILayout.BeginScrollView(_scroll);
            _pickerHover = Emote.None;
            var q = (_search ?? "").Trim().ToLowerInvariant();
            foreach (var info in Emotes.All.Where(Emotes.Pickable))
            {
                if (_category != "All" && info.Category != _category) continue;
                if (q.Length > 0 && !info.Label.ToLowerInvariant().Contains(q) && !info.Name.Contains(q) && !info.Aliases.Any(a => a.Contains(q))) continue;
                bool inSlot = SlotNames[_editSlot] == info.Name;
                if (GUILayout.Button((inSlot ? "» " : "") + info.Label + "   ·   " + info.Category + (info.Seconds <= 0 ? "  (loops)" : "")))
                {
                    SetSlot(_editSlot, info);
                    _editSlot = -1;
                    GUIUtility.keyboardControl = 0;
                }
                if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
                    _pickerHover = info.Emote;
            }
            GUILayout.EndScrollView();
            if (GUILayout.Button("Back to the wheel")) { _editSlot = -1; GUIUtility.keyboardControl = 0; }
            GUILayout.EndArea();
            GUI.skin = old;
        }

        // ---------- look ----------

        void MakeTextures()
        {
            if (_wedge != null) return;
            _wedge = Wedge(new Color(0.02f, 0.10f, 0.15f, 0.82f), new Color(0.35f, 0.80f, 0.92f, 0.55f));
            _wedgeHot = Wedge(new Color(0.08f, 0.38f, 0.48f, 0.92f), new Color(0.60f, 0.95f, 1f, 1f));
            _disc = Disc(new Color(0.01f, 0.06f, 0.10f, 0.9f), new Color(0.35f, 0.80f, 0.92f, 0.8f));
            _dim = new Texture2D(1, 1); _dim.SetPixel(0, 0, new Color(0, 0.02f, 0.04f, 0.35f)); _dim.Apply();

            _label = new GUIStyle(SnSkin.Skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(21 * Screen.height / 1080f), fontStyle = FontStyle.Bold, wordWrap = false };
            _label.normal.textColor = SnSkin.Text;
            _small = new GUIStyle(_label) { fontSize = Mathf.RoundToInt(14 * Screen.height / 1080f), fontStyle = FontStyle.Normal };
            _small.normal.textColor = SnSkin.Muted;
            _big = new GUIStyle(_label) { fontSize = Mathf.RoundToInt(24 * Screen.height / 1080f) };
            _big.normal.textColor = SnSkin.Cyan;
        }

        // One slot of the ring, pointing up, on a square texture the size of the whole wheel.
        static Texture2D Wedge(Color fill, Color edge)
        {
            const int size = 512;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float half = size / 2f, rOut = half - 2, rIn = half * Inner / Outer, halfAngle = 180f / Slots - 1.2f;
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - half, dy = y + 0.5f - half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float ang = Mathf.Abs(Mathf.Atan2(dx, dy) * Mathf.Rad2Deg); // 0 = up (texture y goes up)
                    float inside = Mathf.Min(Mathf.Clamp01(rOut - r), Mathf.Clamp01(r - rIn), Mathf.Clamp01((halfAngle - ang) * r * Mathf.Deg2Rad));
                    if (inside <= 0f) { px[y * size + x] = Color.clear; continue; }
                    float edgeDist = Mathf.Min(rOut - r, r - rIn, (halfAngle - ang) * r * Mathf.Deg2Rad);
                    var col = edgeDist < 2.5f ? edge : Color.Lerp(fill, fill * 1.25f, (r - rIn) / (rOut - rIn));
                    col.a *= inside;
                    px[y * size + x] = col;
                }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        static Texture2D Disc(Color fill, Color edge)
        {
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float half = size / 2f;
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float r = Mathf.Sqrt((x + 0.5f - half) * (x + 0.5f - half) + (y + 0.5f - half) * (y + 0.5f - half));
                    float a = Mathf.Clamp01(half - 1 - r);
                    var col = half - 1 - r < 3f ? edge : fill;
                    col.a *= a;
                    px[y * size + x] = col;
                }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }
    }

    // A copy of your diver somewhere nobody can see, with its own little camera: what the wheel shows in the middle.
    internal sealed class EmotePreview
    {
        const int Layer = 31;
        static readonly Vector3 Spot = new Vector3(0f, 2500f, 0f); // high above the world

        GameObject _root, _diver;
        Camera _cam;
        RenderTexture _rt;
        EmoteAnimator _anim;
        Emote _playing = Emote.None;
        float _restartAt;

        public Texture Texture => _rt;

        public void Show(bool on)
        {
            if (on && _root == null) Build();
            if (_root != null) _root.SetActive(on);
            if (!on) { _anim?.ResetNow(); _playing = Emote.None; }
        }

        void Build()
        {
            _root = new GameObject("SubnauticaMP_EmotePreview");
            Game.KeepAlive(_root);
            _root.transform.position = Spot;

            _rt = new RenderTexture(512, 512, 16, RenderTextureFormat.ARGB32) { name = "SNMP_EmotePreview" };
            var camGo = new GameObject("Camera");
            camGo.transform.SetParent(_root.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 1.0f, 4.2f);
            camGo.transform.localRotation = Quaternion.LookRotation(new Vector3(0f, -0.08f, -1f));
            _cam = camGo.AddComponent<Camera>();
            _cam.cullingMask = 1 << Layer;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0, 0, 0, 0);
            _cam.fieldOfView = 34f;
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 20f;
            _cam.targetTexture = _rt;

            foreach (var (pos, intensity) in new[] { (new Vector3(1.5f, 2.5f, 3f), 1.6f), (new Vector3(-2f, 1.5f, 2f), 0.9f), (new Vector3(0f, 2f, -2f), 0.8f) })
            {
                var lg = new GameObject("Light");
                lg.transform.SetParent(_root.transform, false);
                lg.transform.localPosition = pos;
                var light = lg.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = 8f;
                light.intensity = intensity;
                light.color = new Color(0.85f, 0.95f, 1f);
                light.cullingMask = 1 << Layer;
            }

            _diver = DiverModel.Create(_root.transform);
            if (_diver != null)
            {
                _diver.transform.localRotation = Quaternion.identity; // facing the camera (+z)
                foreach (var t in _diver.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
                foreach (var r in _diver.GetComponentsInChildren<SkinnedMeshRenderer>(true)) r.updateWhenOffscreen = true;
                Game.TryDo("preview color", () => SuitPaint.Apply(_diver, Plugin.DiverColor.Value));
                var animator = _diver.GetComponentInChildren<Animator>(true);
                if (animator != null) { animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; animator.updateMode = AnimatorUpdateMode.UnscaledTime; }
                _anim = new EmoteAnimator(_diver);
            }
            else Plugin.Log.LogInfo("Emote preview: no diver model yet");
        }

        public void Play(Emote emote, float dt)
        {
            if (_anim == null) return;
            if (emote != _playing || (emote != Emote.None && Time.unscaledTime >= _restartAt))
            {
                _playing = emote;
                if (emote == Emote.None) _anim.Stop();
                else
                {
                    _anim.ResetNow();
                    _anim.Play(emote);
                    var info = Emotes.Get(emote);
                    _restartAt = Time.unscaledTime + (info != null && info.Seconds > 0 ? info.Seconds + 0.6f : float.MaxValue);
                }
            }
            _anim.Apply(dt);
        }
    }
}
