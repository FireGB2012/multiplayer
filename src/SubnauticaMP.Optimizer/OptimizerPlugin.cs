using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Scripting;

namespace SubnauticaOptimizer
{
    // Small, safe speed-ups for Subnautica. Every tweak can be switched off in
    // BepInEx\config\com.subnauticamp.optimizer.cfg. Works with or without the multiplayer mod.
    //
    // Default (no visual change):
    //  * error reporting (Sentry) and analytics (Telemetry) off: the error reporter runs on EVERY message the game
    //    logs (and it logs a lot), and both try to reach the internet in the background
    //  * the game's own log only keeps warnings and errors (less writing to disk every frame)
    //  * the incremental garbage collector takes smaller bites per frame (fewer frame spikes)
    //  * at most 1 frame queued ahead of the GPU (less input lag)
    // Optional "performance mode" (visible): shorter shadows, 2 shadow cascades, nearer LOD switch, FPS cap.
    [BepInPlugin(Guid, "Subnautica Optimizer", "1.0.1")]
    public sealed class OptimizerPlugin : BaseUnityPlugin
    {
        public const string Guid = "com.subnauticamp.optimizer";

        ConfigEntry<bool> _noTelemetry, _quietLog, _smallGcSteps, _lowInputLag, _perfMode;
        ConfigEntry<float> _gcSliceMs, _shadowDistance, _lodBias;
        ConfigEntry<int> _fpsCap;

        static bool _telemetryOff;
        float _nextCheck;

        void Awake()
        {
            _noTelemetry = Config.Bind("General", "DisableTelemetry", true,
                "Turn off the game's error reporting (Sentry) and analytics. Saves CPU on every log message and background network calls.");
            _quietLog = Config.Bind("General", "QuietGameLog", true,
                "The game's own log keeps only warnings and errors (mods log through BepInEx as normal).");
            _smallGcSteps = Config.Bind("General", "SmallGcSteps", true,
                "The incremental garbage collector does smaller steps per frame: fewer frame-time spikes.");
            _gcSliceMs = Config.Bind("General", "GcStepMilliseconds", 1.5f,
                "Time per frame the garbage collector may use when SmallGcSteps is on (Unity's default is 3).");
            // new key (was LowInputLag, on by default): 1 queued frame can cost FPS when the graphics card is the limit
            _lowInputLag = Config.Bind("General", "LowInputLagMode", false,
                "At most 1 frame queued ahead of the graphics card (default 2): snappier mouse, but can lower FPS. Off by default.");
            _perfMode = Config.Bind("Performance mode", "Enabled", false,
                "Trade a little visual quality for FPS (shadows, LOD). Off by default because you can see it.");
            _shadowDistance = Config.Bind("Performance mode", "MaxShadowDistance", 60f, "Shadows stop at this distance (meters).");
            _lodBias = Config.Bind("Performance mode", "LodBias", 0.8f, "Lower = models switch to simpler versions sooner. 1 = game default.");
            _fpsCap = Config.Bind("Performance mode", "FpsCap", 0, "Frame rate limit (0 = none). Only works with V-Sync off.");

            if (_noTelemetry.Value) PatchTelemetry();
            SceneManager.sceneLoaded += (_, __) => Apply(false);
            Apply(true);
        }

        void Update()
        {
            // the game's options menu can put quality settings back: check now and then
            if (Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 10f;
            Apply(false);
        }

        void Apply(bool log)
        {
            var on = new List<string>();
            Try("telemetry", () => { if (_noTelemetry.Value && DisableTelemetryObjects()) on.Add("error reporting + analytics off"); });
            Try("log", () =>
            {
                if (!_quietLog.Value) return;
                Debug.unityLogger.filterLogType = LogType.Warning;
                on.Add("game log: warnings and errors only");
            });
            Try("gc", () =>
            {
                if (!_smallGcSteps.Value || !GarbageCollector.isIncremental) return;
                ulong ns = (ulong)(Mathf.Clamp(_gcSliceMs.Value, 0.5f, 5f) * 1_000_000f);
                if (GarbageCollector.incrementalTimeSliceNanoseconds != ns) GarbageCollector.incrementalTimeSliceNanoseconds = ns;
                on.Add($"GC steps {ns / 1_000_000f:0.#} ms");
            });
            Try("input lag", () =>
            {
                if (!_lowInputLag.Value) { if (QualitySettings.maxQueuedFrames == 1) QualitySettings.maxQueuedFrames = 2; return; } // undo 1.0.0's setting
                if (QualitySettings.maxQueuedFrames != 1) QualitySettings.maxQueuedFrames = 1;
                on.Add("1 queued frame");
            });
            Try("performance mode", () =>
            {
                if (!_perfMode.Value) return;
                if (QualitySettings.shadowDistance > _shadowDistance.Value) QualitySettings.shadowDistance = _shadowDistance.Value;
                if (QualitySettings.shadowCascades > 2) QualitySettings.shadowCascades = 2;
                if (Math.Abs(QualitySettings.lodBias - _lodBias.Value) > 0.01f) QualitySettings.lodBias = _lodBias.Value;
                if (_fpsCap.Value > 0 && Application.targetFrameRate != _fpsCap.Value) Application.targetFrameRate = _fpsCap.Value;
                on.Add($"performance mode (shadows {QualitySettings.shadowDistance:0} m, LOD {QualitySettings.lodBias:0.##}" +
                       (_fpsCap.Value > 0 ? $", {_fpsCap.Value} fps cap" : "") + ")");
            });
            if (log) Logger.LogInfo("Subnautica Optimizer 1.0.1: " + (on.Count > 0 ? string.Join(", ", on.ToArray()) : "everything off in the config"));
        }

        void Try(string what, Action action)
        {
            try { action(); }
            catch (Exception e) { Logger.LogWarning($"Optimizer: '{what}' skipped: {e.GetBaseException().Message}"); }
        }

        // ---------- error reporting / analytics ----------

        static readonly string[] ReportingTypes = { "SentrySdkManager", "SentrySdk", "Telemetry" };

        // Switches the reporting components off wherever they are (they unhook themselves in OnDisable).
        static bool DisableTelemetryObjects()
        {
            bool any = _telemetryOff;
            foreach (var name in ReportingTypes)
            {
                var type = AccessTools.TypeByName(name);
                if (type == null || !typeof(Behaviour).IsAssignableFrom(type)) continue;
                foreach (var o in Resources.FindObjectsOfTypeAll(type))
                    if (o is Behaviour b && b != null && b.enabled) b.enabled = false;
                any = true;
            }
            return any;
        }

        void PatchTelemetry()
        {
            try
            {
                var harmony = new Harmony(Guid);
                var telemetry = AccessTools.TypeByName("Telemetry");
                var skip = new HarmonyMethod(typeof(OptimizerPlugin).GetMethod(nameof(Skip), BindingFlags.Static | BindingFlags.NonPublic));
                foreach (var method in new[] { "SendAnalyticsEvent", "SessionStart", "ScheduledUpdate" })
                {
                    var m = telemetry != null ? AccessTools.Method(telemetry, method) : null;
                    if (m == null || m.ReturnType != typeof(void) && m.ReturnType != typeof(System.Collections.IEnumerator)) continue;
                    if (m.ReturnType == typeof(void)) harmony.Patch(m, prefix: skip);
                }
                _telemetryOff = true;
            }
            catch (Exception e) { Logger.LogWarning("Optimizer: couldn't stop analytics: " + e.GetBaseException().Message); }
        }

        static bool Skip() => false; // don't run the original
    }
}
