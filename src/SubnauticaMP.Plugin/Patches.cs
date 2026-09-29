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

            // for when overloads have the same length and we need a particular one
            void HookExact(string label, MethodInfo target, string prefix = null, string postfix = null)
            {
                try
                {
                    if (target == null) throw new MissingMethodException(label);
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
            Hook("story", Game.StoryGoal, "Execute", prefix: nameof(StoryGoalRan));
            Hook("lobby start", Game.GameInput, "get_AnyKeyDown", postfix: nameof(AnyKeyDown));
            Hook("intro sync", Game.EscapePod, "TriggerIntroCinematic", postfix: nameof(IntroCinematicStarted));

            // creatures: same spawns, same ids, one brain each
            Hook("spawns (groups)", Game.EntitySlotsPlaceholder, "Spawn", prefix: nameof(SlotGroupStart), postfix: nameof(SlotEnd));
            Hook("spawns (single)", Game.EntitySlot, "SpawnVirtualEntities", prefix: nameof(SlotStart), postfix: nameof(SlotEnd));
            Hook("spawn rolls", Game.CellManager, "GetPrefabForSlot", postfix: nameof(SlotRolled));
            HookExact("spawn ids", Game.FindMethod(Game.CellManager, "RegisterEntity", Game.LargeWorldEntity ?? typeof(void)), prefix: nameof(EntityRegistered));
            HookExact("spawned ids", Game.FindMethod(Game.DeferredSpawner, "InstantiateAsync", typeof(string)), postfix: nameof(Instantiating));
            Hook("creature brains", Game.Creature, "ChooseBestAction", prefix: nameof(ChooseAction));
            Hook("creature damage", Game.LiveMixin, "TakeDamage", prefix: nameof(Damaged));
            Hook("creature deaths", Game.LiveMixin, "Kill", prefix: nameof(Killed));

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

        static void StoryGoalRan(object[] __args)
        {
            if (ApplyingRemote || S == null || __args.Length < 2 || !(__args[0] is string key) || __args[1] == null) return;
            int type = Convert.ToInt32(__args[1]);
            // "Story"-type goals that already happened do nothing in the game; don't announce those
            if (__args[1].ToString() == "Story" && Game.GoalDone(key)) return;
            S.Story.OnLocalGoal(key, type);
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

        // ---------- creatures ----------

        static Transform _slotParent;   // EntitySlotsPlaceholder being filled (slot positions are relative to it)
        static string _slotKey;         // slot being filled right now
        static int _slotIndex;          // how many things it has made so far

        static void SlotGroupStart(Component __instance) { _slotParent = __instance != null ? __instance.transform : null; _slotKey = null; }
        static void SlotStart() { _slotParent = null; _slotKey = null; }
        static void SlotEnd() { _slotParent = null; _slotKey = null; }

        // Every PC uses the first roll anyone made for this slot.
        static void SlotRolled(object[] __args, ref object __result)
        {
            _slotKey = null;
            var creatures = S?.Creatures;
            if (creatures == null || __result == null || __args.Length == 0 || __args[0] == null) return;

            Vector3 pos;
            if (__args[0] is Component slotComp) pos = slotComp.transform.position;
            else if (_slotParent != null && Game.TryGet(__args[0].GetType(), __args[0], "localPosition") is Vector3 local) pos = _slotParent.TransformPoint(local);
            else return;

            var key = CreatureSync.SlotKey(pos);
            var fillerType = __result.GetType();
            if (creatures.TryGetSlot(key, out var slot))
            {
                var box = __result; // a boxed copy of the struct: change it, Harmony unboxes it back
                AccessTools.Field(fillerType, "classId")?.SetValue(box, string.IsNullOrEmpty(slot.ClassId) ? null : slot.ClassId);
                AccessTools.Field(fillerType, "count")?.SetValue(box, slot.Count);
                __result = box;
            }
            else
            {
                creatures.RecordSlot(key, Game.TryGet(fillerType, __result, "classId") as string,
                    Game.TryGet(fillerType, __result, "count") is int n ? n : 0);
            }
            _slotKey = key;
            _slotIndex = 0;
        }

        // The placeholders a slot makes get ids built from the slot, so they match on every PC.
        static void EntityRegistered(object[] __args)
        {
            if (_slotKey == null || S == null || __args.Length == 0 || !(__args[0] is Component lwe) || lwe == null) return;
            if (Game.VirtualPrefabIdentifier == null || lwe.GetComponent(Game.VirtualPrefabIdentifier) == null) return;
            Game.SetId(lwe.gameObject, _slotKey + "#" + _slotIndex++);
        }

        // When a placeholder turns into the real fish/plant, the real thing keeps the placeholder's id.
        static void Instantiating(object[] __args, object __result)
        {
            if (__result == null || __args.Length < 2 || !(__args[1] is Component owner) || owner == null) return;
            if (Game.VirtualPrefabIdentifier == null || !Game.VirtualPrefabIdentifier.IsInstanceOfType(owner)) return;
            var id = Game.Get(Game.UniqueIdentifier, owner, "Id") as string;
            if (id == null || !id.StartsWith(CreatureSync.IdPrefix)) return;

            var task = __result;
            Action keepId = () =>
            {
                var go = Game.TryGet(task.GetType(), task, "GetResult") as GameObject;
                if (go == null || owner == null) return;
                Game.Set(Game.UniqueIdentifier, owner, "Id", Guid.NewGuid().ToString("N")); // placeholder is about to go away
                Game.SetId(go, id);
            };
            var existing = Game.TryGet(Game.VirtualPrefabIdentifier, owner, "OnInstantiate") as Delegate;
            Game.Set(Game.VirtualPrefabIdentifier, owner, "OnInstantiate", Delegate.Combine(existing, keepId));
        }

        // Someone else runs this creature: its own brain stays off, it just follows them.
        static bool ChooseAction(Component __instance) =>
            S == null || __instance == null || !S.Creatures.IsPuppet(__instance.gameObject);

        static bool Damaged(Component __instance, object[] __args)
        {
            if (ApplyingRemote || S == null || __instance == null || __args.Length == 0) return true;
            float damage = __args[0] is float f ? f : 0f;
            var pos = __args.Length > 1 && __args[1] is Vector3 v ? v : __instance.transform.position;
            int type = __args.Length > 2 && __args[2] != null ? Convert.ToInt32(__args[2]) : 0;
            return !S.Creatures.ForwardDamage(__instance.gameObject, damage, type, pos);
        }

        static void Killed(Component __instance)
        {
            if (ApplyingRemote || S == null || __instance == null) return;
            S.Creatures.OnLocalDeath(__instance.gameObject);
        }
    }
}
