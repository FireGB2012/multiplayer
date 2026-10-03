using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace SubnauticaMP
{
    // Finding lag for real instead of guessing.
    //  * F9 (PerfKey): overlay with FPS, frame times, how long each part of the mod takes per frame, garbage collections.
    //  * "/perf" in chat: records 10 seconds (play normally: turn the camera, swim around) and writes a report to
    //    the log + chat, including what was going on during every stutter (mod part, garbage collection, or neither
    //    = the game itself, e.g. loading terrain).
    internal static class PerfMonitor
    {
        const int Frames = 600;
        const float SpikeMs = 50f;

        static readonly float[] _frameMs = new float[Frames];
        static int _frameIdx, _frameCount;
        static readonly Dictionary<string, double> _thisFrame = new Dictionary<string, double>();
        static readonly Dictionary<string, double> _window = new Dictionary<string, double>(); // last second, per module
        static readonly Dictionary<string, double> _shown = new Dictionary<string, double>();
        static int _windowFrames, _shownFrames, _lastGcCount;
        static float _windowStart;
        static double _modThisFrame;

        public static bool Overlay;

        // report
        static bool _recording;
        static float _recordUntil;
        static readonly List<float> _recFrames = new List<float>();
        static readonly Dictionary<string, double> _recModules = new Dictionary<string, double>();
        static readonly List<string> _spikes = new List<string>();
        static int _recGcs, _recSpikesGc, _recSpikesMod, _recSpikesTurn, _recSpikesNone;
        static Quaternion _lastCam = Quaternion.identity;
        static Session _reportTo;

        public static void Record(string what, double ms)
        {
            _thisFrame.TryGetValue(what, out var v);
            _thisFrame[what] = v + ms;
            _modThisFrame += ms;
        }

        // Once per frame, at the start of Session.Update: closes out the previous frame.
        public static void EndFrame()
        {
            float ms = Time.unscaledDeltaTime * 1000f;
            _frameMs[_frameIdx] = ms;
            _frameIdx = (_frameIdx + 1) % Frames;
            _frameCount = Math.Min(_frameCount + 1, Frames);

            int gc = GC.CollectionCount(0);
            bool hadGc = gc != _lastGcCount;
            _lastGcCount = gc;

            float turned = 0f;
            var cam = Game.Camera;
            if (cam != null) { turned = Quaternion.Angle(_lastCam, cam.transform.rotation); _lastCam = cam.transform.rotation; }

            foreach (var kv in _thisFrame)
            {
                _window.TryGetValue(kv.Key, out var w);
                _window[kv.Key] = w + kv.Value;
            }
            _windowFrames++;
            if (Time.unscaledTime - _windowStart >= 1f)
            {
                _shown.Clear();
                foreach (var kv in _window) _shown[kv.Key] = kv.Value;
                _shownFrames = Math.Max(1, _windowFrames);
                _window.Clear();
                _windowFrames = 0;
                _windowStart = Time.unscaledTime;
            }

            if (_recording) RecordFrame(ms, hadGc, turned);
            if (ms >= SpikeMs && Game.InWorld && Session.Instance?.Loading != true) CountStutter(ms, hadGc, turned);

            _thisFrame.Clear();
            _modThisFrame = 0;
        }

        // ---------- what caused the stutters (for the [lag] line every minute) ----------

        static readonly Dictionary<string, int> _stutterCauses = new Dictionary<string, int>();

        static void CountStutter(float ms, bool gc, float turned)
        {
            var top = _thisFrame.OrderByDescending(kv => kv.Value).FirstOrDefault();
            string why = top.Key != null && top.Value > ms * 0.3f ? "mod: " + top.Key
                : gc ? "memory cleanup (GC)"
                : turned > 3f ? "game: loading the world while you turn/swim"
                : "game";
            _stutterCauses.TryGetValue(why, out var n);
            _stutterCauses[why] = n + 1;
        }

        // "game 9, memory cleanup (GC) 3, mod: emote wheel 1" and clears the counts.
        public static string TakeStutterCauses()
        {
            if (_stutterCauses.Count == 0) return null;
            var text = string.Join(", ", _stutterCauses.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}").ToArray());
            _stutterCauses.Clear();
            return text;
        }

        // ---------- /perf report ----------

        public static void StartReport(Session s, float seconds = 10f)
        {
            _reportTo = s;
            _recording = true;
            _recordUntil = Time.unscaledTime + seconds;
            _recFrames.Clear(); _recModules.Clear(); _spikes.Clear();
            _recGcs = _recSpikesGc = _recSpikesMod = _recSpikesTurn = _recSpikesNone = 0;
            NetLag.StartRecording();
            s.AddChat($"Recording performance for {seconds:0} s: play normally (turn the camera, swim around)...");
        }

        static void RecordFrame(float ms, bool gc, float turned)
        {
            _recFrames.Add(ms);
            if (gc) _recGcs++;
            foreach (var kv in _thisFrame)
            {
                _recModules.TryGetValue(kv.Key, out var v);
                _recModules[kv.Key] = v + kv.Value;
            }
            if (ms >= SpikeMs)
            {
                var top = _thisFrame.OrderByDescending(kv => kv.Value).FirstOrDefault();
                bool modHeavy = top.Key != null && top.Value > ms * 0.3f;
                string why;
                if (modHeavy) { _recSpikesMod++; why = $"mod '{top.Key}' {top.Value:0} ms"; }
                else if (gc) { _recSpikesGc++; why = "garbage collection"; }
                else if (turned > 3f) { _recSpikesTurn++; why = $"game (camera turning {turned:0}°/frame: world streaming / rendering)"; }
                else { _recSpikesNone++; why = "game (not the mod)"; }
                if (_spikes.Count < 25) _spikes.Add($"  {ms:0} ms: {why}, mod total {_modThisFrame:0.0} ms");
            }
            if (Time.unscaledTime >= _recordUntil) FinishReport();
        }

        static void FinishReport()
        {
            _recording = false;
            int n = Math.Max(1, _recFrames.Count);
            var sorted = _recFrames.OrderBy(x => x).ToList();
            float avg = _recFrames.Sum() / n;
            float p99 = sorted[Math.Min(sorted.Count - 1, (int)(sorted.Count * 0.99f))];
            float worst = sorted.Count > 0 ? sorted[sorted.Count - 1] : 0f;
            double modAvg = _recModules.Values.Sum() / n;
            int spikes = _recFrames.Count(x => x >= SpikeMs);

            var sb = new StringBuilder();
            sb.AppendLine($"[perf] {n} frames: {1000f / Math.Max(0.01f, avg):0} fps avg, frame {avg:0.0} ms avg / {p99:0} ms (1% worst) / {worst:0} ms worst");
            sb.AppendLine($"[perf] mod total {modAvg:0.00} ms per frame; garbage collections: {_recGcs}; heap {GC.GetTotalMemory(false) / (1024 * 1024)} MB");
            sb.AppendLine($"[perf] stutters over {SpikeMs:0} ms: {spikes}  (mod: {_recSpikesMod}, garbage collection: {_recSpikesGc}, game while turning: {_recSpikesTurn}, game other: {_recSpikesNone})");
            sb.AppendLine("[perf] mod parts, ms per frame:");
            foreach (var kv in _recModules.OrderByDescending(kv => kv.Value).Take(12))
                sb.AppendLine($"  {kv.Key}: {kv.Value / n:0.000}");
            if (_spikes.Count > 0) { sb.AppendLine("[perf] stutters:"); foreach (var s in _spikes) sb.AppendLine(s); }
            sb.Append($"[perf] settings: Performance={Plugin.Performance.Value}, upload {QualitySettings.asyncUploadBufferSize} MB / {QualitySettings.asyncUploadTimeSlice} ms, " +
                      $"loading priority {Application.backgroundLoadingPriority}, vSync {QualitySettings.vSyncCount}, target fps {Application.targetFrameRate}");
            var net = NetLag.StopRecording();
            sb.Append($"\n[perf] network: {net}");
            Plugin.Log.LogInfo(sb.ToString());

            var top = _recModules.OrderByDescending(kv => kv.Value).FirstOrDefault();
            _reportTo?.AddChat($"Perf: {1000f / Math.Max(0.01f, avg):0} fps, worst {worst:0} ms, {spikes} stutters " +
                               $"(mod {_recSpikesMod}, GC {_recSpikesGc}, game {_recSpikesTurn + _recSpikesNone}). Mod uses {modAvg:0.00} ms/frame" +
                               (top.Key != null ? $", most: {top.Key}." : ".") + " Full report is in LogOutput.log.");
            _reportTo?.AddChat("Network: " + net);
        }

        // ---------- F9 overlay ----------

        static GUIStyle _style;

        public static void OnGUI(Session s)
        {
            if (!Overlay || Event.current.type != EventType.Repaint) return;
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label) { fontSize = 13, richText = true };
                _style.normal.textColor = Color.white;
            }
            int n = Math.Max(1, _frameCount);
            float sum = 0f, worst = 0f;
            int from = _frameIdx - Math.Min(n, 120);
            for (int i = from; i < _frameIdx; i++)
            {
                float v = _frameMs[(i + Frames) % Frames];
                sum += v; worst = Math.Max(worst, v);
            }
            int m = Math.Min(n, 120);
            float avg = sum / Math.Max(1, m);
            var sb = new StringBuilder();
            sb.AppendLine($"<b>{1000f / Math.Max(0.01f, avg):0} fps</b>   frame {avg:0.0} ms   worst {worst:0} ms (2 s)");
            double modTotal = _shown.Values.Sum() / Math.Max(1, _shownFrames);
            sb.AppendLine($"mod: {modTotal:0.00} ms/frame   heap {GC.GetTotalMemory(false) / (1024 * 1024)} MB   GCs {GC.CollectionCount(0)}");
            foreach (var kv in _shown.OrderByDescending(kv => kv.Value).Take(6))
                sb.AppendLine($"  {kv.Key}: {kv.Value / Math.Max(1, _shownFrames):0.000} ms");
            sb.AppendLine($"players {s.RemotePlayers.Count() + 1}   creatures synced {s.Creatures.TrackedCount}   " +
                          $"bases building {s.Structures.Waiting}, far {s.Structures.FarCount}");
            sb.AppendLine(NetLag.Overlay());
            sb.Append("/perf in chat = 10 s report");
            var text = sb.ToString();
            var size = _style.CalcSize(new GUIContent(text));
            var r = new Rect(10, 10, Math.Max(size.x, 360) + 16, size.y + 12);
            GUI.color = new Color(0, 0, 0, 0.6f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(r.x + 8, r.y + 6, r.width, r.height), text, _style);
            // frame time graph
            var g = new Rect(r.x, r.yMax + 4, r.width, 50);
            GUI.color = new Color(0, 0, 0, 0.5f);
            GUI.DrawTexture(g, Texture2D.whiteTexture);
            int bars = Math.Min(n, (int)g.width / 2);
            for (int i = 0; i < bars; i++)
            {
                float v = _frameMs[(_frameIdx - bars + i + Frames) % Frames];
                float h = Mathf.Clamp(v / 100f, 0.02f, 1f) * g.height;
                GUI.color = v >= SpikeMs ? new Color(1f, 0.3f, 0.3f) : v >= 25f ? new Color(1f, 0.8f, 0.3f) : new Color(0.4f, 0.9f, 1f);
                GUI.DrawTexture(new Rect(g.x + i * 2, g.yMax - h, 2, h), Texture2D.whiteTexture);
            }
            GUI.color = Color.white;
        }
    }
}
