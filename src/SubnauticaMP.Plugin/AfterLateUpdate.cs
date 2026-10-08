using System;
using System.Collections.Generic;
using UnityEngine.LowLevel;

namespace SubnauticaMP
{
    // Runs code after every script's LateUpdate (the game's arm IK included) and before the skinned meshes are
    // drawn, by adding a step to the start of Unity's PostLateUpdate. The bat poses your arms here so nothing
    // the game does afterwards undoes it.
    internal static class AfterLateUpdate
    {
        public static event Action Run;
        struct BatPoseStep { } // just a name for the step in Unity's player loop
        static bool _installed;

        public static bool Install()
        {
            if (_installed) return true;
            try
            {
                var loop = PlayerLoop.GetCurrentPlayerLoop();
                for (int i = 0; i < loop.subSystemList.Length; i++)
                {
                    if (loop.subSystemList[i].type != typeof(UnityEngine.PlayerLoop.PostLateUpdate)) continue;
                    var post = loop.subSystemList[i];
                    var steps = new List<PlayerLoopSystem>(post.subSystemList ?? new PlayerLoopSystem[0]);
                    steps.Insert(0, new PlayerLoopSystem { type = typeof(BatPoseStep), updateDelegate = Tick });
                    post.subSystemList = steps.ToArray();
                    loop.subSystemList[i] = post;
                    PlayerLoop.SetPlayerLoop(loop);
                    _installed = true;
                    return true;
                }
            }
            catch (Exception e) { Game.WarnOnce("playerloop", "Couldn't hook the end of the frame: " + e.GetBaseException().Message); }
            return false;
        }

        static void Tick()
        {
            try { Run?.Invoke(); }
            catch (Exception e) { Game.WarnOnce("afterlate", "End-of-frame step failed: " + e.GetBaseException().Message); }
        }
    }
}
