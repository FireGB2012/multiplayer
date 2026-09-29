using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SubnauticaMP
{
    // Every call into Subnautica's own code goes through here, looked up by name.
    // If an update renames something, just that one feature stops and the log says what's missing,
    // instead of the whole mod failing to load.
    internal static class Game
    {
        internal static Type Player, MainCamera, Vehicle, SubRoot, KnownTech, PDAEncyclopedia, CraftData,
            UniqueIdentifier, Pickupable, BreakableResource, DayNightCycle, LargeWorldEntity, CrafterLogic,
            StorageContainer, TechType, VFXConstructing, WorldForces, LightmappedPrefabs, SubConsoleCommand,
            SaveLoadManager;

        static readonly HashSet<string> Warned = new HashSet<string>();
        static readonly Dictionary<string, Func<object, object>> Getters = new Dictionary<string, Func<object, object>>();

        static Assembly _gameAssembly;

        public static void Init()
        {
            // BepInEx can start us before Unity has loaded the game's code; pull it in ourselves.
            try { _gameAssembly = Assembly.Load("Assembly-CSharp"); }
            catch (Exception e) { Plugin.Log.LogWarning("Couldn't load Assembly-CSharp yet: " + e.Message); }

            Player = Find("Player");
            MainCamera = Find("MainCamera");
            Vehicle = Find("Vehicle");
            SubRoot = Find("SubRoot");
            KnownTech = Find("KnownTech");
            PDAEncyclopedia = Find("PDAEncyclopedia");
            CraftData = Find("CraftData");
            UniqueIdentifier = Find("UniqueIdentifier");
            Pickupable = Find("Pickupable");
            BreakableResource = Find("BreakableResource");
            DayNightCycle = Find("DayNightCycle");
            LargeWorldEntity = Find("LargeWorldEntity");
            CrafterLogic = Find("CrafterLogic");
            StorageContainer = Find("StorageContainer");
            TechType = Find("TechType");
            VFXConstructing = Find("VFXConstructing");
            WorldForces = Find("WorldForces");
            LightmappedPrefabs = Find("LightmappedPrefabs");
            SubConsoleCommand = Find("SubConsoleCommand");
            SaveLoadManager = Find("SaveLoadManager");
        }

        static Type Find(string name)
        {
            var t = _gameAssembly?.GetType(name, false) ?? AccessTools.TypeByName(name);
            if (t == null) WarnOnce("type:" + name, "Game type not found: " + name);
            return t;
        }

        internal static void WarnOnce(string key, string message)
        {
            if (Warned.Add(key)) Plugin.Log.LogWarning(message);
        }

        // ---------- reflection helpers ----------

        // Reads a field, property, or no-argument method by name. Cached.
        internal static object Get(Type type, object target, string name)
        {
            if (type == null) return null;
            var key = type.FullName + "." + name;
            if (!Getters.TryGetValue(key, out var getter))
            {
                getter = MakeGetter(type, name);
                Getters[key] = getter;
                if (getter == null) WarnOnce("get:" + key, "Game member not found: " + key);
            }
            return getter?.Invoke(target);
        }

        static Func<object, object> MakeGetter(Type type, string name)
        {
            var f = AccessTools.Field(type, name);
            if (f != null) return o => f.GetValue(o);
            var p = AccessTools.Property(type, name);
            if (p != null && p.GetGetMethod(true) != null) return o => p.GetValue(o, null);
            var m = AccessTools.GetDeclaredMethods(type).FirstOrDefault(x => x.Name == name && x.GetParameters().Length == 0)
                    ?? AccessTools.Method(type, name, Type.EmptyTypes);
            if (m != null) return o => m.Invoke(o, null);
            return null;
        }

        internal static bool Set(Type type, object target, string name, object value)
        {
            if (type == null) return false;
            var f = AccessTools.Field(type, name);
            if (f != null) { f.SetValue(target, Convert.ChangeType(value, f.FieldType)); return true; }
            var p = AccessTools.Property(type, name);
            if (p != null && p.GetSetMethod(true) != null) { p.SetValue(target, Convert.ChangeType(value, p.PropertyType), null); return true; }
            return false;
        }

        // Finds a method by name whose first parameters accept `leading`.
        internal static MethodInfo FindMethod(Type type, string name, params Type[] leading)
        {
            if (type == null) return null;
            foreach (var m in AccessTools.GetDeclaredMethods(type))
            {
                if (m.Name != name) continue;
                var ps = m.GetParameters();
                if (ps.Length < leading.Length) continue;
                bool ok = true;
                for (int i = 0; i < leading.Length && ok; i++)
                    ok = ps[i].ParameterType.IsAssignableFrom(leading[i]);
                if (ok) return m;
            }
            return null;
        }

        // Calls a method, filling any extra parameters with their defaults. Anything called "verbose" is false
        // so remote unlocks don't spam popups.
        internal static object Call(Type type, object target, string name, params object[] leading)
        {
            var m = FindMethod(type, name, leading.Select(a => a?.GetType() ?? typeof(object)).ToArray());
            if (m == null)
            {
                WarnOnce("call:" + type?.Name + "." + name, $"Game method not found: {type?.Name}.{name}");
                return null;
            }
            var ps = m.GetParameters();
            var args = new object[ps.Length];
            for (int i = 0; i < ps.Length; i++)
            {
                if (i < leading.Length) args[i] = leading[i];
                else if (ps[i].Name == "verbose" && ps[i].ParameterType == typeof(bool)) args[i] = false;
                else if (ps[i].IsOptional && ps[i].DefaultValue != DBNull.Value) args[i] = ps[i].DefaultValue;
                else args[i] = ps[i].ParameterType.IsValueType ? Activator.CreateInstance(ps[i].ParameterType) : null;
            }
            return m.Invoke(m.IsStatic ? null : target, args);
        }

        static T As<T>(object o) where T : class => o as T;

        // ---------- player ----------

        public static Component LocalPlayer => As<Component>(Get(Player, null, "main"));

        public static Camera Camera
        {
            get
            {
                var cam = As<Camera>(Get(MainCamera, null, "camera"));
                return cam != null ? cam : Camera.main;
            }
        }

        public static Component DayNight => As<Component>(Get(DayNightCycle, null, "main"));

        public static bool InWorld
        {
            get
            {
                var p = LocalPlayer;
                var d = DayNight;
                return p != null && d != null;
            }
        }

        public static bool Is(Component player, string method) => Get(Player, player, method) is bool b && b;

        public static Component PlayerVehicle(Component player)
        {
            var v = As<Component>(Get(Player, player, "GetVehicle"));
            return v != null ? v : null;
        }

        public static Component PlayerSub(Component player)
        {
            var s = As<Component>(Get(Player, player, "currentSub"));
            return s != null ? s : null;
        }

        public static bool IsPiloting(Component player) => Get(Player, player, "mode")?.ToString() == "Piloting";

        public static bool IsCyclops(Component subRoot) => subRoot != null && Get(SubRoot, subRoot, "isCyclops") is bool b && b;

        public static string CurrentSaveSlot()
        {
            try
            {
                var mgr = Get(SaveLoadManager, null, "main");
                var slot = mgr != null ? Get(SaveLoadManager, mgr, "GetCurrentSlot") as string : null;
                return string.IsNullOrEmpty(slot) ? "default" : slot;
            }
            catch { return "default"; }
        }

        // ---------- ids ----------

        public static string GetId(GameObject go)
        {
            if (go == null || UniqueIdentifier == null) return null;
            var uid = go.GetComponent(UniqueIdentifier);
            return uid != null ? Get(UniqueIdentifier, uid, "Id") as string : null;
        }

        public static void SetId(GameObject go, string id)
        {
            var uid = go.GetComponent(UniqueIdentifier);
            if (uid != null && !Set(UniqueIdentifier, uid, "Id", id))
                WarnOnce("setid", "Couldn't set UniqueIdentifier.Id; synced objects may duplicate after reload");
        }

        public static GameObject FindById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (Get(UniqueIdentifier, null, "identifiers") is IDictionary dict && dict.Contains(id))
            {
                var c = dict[id] as Component;
                return c != null ? c.gameObject : null;
            }
            return null;
        }

        // ---------- tech ----------

        public static string TechTypeOf(GameObject go)
        {
            var tt = Call(CraftData, null, "GetTechType", go);
            var name = tt?.ToString();
            return string.IsNullOrEmpty(name) || name == "None" ? null : name;
        }

        public static object ParseTechType(string name)
        {
            if (TechType == null || string.IsNullOrEmpty(name)) return null;
            try { return Enum.Parse(TechType, name); }
            catch { WarnOnce("tt:" + name, "Unknown TechType " + name); return null; }
        }

        public static void AddBlueprint(string name)
        {
            var tt = ParseTechType(name);
            if (tt != null) Call(KnownTech, null, "Add", tt);
        }

        public static void AddAnalyzed(string name)
        {
            var tt = ParseTechType(name);
            if (tt != null) Call(KnownTech, null, "Analyze", tt);
        }

        public static void AddDatabank(string key) => Call(PDAEncyclopedia, null, "Add", key);

        public static IEnumerable<string> KnownBlueprints() => Names(Get(KnownTech, null, "knownTech"));
        public static IEnumerable<string> AnalyzedTech() => Names(Get(KnownTech, null, "analyzedTech"));

        public static IEnumerable<string> DatabankKeys()
        {
            if (Get(PDAEncyclopedia, null, "entries") is IDictionary dict)
                foreach (var k in dict.Keys) yield return k.ToString();
        }

        static IEnumerable<string> Names(object collection)
        {
            if (collection is IEnumerable e)
                foreach (var x in e) yield return x.ToString();
        }

        // ---------- time ----------

        public static double? GetTime()
        {
            var d = DayNight;
            if (d == null) return null;
            var t = Get(DayNightCycle, d, "timePassedAsDouble");
            return t == null ? (double?)null : Convert.ToDouble(t);
        }

        public static void SetTime(double time)
        {
            var d = DayNight;
            if (d == null) return;
            if (!Set(DayNightCycle, d, "timePassedAsDouble", time) && !Set(DayNightCycle, d, "timePassed", time))
                WarnOnce("settime", "Couldn't set the time of day; time won't sync");
        }

        // ---------- world objects ----------

        public static bool IsPickupOrBreakable(GameObject go) =>
            (Pickupable != null && go.GetComponent(Pickupable) != null) ||
            (BreakableResource != null && go.GetComponent(BreakableResource) != null);

        // True if the object is in someone's hands, a locker or a vehicle rather than lying in the world.
        public static bool IsStored(GameObject go)
        {
            var t = go.transform.parent;
            while (t != null)
            {
                if ((Player != null && t.GetComponent(Player) != null) ||
                    (StorageContainer != null && t.GetComponent(StorageContainer) != null) ||
                    (Vehicle != null && t.GetComponent(Vehicle) != null))
                    return true;
                t = t.parent;
            }
            return false;
        }

        // ---------- vehicles ----------

        public static List<GameObject> FindVehicles()
        {
            var list = new List<GameObject>();
            if (Vehicle != null)
                foreach (var o in UnityEngine.Object.FindObjectsOfType(Vehicle))
                    list.Add(((Component)o).gameObject);
            if (SubRoot != null)
                foreach (var o in UnityEngine.Object.FindObjectsOfType(SubRoot))
                    if (IsCyclops((Component)o)) list.Add(((Component)o).gameObject);
            return list;
        }

        // Remote-driven vehicles: we move them by hand, so switch off their physics here.
        public static void SetRemoteDriven(GameObject go, bool remote)
        {
            var rb = go.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = remote;
            if (WorldForces != null && go.GetComponent(WorldForces) is Behaviour wf) wf.enabled = !remote;
        }

        sealed class PrefabWaiter
        {
            public GameObject Result;
            public bool Done;
            public void OnLoaded(GameObject go) { Result = go; Done = true; }
        }

        // Builds a copy of someone else's vehicle in our world.
        public static IEnumerator SpawnVehicle(string techName, Vector3 pos, Quaternion rot, string id, Action<GameObject> done)
        {
            GameObject go = null;
            var tt = ParseTechType(techName);
            if (tt == null) { done(null); yield break; }

            if (techName == "Cyclops")
            {
                var lp = Get(LightmappedPrefabs, null, "main");
                var request = FindMethod(LightmappedPrefabs, "RequestScenePrefab", typeof(string));
                var subCmd = Get(SubConsoleCommand, null, "main");
                if (lp == null || request == null || subCmd == null)
                {
                    WarnOnce("cyclops", "Can't spawn Cyclops: game API not found");
                    done(null);
                    yield break;
                }
                var waiter = new PrefabWaiter();
                var callbackType = request.GetParameters()[1].ParameterType;
                var callback = Delegate.CreateDelegate(callbackType, waiter, typeof(PrefabWaiter).GetMethod(nameof(PrefabWaiter.OnLoaded)));
                request.Invoke(lp, new object[] { "cyclops", callback });
                float waited = 0f;
                while (!waiter.Done && waited < 30f) { waited += Time.unscaledDeltaTime; yield return null; }
                if (waiter.Result == null) { done(null); yield break; }
                Call(SubConsoleCommand, subCmd, "OnSubPrefabLoaded", waiter.Result);
                go = Get(SubConsoleCommand, subCmd, "GetLastCreatedSub") as GameObject;
            }
            else
            {
                var task = Call(CraftData, null, "GetPrefabForTechTypeAsync", tt);
                if (task is IEnumerator e) yield return e;
                var prefab = task == null ? null : Get(task.GetType(), task, "GetResult") as GameObject;
                if (prefab == null)
                {
                    WarnOnce("prefab:" + techName, "No prefab for " + techName);
                    done(null);
                    yield break;
                }
                go = UnityEngine.Object.Instantiate(prefab, pos, rot);
            }

            if (go == null) { done(null); yield break; }
            go.transform.SetPositionAndRotation(pos, rot);
            go.SetActive(true);
            TryDo("register", () => Call(LargeWorldEntity, null, "Register", go));
            TryDo("craftend", () => Call(CrafterLogic, null, "NotifyCraftEnd", go, tt));
            if (Vehicle != null && go.GetComponent(Vehicle) is Component v)
            {
                TryDo("lazyinit", () => Call(Vehicle, v, "LazyInitialize"));
                Set(Vehicle, v, "constructionFallOverride", false);
            }
            yield return new WaitForEndOfFrame();
            if (VFXConstructing != null)
                foreach (var vfx in go.GetComponentsInChildren(VFXConstructing))
                    TryDo("vfx", () => Call(VFXConstructing, vfx, "EndGracefully"));
            SetId(go, id);
            done(go);
        }

        static void TryDo(string what, Action a)
        {
            try { a(); }
            catch (Exception ex) { WarnOnce("try:" + what, $"{what} failed: {ex.GetBaseException().Message}"); }
        }
    }
}
