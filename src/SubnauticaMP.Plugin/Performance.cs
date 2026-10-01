using System;
using System.Diagnostics;
using UnityEngine;

namespace SubnauticaMP
{
    // Lets the game use more of the PC: Windows gives it more CPU time than other programs (except a multiplayer
    // server on the same PC: that one has to win, or everyone lags while you load terrain), Unity gets a bigger
    // buffer and more time per frame for moving textures / meshes onto the graphics card (the world streams in
    // faster, fewer pop-ins and hitches), and loading runs flat out while a loading screen hides it.
    // Turn off with Performance = false in the config.
    internal static class Performance
    {
        static bool _applied, _loadingFast, _serverHere;
        static int _normalSlice = 2;
        static ThreadPriority _normalPriority = ThreadPriority.BelowNormal;

        public static void Apply()
        {
            if (_applied || !Plugin.Performance.Value) return;
            _applied = true;
            SetPriority();

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

        // The server runs on this PC (hosting from the launcher or the game): the game mustn't outrank it.
        public static void ServerOnThisPc(bool here)
        {
            if (here == _serverHere) return;
            _serverHere = here;
            if (_applied) SetPriority();
        }

        static void SetPriority()
        {
            var want = _serverHere ? ProcessPriorityClass.Normal : ProcessPriorityClass.AboveNormal;
            try
            {
                using (var me = Process.GetCurrentProcess())
                {
                    if (me.PriorityClass == want) return;
                    me.PriorityClass = want;
                }
                Plugin.Log.LogInfo(_serverHere ? "Game priority normal: the server runs on this PC and gets the CPU first"
                                               : "Game priority raised (above normal)");
            }
            catch (Exception e) { Plugin.Log.LogInfo("Couldn't change the game's priority: " + e.Message); }
        }

        public static bool IsThisPc(string host)
        {
            if (string.IsNullOrEmpty(host)) return false;
            if (host == "localhost") return true;
            if (!System.Net.IPAddress.TryParse(host, out var ip)) return false;
            if (System.Net.IPAddress.IsLoopback(ip)) return true;
            try
            {
                foreach (var mine in System.Net.Dns.GetHostAddresses(System.Net.Dns.GetHostName()))
                    if (mine.Equals(ip)) return true;
            }
            catch { }
            return false;
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
