using System;
using System.Diagnostics;
using UnityEngine;

namespace SubnauticaMP
{
    // Lets the game use more of the PC: Windows gives it more CPU time than other programs, Unity gets a bigger
    // buffer and more time per frame for moving textures / meshes onto the graphics card (the world streams in
    // faster, fewer pop-ins and hitches), and loading runs flat out while a loading screen hides it.
    // Turn off with Performance = false in the config.
    internal static class Performance
    {
        static bool _applied, _loadingFast;
        static int _normalSlice = 2;
        static ThreadPriority _normalPriority = ThreadPriority.BelowNormal;

        public static void Apply()
        {
            if (_applied || !Plugin.Performance.Value) return;
            _applied = true;
            try
            {
                using (var me = Process.GetCurrentProcess()) me.PriorityClass = ProcessPriorityClass.AboveNormal;
            }
            catch (Exception e) { Plugin.Log.LogInfo("Couldn't raise the game's priority: " + e.Message); }

            try
            {
                // bigger buffer = fewer stalls when a lot streams in at once. The time per frame for uploading stays
                // the game's own while you play (more of it = hitches when turning / moving into new areas);
                // it only goes up behind loading screens.
                _normalSlice = QualitySettings.asyncUploadTimeSlice;
                _normalPriority = Application.backgroundLoadingPriority; // the game's own, put back after loading
                QualitySettings.asyncUploadBufferSize = Math.Max(QualitySettings.asyncUploadBufferSize, 64);
                QualitySettings.asyncUploadPersistentBuffer = true;
            }
            catch (Exception e) { Plugin.Log.LogInfo("Couldn't enlarge the upload buffer: " + e.Message); }

            Plugin.Log.LogInfo($"Performance: high priority, {SystemInfo.processorCount} CPU threads, {SystemInfo.systemMemorySize / 1024f:0.#} GB RAM, " +
                               $"upload buffer {QualitySettings.asyncUploadBufferSize} MB / {QualitySettings.asyncUploadTimeSlice} ms");
        }

        // While a loading screen is up nobody sees the frame rate: load as fast as possible.
        public static void Update(bool loadingScreen)
        {
            if (!_applied || !Plugin.Performance.Value || loadingScreen == _loadingFast) return;
            _loadingFast = loadingScreen;
            try
            {
                Application.backgroundLoadingPriority = loadingScreen ? ThreadPriority.High : _normalPriority;
                QualitySettings.asyncUploadTimeSlice = loadingScreen ? 16 : _normalSlice;
            }
            catch { }
        }
    }
}
