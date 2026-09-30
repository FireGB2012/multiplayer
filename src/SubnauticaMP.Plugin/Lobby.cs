using System.Collections;
using UnityEngine;

namespace SubnauticaMP
{
    // Holds a new game's intro on a black "waiting for players" screen until the host starts,
    // then lets everyone's lifepod intro roll at the same time.
    internal static class Lobby
    {
        public static bool Holding { get; private set; }
        public static bool WaitingForLoads { get; private set; } // holding because others are still loading
        public static bool IntroGo; // the server said everyone has loaded
        const float LoadWaitMax = 100f;
        public static bool ForceAnyKey { get; private set; } // skips the game's own "press any key" prompt
        static float _forceUntil;

        // Wraps uGUI_SceneIntro.IntroSequence (see Patches).
        public static IEnumerator Hold(IEnumerator intro)
        {
            var s = Session.Instance;

            // launcher join still connecting? give it a moment so we know if there's a lobby
            float waited = 0f;
            while (s != null && s.Connecting && waited < 20f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            if (s != null && s.InLobby)
            {
                Holding = true;
                while (s.InLobby) yield return null;
                Holding = false;

                if (s.Joined && s.WorldStarted)
                {
                    ForceAnyKey = true;
                    _forceUntil = Time.unscaledTime + 20f;
                }
            }

            // a world that was just started: wait for everyone else to finish loading, then all go together
            if (s != null && s.Joined && s.FreshStart)
            {
                IntroGo = false;
                s.Send(new SubnauticaMP.Shared.IntroPacket());
                Holding = WaitingForLoads = true;
                float waitedLoads = 0f;
                while (s.Joined && !IntroGo && waitedLoads < LoadWaitMax)
                {
                    waitedLoads += Time.unscaledDeltaTime;
                    yield return null;
                }
                Holding = WaitingForLoads = false;
                s.FreshStart = false;
                ForceAnyKey = true;
                _forceUntil = Time.unscaledTime + 20f;
            }

            yield return intro;
            ForceAnyKey = false;
        }

        public static void OnIntroCinematicStarted() => ForceAnyKey = false;

        public static void Update()
        {
            if (ForceAnyKey && Time.unscaledTime > _forceUntil) ForceAnyKey = false;
        }
    }
}
