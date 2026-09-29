using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SubnauticaMP.Shared;
using UnityEngine;

namespace SubnauticaMP
{
    // Hooks into the game so we hear about things the local player does.
    // Each hook is applied on its own: if one fails, the rest still work.
    internal static class Patches
    {
        // True while we're applying something that came from the network, so we don't echo it back.
        internal static bool ApplyingRemote;

        public static void Apply(Harmony harmony)
        {
            var ok = new List<string>();
            var failed = new List<string>();

            void Hook(string label, Type type, string method, string prefix = null, string postfix = null)
            {
                try
                {
                    // with overloads, take the one with the most parameters: it's usually the one the others call
                    MethodInfo target = null;
                    if (type != null)
                        foreach (var m in AccessTools.GetDeclaredMethods(type))
                            if (m.Name == method && (target == null || m.GetParameters().Length > target.GetParameters().Length))
                                target = m;
                    if (target == null) throw new MissingMethodException(type?.Name, method);
                    Patch(harmony, target, Handler(prefix), Handler(postfix));
                    ok.Add(label);
                }
                catch (Exception e)
                {
                    failed.Add($"{label} ({e.GetBaseException().Message})");
                }
            }

            Hook("blueprints", Game.KnownTech, "NotifyAdd", prefix: nameof(KnownTechAdded));
            Hook("scans", Game.KnownTech, "NotifyAnalyze", prefix: nameof(KnownTechAnalyzed));
            Hook("databank", Game.PDAEncyclopedia, "Add", postfix: nameof(DatabankAdded));
            Hook("pickups", Game.Pickupable, "Pickup", prefix: nameof(PickedUp));
            Hook("breaking", Game.BreakableResource, "BreakIntoResources", prefix: nameof(Broken));
            Hook("vehicle deaths", Game.Vehicle, "OnKill", prefix: nameof(VehicleKilled));
            Hook("lobby", Game.SceneIntro, "IntroSequence", postfix: nameof(WrapIntro));
            Hook("building", Game.Constructable, "Construct", postfix: nameof(Built));
            Hook("deconstructing", Game.Constructable, "ProgressDeconstruction", prefix: nameof(Built));
            Hook("base deconstructing", Game.BaseDeconstructable, "Deconstruct", prefix: nameof(Built));
            Hook("lockers (add)", Game.ItemsContainer, "NotifyAddItem", postfix: nameof(ContainerChanged));
            Hook("lockers (remove)", Game.ItemsContainer, "NotifyRemoveItem", postfix: nameof(ContainerChanged));
            Hook("PDA logs", Game.PDALog, "Add", postfix: nameof(PdaLogAdded));
            Hook("dropping items", Game.Pickupable, "Drop", postfix: nameof(Dropped));
            Hook("doors", Game.Openable, "PlayOpenAnimation", prefix: nameof(DoorMoved));
            Hook("deaths", Game.Player, "OnKill", prefix: nameof(PlayerKilled));
            Hook("lobby start", Game.GameInput, "get_AnyKeyDown", postfix: nameof(AnyKeyDown));
            Hook("intro sync", Game.EscapePod, "TriggerIntroCinematic", postfix: nameof(IntroCinematicStarted));

            Plugin.Log.LogInfo("Synced features: " + string.Join(", ", ok.ToArray()));
            if (failed.Count > 0) Plugin.Log.LogWarning("Could NOT hook (these won't sync): " + string.Join("; ", failed.ToArray()));
        }

        static HarmonyMethod Handler(string name) =>
            name == null ? null : new HarmonyMethod(typeof(Patches).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic));

        // Harmony.Patch gained extra optional parameters between versions; call whichever one this BepInEx has.
        static void Patch(Harmony harmony, MethodBase target, HarmonyMethod prefix, HarmonyMethod postfix)
        {
            var patch = typeof(Harmony).GetMethods()
                .Where(m => m.Name == "Patch" && m.GetParameters().Length >= 3 && m.GetParameters()[0].ParameterType == typeof(MethodBase))
                .OrderBy(m => m.GetParameters().Length)
                .First();
            var args = new object[patch.GetParameters().Length];
            args[0] = target;
            args[1] = prefix;
            args[2] = postfix;
            patch.Invoke(harmony, args);
        }

        static Session S => Session.Instance != null && Session.Instance.Joined ? Session.Instance : null;

        static void KnownTechAdded(object[] __args)
        {
            if (ApplyingRemote || S == null || __args.Length == 0) return;
            S.SendUnlock(UnlockKind.Blueprint, __args[0]?.ToString());
        }

        static void KnownTechAnalyzed(object[] __args)
        {
            if (ApplyingRemote || S == null || __args.Length == 0 || __args[0] == null) return;
            // arg is KnownTech.AnalysisTech; we want its techType
            var tech = Game.Get(__args[0].GetType(), __args[0], "techType");
            S.SendUnlock(UnlockKind.Analyzed, tech?.ToString());
        }

        static void DatabankAdded(object[] __args, object __result)
        {
            // __result is null when the entry was already known
            if (ApplyingRemote || S == null || __result == null || __args.Length == 0) return;
            S.SendUnlock(UnlockKind.Databank, __args[0] as string);
        }

        static void PickedUp(Component __instance) => SendRemoved(__instance);
        static void Broken(Component __instance) => SendRemoved(__instance);

        static void SendRemoved(Component c)
        {
            if (ApplyingRemote || S == null || c == null) return;
            var id = Game.GetId(c.gameObject);
            if (string.IsNullOrEmpty(id)) return;
            S.Items.OnPickedUp(id);
            S.Send(new EntityRemovedPacket { EntityId = id });
        }

        static void WrapIntro(ref System.Collections.IEnumerator __result) => __result = Lobby.Hold(__result);

        static void AnyKeyDown(ref bool __result)
        {
            if (Lobby.ForceAnyKey) __result = true;
        }

        static void IntroCinematicStarted() => Lobby.OnIntroCinematicStarted();

        static void Built(Component __instance)
        {
            if (ApplyingRemote || S == null || __instance == null) return;
            S.Structures.OnLocalBuild(__instance.gameObject);
        }

        static void ContainerChanged(object __instance)
        {
            if (ApplyingRemote || S == null || __instance == null) return;
            S.Containers.OnLocalChange(__instance);
        }

        static void PdaLogAdded(object[] __args, object __result)
        {
            if (ApplyingRemote || S == null || __result == null || __args.Length == 0) return;
            S.SendUnlock(UnlockKind.PdaLog, __args[0] as string);
        }

        static void Dropped(Component __instance)
        {
            if (ApplyingRemote || S == null || __instance == null) return;
            S.Items.OnLocalDrop(__instance.gameObject);
        }

        static void DoorMoved(Component __instance, object[] __args)
        {
            if (ApplyingRemote || S == null || __instance == null || __args.Length < 2) return;
            if (__args[0] is bool open && __args[1] is float duration) S.Items.OnLocalDoor(__instance, open, duration);
        }

        static void PlayerKilled(Component __instance)
        {
            if (S == null || __instance == null) return;
            var p = __instance.transform.position;
            S.Send(new PlayerDiedPacket { Position = new Vec3(p.x, p.y, p.z) });
            S.AddChat("You died. Your teammates can see where.");
        }

        static void VehicleKilled(Component __instance)
        {
            if (ApplyingRemote || S == null || __instance == null) return;
            S.Vehicles.OnLocalVehicleDestroyed(__instance.gameObject);
        }
    }
}
