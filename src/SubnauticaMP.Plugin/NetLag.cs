using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace SubnauticaMP
{
    // Tells apart the kinds of multiplayer lag, from the other players' position updates (sent 20x a second
    // with the sender's clock):
    //  * "froze": the gap between two updates on THEIR clock was long = their game hitched (that player stutters
    //    for everyone, nothing anyone else can fix)
    //  * "late": the update took much longer than usual to get here = server PC busy or internet
    //  * "send": our own updates waited to go out = this PC too busy
    // Written to the log every minute when something happened, shown on F9, and part of /perf.
    internal static class NetLag
    {
        const float FrozeSeconds = 0.2f;  // normal gap is 0.05 s
        const float LateSeconds = 0.15f;
        const float ReportEvery = 60f;

        sealed class Stat { public int Froze, Late; public float WorstFroze, WorstLate; }

        static readonly Dictionary<string, Stat> _window = new Dictionary<string, Stat>(); // since the last log line
        static readonly Dictionary<string, Stat> _perf = new Dictionary<string, Stat>();   // during a /perf recording
        static bool _recording;
        static int _sendWaitMax, _perfSendWaitMax;
        static float _nextReport;
        static int _frames, _stutters;
        static float _frameTime, _worstFrame;
        public static float LastLateMs, LastFrozeMs; // for the F9 overlay
        public static string LastWho = "";

        static Stat For(Dictionary<string, Stat> d, string who)
        {
            if (!d.TryGetValue(who, out var s)) d[who] = s = new Stat();
            return s;
        }

        public static void Froze(string who, float seconds)
        {
            if (seconds < FrozeSeconds) return;
            Add(who, seconds, true);
            LastFrozeMs = seconds * 1000f; LastWho = who;
        }

        public static void Late(string who, float seconds)
        {
            if (seconds < LateSeconds) return;
            Add(who, seconds, false);
            LastLateMs = seconds * 1000f; LastWho = who;
        }

        static void Add(string who, float seconds, bool froze)
        {
            who = string.IsNullOrEmpty(who) ? "?" : who;
            foreach (var d in _recording ? new[] { _window, _perf } : new[] { _window })
            {
                var s = For(d, who);
                if (froze) { s.Froze++; s.WorstFroze = Mathf.Max(s.WorstFroze, seconds); }
                else { s.Late++; s.WorstLate = Mathf.Max(s.WorstLate, seconds); }
            }
        }

        // Once per frame (cheap): our own send delays, and the minute log line.
        public static void Tick(Session s)
        {
            if (s == null || !s.Joined) return;
            if (Game.InWorld && !s.Loading)
            {
                float dt = Time.unscaledDeltaTime;
                _frames++; _frameTime += dt;
                if (dt > 0.05f) _stutters++;
                if (dt > _worstFrame) _worstFrame = dt;
            }
            int wait = s.TakeSendWaitMs();
            if (wait > _sendWaitMax) _sendWaitMax = wait;
            if (_recording && wait > _perfSendWaitMax) _perfSendWaitMax = wait;
            if (Time.unscaledTime < _nextReport) return;
            _nextReport = Time.unscaledTime + ReportEvery;
            var text = Describe(_window, _sendWaitMax) ?? "updates from everyone on time";
            if (_frames > 0)
                Plugin.Log.LogInfo($"[lag] last minute: {_frames / Mathf.Max(0.01f, _frameTime):0} fps, {_stutters} stutter(s) over 50 ms " +
                                   $"(worst {_worstFrame * 1000f:0} ms); network: {text}");
            _frames = _stutters = 0;
            _frameTime = _worstFrame = 0f;
            _window.Clear();
            _sendWaitMax = 0;
            LastLateMs = LastFrozeMs = 0f;
        }

        public static void StartRecording() { _perf.Clear(); _perfSendWaitMax = 0; _recording = true; }

        public static string StopRecording()
        {
            _recording = false;
            return Describe(_perf, _perfSendWaitMax) ?? "no network lag (updates from everyone arrived on time)";
        }

        static string Describe(Dictionary<string, Stat> d, int sendWait)
        {
            var parts = new List<string>();
            foreach (var kv in d.OrderByDescending(kv => kv.Value.Froze + kv.Value.Late))
            {
                var st = kv.Value;
                if (st.Froze > 0) parts.Add($"{kv.Key}'s game froze {st.Froze}x (worst {st.WorstFroze * 1000f:0} ms)");
                if (st.Late > 0) parts.Add($"updates from {kv.Key} arrived late {st.Late}x (worst {st.WorstLate * 1000f:0} ms: server PC or internet)");
            }
            if (sendWait >= 100) parts.Add($"our own updates waited up to {sendWait} ms to go out (this PC busy)");
            return parts.Count == 0 ? null : string.Join("; ", parts.ToArray());
        }

        public static string Overlay()
        {
            var sb = new StringBuilder("net: ");
            if (LastFrozeMs <= 0 && LastLateMs <= 0) return sb.Append("all on time").ToString();
            if (LastFrozeMs > 0) sb.Append($"{LastWho} froze {LastFrozeMs:0} ms  ");
            if (LastLateMs > 0) sb.Append($"late {LastLateMs:0} ms");
            return sb.ToString();
        }
    }
}
