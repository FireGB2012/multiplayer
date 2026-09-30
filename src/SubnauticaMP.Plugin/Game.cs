using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SubnauticaMP.Shared;
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
            SaveLoadManager, MainMenuType, MainMenuLoadButton, SceneIntro, GameInput, EscapePod, GameModeUtils,
            GameModeOption, GameModeEnum, ProtobufSerializer, TaskResultOfT, Base, Constructable, BaseDeconstructable,
            ItemsContainer, InventoryItem, PDALog, PDAScanner, PingInstance, PingType, Openable, Inventory, Survival,
            LiveMixin, VehicleDockingBay, CyclopsLightingPanel, CyclopsSilentRunningAbilityButton, CyclopsMotorModeButton,
            SubControl, LargeWorldStreamer, StoryGoal, StoryGoalManager, StoryGoalScheduler, GoalType, CrashedShipExploder,
            Creature, EcoTarget, LastTarget, CellManager, EntitySlot, EntitySlotsPlaceholder, VirtualPrefabIdentifier, DeferredSpawner,
            PowerSource, SolarPanel, ThermalPlant, BaseBioReactor, BaseNuclearReactor, Crafter, SubFire, Fire, PrefabSpawnBase,
            PickPrefab, WaterPark, WaterParkCreature, Builder,
            MainMenuRightSide, MainMenuLoadPanel, MainMenuEmailHandler, MainMenuGroup, IngameMenu, ErrorMessage, CrashHome, AvatarInputHandler, RandomStart;

        static readonly HashSet<string> Warned = new HashSet<string>();
        // keyed by (type, name) so looking one up doesn't build a string every call (these run every frame)
        static readonly Dictionary<(Type, string), Func<object, object>> Getters = new Dictionary<(Type, string), Func<object, object>>();
        static readonly Dictionary<(Type, string), MemberInfo> Setters = new Dictionary<(Type, string), MemberInfo>();

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
            MainMenuType = Find("uGUI_MainMenu");
            MainMenuLoadButton = Find("MainMenuLoadButton");
            SceneIntro = Find("uGUI_SceneIntro");
            GameInput = Find("GameInput");
            EscapePod = Find("EscapePod");
            GameModeUtils = Find("GameModeUtils");
            GameModeOption = Find("GameModeOption");
            GameModeEnum = Find("GameMode");
            ProtobufSerializer = Find("ProtobufSerializer");
            TaskResultOfT = Find("TaskResult`1");
            Base = Find("Base");
            Constructable = Find("Constructable");
            BaseDeconstructable = Find("BaseDeconstructable");
            ItemsContainer = Find("ItemsContainer");
            InventoryItem = Find("InventoryItem");
            PDALog = Find("PDALog");
            PDAScanner = Find("PDAScanner");
            PingInstance = Find("PingInstance");
            PingType = Find("PingType");
            SceneCleanerPreserve = Find("SceneCleanerPreserve");
            Openable = Find("Openable");
            Inventory = Find("Inventory");
            Survival = Find("Survival");
            LiveMixin = Find("LiveMixin");
            VehicleDockingBay = Find("VehicleDockingBay");
            CyclopsLightingPanel = Find("CyclopsLightingPanel");
            CyclopsSilentRunningAbilityButton = Find("CyclopsSilentRunningAbilityButton");
            CyclopsMotorModeButton = Find("CyclopsMotorModeButton");
            SubControl = Find("SubControl");
            LargeWorldStreamer = Find("LargeWorldStreamer");
            StoryGoal = Find("Story.StoryGoal");
            StoryGoalManager = Find("Story.StoryGoalManager");
            StoryGoalScheduler = Find("Story.StoryGoalScheduler");
            GoalType = Find("Story.GoalType");
            CrashedShipExploder = Find("CrashedShipExploder");
            Creature = Find("Creature");
            EcoTarget = Find("EcoTarget");
            LastTarget = Find("LastTarget");
            CellManager = Find("CellManager");
            EntitySlot = Find("EntitySlot");
            EntitySlotsPlaceholder = Find("EntitySlotsPlaceholder");
            VirtualPrefabIdentifier = Find("VirtualPrefabIdentifier");
            DeferredSpawner = Find("DeferredSpawner");
            PowerSource = Find("PowerSource");
            SolarPanel = Find("SolarPanel");
            ThermalPlant = Find("ThermalPlant");
            BaseBioReactor = Find("BaseBioReactor");
            BaseNuclearReactor = Find("BaseNuclearReactor");
            Crafter = Find("Crafter");
            SubFire = Find("SubFire");
            Fire = Find("Fire");
            PrefabSpawnBase = Find("PrefabSpawnBase");
            PickPrefab = Find("PickPrefab");
            WaterPark = Find("WaterPark");
            WaterParkCreature = Find("WaterParkCreature");
            Builder = Find("Builder");
            MainMenuRightSide = Find("MainMenuRightSide");
            MainMenuLoadPanel = Find("MainMenuLoadPanel");
            MainMenuEmailHandler = Find("MainMenuEmailHandler");
            MainMenuGroup = Find("MainMenuGroup");
            IngameMenu = Find("IngameMenu");
            ErrorMessage = Find("ErrorMessage");
            CrashHome = Find("CrashHome");
            AvatarInputHandler = Find("AvatarInputHandler");
            RandomStart = Find("RandomStart");
        }

        // ---------- story ----------

        public static bool GoalDone(string key)
        {
            var mgr = TryGet(StoryGoalManager, null, "main");
            var done = mgr != null ? TryGet(StoryGoalManager, mgr, "completedGoals") : null;
            return done is System.Collections.Generic.ICollection<string> set && set.Contains(key);
        }

        // Runs a story goal the way the game would, and drops it from the "later" schedule if it was waiting there.
        public static void RunGoal(string key, int goalType)
        {
            if (GoalDone(key) || GoalType == null) return;
            var sched = TryGet(StoryGoalScheduler, null, "main");
            if (sched != null && TryGet(StoryGoalScheduler, sched, "schedule") is IList list)
                for (int i = list.Count - 1; i >= 0; i--)
                    if (list[i] != null && TryGet(list[i].GetType(), list[i], "goalKey") as string == key) list.RemoveAt(i);
            Call(StoryGoal, null, "Execute", key, Enum.ToObject(GoalType, goalType));
        }

        public static AuroraPacket ReadAurora()
        {
            var ex = TryGet(CrashedShipExploder, null, "main");
            if (ex == null) return null;
            var c = TryGet(CrashedShipExploder, ex, "timeToStartCountdown");
            var w = TryGet(CrashedShipExploder, ex, "timeToStartWarning");
            if (c == null || w == null) return null;
            return new AuroraPacket { TimeToStartCountdown = Convert.ToSingle(c), TimeToStartWarning = Convert.ToSingle(w) };
        }

        public static void ApplyAurora(AuroraPacket a)
        {
            var ex = TryGet(CrashedShipExploder, null, "main");
            if (ex == null || a == null) return;
            Set(CrashedShipExploder, ex, "timeToStartCountdown", a.TimeToStartCountdown);
            Set(CrashedShipExploder, ex, "timeToStartWarning", a.TimeToStartWarning);
        }

        // ---------- vehicle health / energy (0..1, -1 = unknown) ----------

        public static float HealthOf(GameObject go)
        {
            var live = LiveMixin != null ? go.GetComponent(LiveMixin) : null;
            if (live == null) return -1f;
            var h = TryGet(LiveMixin, live, "health");
            var max = TryGet(LiveMixin, live, "maxHealth");
            if (h == null || max == null || Convert.ToSingle(max) <= 0f) return -1f;
            return Mathf.Clamp01(Convert.ToSingle(h) / Convert.ToSingle(max));
        }

        public static void SetHealth(GameObject go, float fraction)
        {
            var live = LiveMixin != null ? go.GetComponent(LiveMixin) : null;
            var max = live != null ? TryGet(LiveMixin, live, "maxHealth") : null;
            if (max == null || fraction < 0f) return;
            Set(LiveMixin, live, "health", Convert.ToSingle(max) * fraction);
        }

        static IEnumerable<object> EnergySources(GameObject go)
        {
            var v = Vehicle != null ? go.GetComponent(Vehicle) : null;
            var ei = v != null ? TryGet(Vehicle, v, "energyInterface") : null;
            if (ei != null && TryGet(ei.GetType(), ei, "sources") is IEnumerable sources)
                foreach (var s in sources) if (s != null && !(s is UnityEngine.Object o && o == null)) yield return s;
        }

        static object PowerRelayOf(GameObject go)
        {
            var sub = SubRoot != null ? go.GetComponent(SubRoot) : null;
            return sub != null ? TryGet(SubRoot, sub, "powerRelay") : null;
        }

        public static float EnergyOf(GameObject go)
        {
            float charge = 0f, capacity = 0f;
            foreach (var s in EnergySources(go))
            {
                charge += Convert.ToSingle(TryGet(s.GetType(), s, "charge") ?? 0f);
                capacity += Convert.ToSingle(TryGet(s.GetType(), s, "capacity") ?? 0f);
            }
            var relay = PowerRelayOf(go);
            if (relay != null)
            {
                charge += Convert.ToSingle(TryGet(relay.GetType(), relay, "GetPower") ?? 0f);
                capacity += Convert.ToSingle(TryGet(relay.GetType(), relay, "GetMaxPower") ?? 0f);
            }
            return capacity > 0f ? Mathf.Clamp01(charge / capacity) : -1f;
        }

        public static void SetEnergy(GameObject go, float fraction)
        {
            if (fraction < 0f) return;
            foreach (var s in EnergySources(go))
            {
                float charge = Convert.ToSingle(TryGet(s.GetType(), s, "charge") ?? 0f);
                float capacity = Convert.ToSingle(TryGet(s.GetType(), s, "capacity") ?? 0f);
                if (capacity > 0f) Call(s.GetType(), s, "ModifyCharge", capacity * fraction - charge);
            }
            var relay = PowerRelayOf(go);
            if (relay != null)
            {
                float power = Convert.ToSingle(TryGet(relay.GetType(), relay, "GetPower") ?? 0f);
                float max = Convert.ToSingle(TryGet(relay.GetType(), relay, "GetMaxPower") ?? 0f);
                if (max > 0f) Call(relay.GetType(), relay, "ModifyPower", max * fraction - power);
            }
        }

        // ---------- docking ----------

        public static bool IsDocked(GameObject vehicle)
        {
            var v = Vehicle != null ? vehicle.GetComponent(Vehicle) : null;
            return v != null && TryGet(Vehicle, v, "docked") is bool b && b;
        }

        static Component NearestBay(Vector3 pos, float maxDistance)
        {
            if (VehicleDockingBay == null) return null;
            Component best = null;
            float bestDist = maxDistance;
            foreach (var bay in SceneIndex.All(VehicleDockingBay))
            {
                float d = Vector3.Distance(bay.transform.position, pos);
                if (d < bestDist) { bestDist = d; best = bay; }
            }
            return best;
        }

        // The bay this vehicle sits in (for telling others where it docked).
        public static Vector3? DockPositionOf(GameObject vehicle)
        {
            if (VehicleDockingBay == null) return null;
            var v = vehicle.GetComponent(Vehicle);
            foreach (var bay in SceneIndex.All(VehicleDockingBay))
            {
                if (ReferenceEquals(TryGet(VehicleDockingBay, bay, "dockedVehicle"), v)) return bay.transform.position;
            }
            var near = NearestBay(vehicle.transform.position, 15f);
            return near != null ? near.transform.position : (Vector3?)null;
        }

        // Nitrox's way: set the dock up by hand, without the parts that assume the local player is driving.
        public static bool DockRemote(GameObject vehicle, Vector3 bayPosition)
        {
            var v = Vehicle != null ? vehicle.GetComponent(Vehicle) : null;
            var bay = NearestBay(bayPosition, 15f);
            if (v == null || bay == null) return false;
            var sub = Call(VehicleDockingBay, bay, "GetSubRoot") as Component;
            Set(VehicleDockingBay, bay, "dockedVehicle", v);
            TryDo("unregister", () =>
            {
                var lws = TryGet(LargeWorldStreamer, null, "main");
                var cells = lws != null ? TryGet(LargeWorldStreamer, lws, "cellManager") : null;
                if (cells != null) Call(cells.GetType(), cells, "UnregisterEntity", vehicle);
            });
            if (sub != null) vehicle.transform.SetParent(sub.transform, true);
            Set(Vehicle, v, "docked", true);
            Set(VehicleDockingBay, bay, "vehicle_docked_param", true);
            return true;
        }

        public static void UndockRemote(GameObject vehicle)
        {
            var v = Vehicle != null ? vehicle.GetComponent(Vehicle) : null;
            if (v == null) return;
            if (VehicleDockingBay != null)
                foreach (var bay in SceneIndex.All(VehicleDockingBay))
                {
                    if (!ReferenceEquals(TryGet(VehicleDockingBay, bay, "dockedVehicle"), v)) continue;
                    var sub = Call(VehicleDockingBay, bay, "GetSubRoot") as Component;
                    if (IsCyclops(sub)) TryDo("undockcyc", () => Call(VehicleDockingBay, bay, "SetVehicleUndocked"));
                    Set(VehicleDockingBay, bay, "dockedVehicle", null);
                    Set(VehicleDockingBay, bay, "vehicle_docked_param", false);
                }
            Set(Vehicle, v, "docked", false);
            vehicle.transform.SetParent(null, true);
            Register(vehicle);
        }

        // ---------- Cyclops controls ----------

        public static CyclopsStatePacket ReadCyclops(GameObject cyclops, string id)
        {
            var state = new CyclopsStatePacket { Id = id };
            var lights = CyclopsLightingPanel != null ? cyclops.GetComponentInChildren(CyclopsLightingPanel, true) : null;
            if (lights != null)
            {
                state.InternalLights = TryGet(CyclopsLightingPanel, lights, "lightingOn") is bool a && a;
                state.FloodLights = TryGet(CyclopsLightingPanel, lights, "floodlightsOn") is bool b && b;
            }
            var silent = CyclopsSilentRunningAbilityButton != null ? cyclops.GetComponentInChildren(CyclopsSilentRunningAbilityButton, true) : null;
            state.SilentRunning = silent != null && TryGet(CyclopsSilentRunningAbilityButton, silent, "active") is bool c && c;
            var control = SubControl != null ? cyclops.GetComponent(SubControl) : null;
            var motor = control != null ? TryGet(SubControl, control, "cyclopsMotorMode") : null;
            var mode = motor != null ? TryGet(motor.GetType(), motor, "cyclopsMotorMode") : null;
            state.MotorMode = mode != null ? Convert.ToInt32(mode) : -1;
            return state;
        }

        public static void ApplyCyclops(GameObject cyclops, CyclopsStatePacket s)
        {
            var now = ReadCyclops(cyclops, s.Id);
            var lights = CyclopsLightingPanel != null ? cyclops.GetComponentInChildren(CyclopsLightingPanel, true) : null;
            if (lights != null && now.InternalLights != s.InternalLights) TryDo("cyclights", () =>
            {
                Set(CyclopsLightingPanel, lights, "lightingOn", s.InternalLights);
                var root = TryGet(CyclopsLightingPanel, lights, "cyclopsRoot");
                if (root != null) Call(root.GetType(), root, "ForceLightingState", s.InternalLights);
                Call(CyclopsLightingPanel, lights, "UpdateLightingButtons");
            });
            if (lights != null && now.FloodLights != s.FloodLights) TryDo("cycflood", () =>
            {
                Set(CyclopsLightingPanel, lights, "floodlightsOn", s.FloodLights);
                Call(CyclopsLightingPanel, lights, "SetExternalLighting", s.FloodLights);
                Call(CyclopsLightingPanel, lights, "UpdateLightingButtons");
            });
            var silent = CyclopsSilentRunningAbilityButton != null ? cyclops.GetComponentInChildren(CyclopsSilentRunningAbilityButton, true) : null;
            if (silent != null && now.SilentRunning != s.SilentRunning) TryDo("cycsilent", () =>
            {
                Set(CyclopsSilentRunningAbilityButton, silent, "active", s.SilentRunning);
                cyclops.BroadcastMessage(s.SilentRunning ? "RigForSilentRunning" : "SecureFromSilentRunning", SendMessageOptions.DontRequireReceiver);
            });
            if (s.MotorMode >= 0 && now.MotorMode != s.MotorMode && CyclopsMotorModeButton != null) TryDo("cycmotor", () =>
            {
                var set = FindMethod(CyclopsMotorModeButton, "SetCyclopsMotorMode");
                if (set == null) return;
                var mode = Enum.ToObject(set.GetParameters()[0].ParameterType, s.MotorMode);
                foreach (var b in cyclops.GetComponentsInChildren(CyclopsMotorModeButton, true)) set.Invoke(b, new[] { mode });
            });
        }

        // ---------- vitals / hands ----------

        public static (byte health, byte food, byte water) Vitals(Component player)
        {
            byte Pct(object v, float max) => v == null ? (byte)0 : (byte)Mathf.Clamp(Mathf.RoundToInt(Convert.ToSingle(v) / max * 100f), 0, 100);
            var live = TryGet(Player, player, "liveMixin");
            var health = live != null ? TryGet(live.GetType(), live, "health") : null;
            var max = live != null ? TryGet(live.GetType(), live, "maxHealth") : null;
            var survival = Survival != null ? player.GetComponent(Survival) : null;
            return (Pct(health, max != null ? Math.Max(1f, Convert.ToSingle(max)) : 100f),
                    survival != null ? Pct(TryGet(Survival, survival, "food"), 100f) : (byte)0,
                    survival != null ? Pct(TryGet(Survival, survival, "water"), 100f) : (byte)0);
        }

        // TechType of whatever the local player is holding, or "".
        public static string HeldTech()
        {
            var inv = TryGet(Inventory, null, "main");
            var tool = inv != null ? TryGet(Inventory, inv, "GetHeldTool") as Component : null;
            return tool != null ? TechTypeOf(tool.gameObject) ?? "" : "";
        }

        public static IEnumerator LoadPrefab(string techName, Action<GameObject> done)
        {
            var tt = ParseTechType(techName);
            var task = tt != null ? Call(CraftData, null, "GetPrefabForTechTypeAsync", tt) : null;
            if (task is IEnumerator e) yield return e;
            done(task == null ? null : TryGet(task.GetType(), task, "GetResult") as GameObject);
        }

        static Type SceneCleanerPreserve;

        // Subnautica's SceneCleaner deletes every leftover object on the way to the main menu
        // unless it carries this tag (Nitrox does the same). Use with DontDestroyOnLoad.
        public static void KeepAlive(GameObject go)
        {
            UnityEngine.Object.DontDestroyOnLoad(go);
            if (SceneCleanerPreserve != null && go.GetComponent(SceneCleanerPreserve) == null)
                go.AddComponent(SceneCleanerPreserve);
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
            var key = (type, name);
            if (!Getters.TryGetValue(key, out var getter))
            {
                getter = MakeGetter(type, name);
                Getters[key] = getter;
                if (getter == null) WarnOnce("get:" + type.FullName + "." + name, "Game member not found: " + type.FullName + "." + name);
            }
            return getter?.Invoke(target);
        }

        const BindingFlags AnyMember = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        // Like AccessTools.Field/Property but without a warning in the log every time a guess misses.
        static FieldInfo QuietField(Type type, string name)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                var f = t.GetField(name, AnyMember);
                if (f != null) return f;
            }
            return null;
        }

        static PropertyInfo QuietProperty(Type type, string name)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                var p = t.GetProperties(AnyMember).FirstOrDefault(x => x.Name == name && x.GetIndexParameters().Length == 0);
                if (p != null) return p;
            }
            return null;
        }

        static Func<object, object> MakeGetter(Type type, string name)
        {
            var f = QuietField(type, name);
            if (f != null) return o => f.GetValue(o);
            var p = QuietProperty(type, name);
            if (p != null && p.GetGetMethod(true) != null) return o => p.GetValue(o, null);
            MethodInfo m = null;
            for (var t = type; t != null && m == null; t = t.BaseType)
                m = t.GetMethods(AnyMember).FirstOrDefault(x => x.Name == name && x.GetParameters().Length == 0);
            if (m != null) return o => m.Invoke(o, null);
            return null;
        }

        // Like Get, but quiet when the member doesn't exist (for guessing between a few possible names).
        internal static object TryGet(Type type, object target, string name)
        {
            if (type == null) return null;
            var key = (type, name);
            if (!Getters.TryGetValue(key, out var getter)) Getters[key] = getter = MakeGetter(type, name);
            return getter?.Invoke(target);
        }

        internal static bool Set(Type type, object target, string name, object value)
        {
            if (type == null) return false;
            if (!Setters.TryGetValue((type, name), out var member))
            {
                member = QuietField(type, name);
                if (member == null && QuietProperty(type, name) is PropertyInfo prop && prop.GetSetMethod(true) != null) member = prop;
                Setters[(type, name)] = member;
            }
            if (member is FieldInfo f) { f.SetValue(target, Coerce(value, f.FieldType)); return true; }
            if (member is PropertyInfo p) { p.SetValue(target, Coerce(value, p.PropertyType), null); return true; }
            return false;
        }

        static object Coerce(object value, Type type)
        {
            if (value == null || type.IsInstanceOfType(value)) return value;
            if (type.IsEnum) return Enum.ToObject(type, value);
            return Convert.ChangeType(value, type);
        }

        // Finds a method by name whose first parameters accept `leading`.
        // remembered per (type, name, argument types): looking methods up used to make garbage every call
        static readonly Dictionary<(Type, string, Type, Type, int), MethodInfo> Methods = new Dictionary<(Type, string, Type, Type, int), MethodInfo>();

        internal static MethodInfo FindMethod(Type type, string name, params Type[] leading)
        {
            if (type == null) return null;
            var key = (type, name, leading.Length > 0 ? leading[0] : null, leading.Length > 1 ? leading[1] : null, leading.Length);
            if (leading.Length <= 2 && Methods.TryGetValue(key, out var cached)) return cached;
            var found = FindMethodUncached(type, name, leading);
            if (leading.Length <= 2) Methods[key] = found;
            return found;
        }

        static MethodInfo FindMethodUncached(Type type, string name, Type[] leading)
        {
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
            var types = new Type[leading.Length];
            for (int i = 0; i < leading.Length; i++) types[i] = leading[i]?.GetType() ?? typeof(object);
            var m = FindMethod(type, name, types);
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
                var mgr = TryGet(SaveLoadManager, null, "main");
                var slot = mgr != null ? TryGet(SaveLoadManager, mgr, "GetCurrentSlot") as string : null;
                return string.IsNullOrEmpty(slot) ? "default" : slot;
            }
            catch { return "default"; }
        }

        // ---------- main menu: new game / load save ----------

        public static Component MainMenu
        {
            get
            {
                var menu = As<Component>(TryGet(MainMenuType, null, "main"));
                if (menu == null && MainMenuType != null) menu = UnityEngine.Object.FindObjectOfType(MainMenuType) as Component;
                return menu != null ? menu : null;
            }
        }

        public static bool StartNewGame(string mode, MonoBehaviour runner)
        {
            var menu = MainMenu;
            if (menu == null || GameModeEnum == null) return false;
            object gm;
            try { gm = Enum.Parse(GameModeEnum, mode); }
            catch { gm = Enum.ToObject(GameModeEnum, 0); } // Survival; the real mode gets set after loading
            if (!(Call(MainMenuType, menu, "StartNewGame", gm) is IEnumerator e)) return false;
            runner.StartCoroutine(e);
            return true;
        }

        public static string CurrentGameMode()
        {
            var current = TryGet(GameModeUtils, null, "currentGameMode");
            if (current == null) return GameModes.Survival;
            int value = Convert.ToInt32(current);
            foreach (var m in GameModes.All)
                if (GameModes.OptionValue(m) == value) return m;
            return GameModes.Survival;
        }

        public static void SetGameMode(string mode)
        {
            if (GameModeUtils == null || GameModeOption == null) return;
            Call(GameModeUtils, null, "SetGameMode", Enum.ToObject(GameModeOption, GameModes.OptionValue(mode)));
        }

        static object SaveInfo(string slot)
        {
            var mgr = TryGet(SaveLoadManager, null, "main");
            return mgr == null ? null : Call(SaveLoadManager, mgr, "GetGameInfo", slot);
        }

        public static bool SaveExists(string slot) => !string.IsNullOrEmpty(slot) && SaveInfo(slot) != null;

        // Loads a save slot as if you clicked it in the Load menu.
        public static bool TryLoadSlot(string slot, MonoBehaviour runner)
        {
            var menu = MainMenu;
            if (menu == null) return false;

            if (MainMenuLoadButton != null)
            {
                foreach (var button in Resources.FindObjectsOfTypeAll(MainMenuLoadButton))
                {
                    if (TryGet(MainMenuLoadButton, button, "saveGame") as string != slot) continue;
                    if (FindMethod(MainMenuLoadButton, "Load") == null) break;
                    Call(MainMenuLoadButton, button, "Load");
                    return true;
                }
            }

            // No button to click: call the menu's loader ourselves with the save's details.
            var info = SaveInfo(slot);
            var load = FindMethod(MainMenuType, "LoadGameAsync", typeof(string));
            if (info == null || load == null) return false;
            var ps = load.GetParameters();
            var args = new object[ps.Length];
            args[0] = slot;
            for (int i = 1; i < ps.Length; i++)
            {
                var name = ps[i].Name;
                var value = TryGet(info.GetType(), info, name)
                            ?? (name.EndsWith("Id") ? TryGet(info.GetType(), info, name.Substring(0, name.Length - 2)) : null);
                var type = ps[i].ParameterType;
                if (value != null && !type.IsInstanceOfType(value))
                {
                    try { value = type.IsEnum ? Enum.ToObject(type, value) : Convert.ChangeType(value, type); }
                    catch { value = null; }
                }
                args[i] = value ?? (ps[i].IsOptional && ps[i].DefaultValue != DBNull.Value ? ps[i].DefaultValue
                    : type.IsValueType ? Activator.CreateInstance(type) : null);
            }
            if (!(load.Invoke(menu, args) is IEnumerator e)) return false;
            runner.StartCoroutine(e);
            return true;
        }

        // ---------- the game's own save serializer ----------
        // Bases, lockers etc. are sent as exactly what the game would write into your save file.

        public static byte[] Serialize(GameObject go)
        {
#pragma warning disable 618
            var serializer = Activator.CreateInstance(ProtobufSerializer, true);
#pragma warning restore 618
            // the game only has an async saver; it never waits on anything, so run it to the end right now
            var m = FindMethod(ProtobufSerializer, "SerializeObjectTreeAsync", typeof(System.IO.Stream), typeof(GameObject))
                    ?? throw new MissingMethodException("ProtobufSerializer", "SerializeObjectTreeAsync");
            using (var ms = new System.IO.MemoryStream())
            {
                var args = new object[m.GetParameters().Length];
                args[0] = ms;
                args[1] = go;
                for (int i = 2; i < args.Length; i++) args[i] = false; // beforeDestroy
                RunToEnd((IEnumerator)m.Invoke(serializer, args));
                return ms.ToArray();
            }
        }

        // Steps a coroutine (and anything it yields) to completion without waiting for frames.
        static void RunToEnd(IEnumerator routine)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(routine);
            int guard = 0;
            while (stack.Count > 0)
            {
                if (++guard > 5_000_000) throw new InvalidOperationException("serializer never finished");
                var top = stack.Peek();
                if (!top.MoveNext()) { stack.Pop(); continue; }
                if (top.Current is IEnumerator nested) stack.Push(nested);
            }
        }

        public static IEnumerator Deserialize(byte[] data, Action<GameObject> done)
        {
#pragma warning disable 618
            var serializer = Activator.CreateInstance(ProtobufSerializer, true);
#pragma warning restore 618
            var stream = new System.IO.MemoryStream(data);
            // public CoroutineTask<GameObject> DeserializeObjectTreeAsync(Stream, bool forceInactiveRoot, bool allowSpawnRestrictions, int verbose)
            MethodInfo load = null;
            foreach (var mi in AccessTools.GetDeclaredMethods(ProtobufSerializer))
            {
                if (mi.Name != "DeserializeObjectTreeAsync" || !mi.IsPublic) continue;
                var ps = mi.GetParameters();
                if (ps.Length == 4 && ps[0].ParameterType == typeof(System.IO.Stream)) { load = mi; break; }
            }
            if (load == null) throw new MissingMethodException("ProtobufSerializer", "DeserializeObjectTreeAsync");
            var task = load.Invoke(serializer, new object[] { stream, false, false, 0 });
            if (task is IEnumerator e) yield return e;
            done(task == null ? null : TryGet(task.GetType(), task, "GetResult") as GameObject);
        }

        public static void Register(GameObject go) => TryDo("register", () => Call(LargeWorldEntity, null, "Register", go));

        // ---------- building ----------

        // The thing to send when a piece changes: its whole base, or the object itself if it stands alone.
        public static GameObject StructureRoot(GameObject go)
        {
            if (go == null) return null;
            if (Base != null && go.GetComponentInParent(Base) is Component b) return b.gameObject;
            return go;
        }

        public static List<GameObject> FindBases()
        {
            var list = new List<GameObject>();
            if (Base != null)
                foreach (var c in SceneIndex.All(Base)) list.Add(c.gameObject);
            return list;
        }

        // ---------- storage ----------

        // The ItemsContainer behind a locker / storage object (found via any component's 'container' field).
        public static object ContainerOf(GameObject go)
        {
            for (var t = go != null ? go.transform : null; t != null; t = t.parent)
            {
                foreach (var c in t.GetComponents<Component>())
                {
                    if (c == null) continue;
                    var value = TryGet(c.GetType(), c, "container");
                    if (value != null && value.GetType().Name == "ItemsContainer") return value;
                }
            }
            return null;
        }

        public static Transform ContainerRoot(object container) => TryGet(ItemsContainer, container, "tr") as Transform;

        // Where to look for the container by id: its root, or the object holding it.
        public static string ContainerId(object container)
        {
            var tr = ContainerRoot(container);
            if (tr == null) return null;
            return GetId(tr.gameObject) ?? (tr.parent != null ? GetId(tr.parent.gameObject) : null);
        }

        public static List<Component> ContainerItems(object container)
        {
            var list = new List<Component>();
            if (container is IEnumerable items)
                foreach (var inv in items)
                    if (inv != null && Get(inv.GetType(), inv, "item") is Component p && p != null) list.Add(p);
            return list;
        }

        public static void EmptyContainer(object container)
        {
            foreach (var pickupable in ContainerItems(container))
            {
                Call(ItemsContainer, container, "RemoveItem", pickupable, true);
                UnityEngine.Object.Destroy(pickupable.gameObject);
            }
        }

        public static bool AddToContainer(object container, GameObject item)
        {
            var pickupable = Pickupable != null ? item.GetComponent(Pickupable) : null;
            if (pickupable == null || InventoryItem == null) return false;
            var inv = Activator.CreateInstance(InventoryItem, pickupable);
            Call(ItemsContainer, container, "UnsafeAdd", inv);
            item.SetActive(false); // stored items are inactive
            return true;
        }

        // ---------- PDA ----------

        public static void AddPdaLog(string key) => Call(PDALog, null, "Add", key);

        // Fragment scan progress: techType name -> how many scanned.
        public static Dictionary<string, int> FragmentProgress()
        {
            var map = new Dictionary<string, int>();
            if (TryGet(PDAScanner, null, "partial") is IEnumerable partial)
                foreach (var entry in partial)
                {
                    if (entry == null) continue;
                    var tech = TryGet(entry.GetType(), entry, "techType")?.ToString();
                    if (tech != null && TryGet(entry.GetType(), entry, "unlocked") is int n) map[tech] = n;
                }
            return map;
        }

        public static void SetFragmentProgress(string techName, int unlocked)
        {
            var tt = ParseTechType(techName);
            if (tt == null || PDAScanner == null) return;
            if (TryGet(PDAScanner, null, "partial") is IEnumerable partial)
                foreach (var entry in partial)
                {
                    if (entry == null || !Equals(TryGet(entry.GetType(), entry, "techType"), tt)) continue;
                    if (TryGet(entry.GetType(), entry, "unlocked") is int have && have < unlocked)
                        Set(entry.GetType(), entry, "unlocked", unlocked);
                    return;
                }
            Call(PDAScanner, null, "Add", tt, unlocked);
        }

        // ---------- HUD markers ----------

        // Shows a beacon-style marker with a name and distance on everyone's HUD.
        public static void AddPing(GameObject target, string label)
        {
            if (PingInstance == null) return;
            var ping = target.AddComponent(PingInstance);
            if (PingType != null)
            {
                try { Set(PingInstance, ping, "pingType", Enum.Parse(PingType, "Signal")); } catch { }
            }
            Set(PingInstance, ping, "origin", target.transform);
            Set(PingInstance, ping, "minDist", 5f);
            Set(PingInstance, ping, "_label", label);
            Set(PingInstance, ping, "displayPingInManager", false);
            Set(PingInstance, ping, "visible", true);
            TryDo("ping", () => Call(PingInstance, ping, "Initialize"));
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

        // Gives go this id. If another object still holds it (usually an old copy the game is about to unload),
        // that one gets a throwaway id first, so the game doesn't log "Overwriting id" / "Unregistering failed" errors.
        public static void TakeId(GameObject go, string id)
        {
            var other = FindById(id);
            if (other != null && other != go)
            {
                var uid = other.GetComponent(UniqueIdentifier);
                if (uid != null) Set(UniqueIdentifier, uid, "Id", id + "~" + Guid.NewGuid().ToString("N").Substring(0, 8));
            }
            SetId(go, id);
        }

        // Another live copy of the same thing already holds this id, right there: `go` is a duplicate spawn.
        public static bool IsDuplicateOf(GameObject go, string id)
        {
            var other = FindById(id);
            return other != null && other != go && other.activeInHierarchy &&
                   (other.transform.position - go.transform.position).sqrMagnitude < 4f;
        }

        // The game's own "mouse is captured for looking around" switch (UWE.Utils.lockCursor, in the firstpass dll).
        static Type _uweUtils;
        static bool _uweLooked;
        static Type UweUtils
        {
            get
            {
                if (_uweLooked) return _uweUtils;
                _uweLooked = true;
                _uweUtils = Type.GetType("UWE.Utils, Assembly-CSharp-firstpass");
                if (_uweUtils == null)
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        _uweUtils = asm.GetType("UWE.Utils", false);
                        if (_uweUtils != null) break;
                    }
                if (_uweUtils == null) WarnOnce("uweutils", "Couldn't find the game's cursor switch; the emote wheel may not free the mouse");
                return _uweUtils;
            }
        }

        public static void SetLockCursor(bool locked)
        {
            if (UweUtils != null) TryDo("lock cursor", () => Set(UweUtils, null, "lockCursor", locked));
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        public static bool LockCursor => UweUtils != null && Get(UweUtils, null, "lockCursor") is bool b ? b : Cursor.lockState == CursorLockMode.Locked;

        // Turns the game's "click to grab the mouse" handler on/off (off while our emote wheel is up).
        static bool _avatarInputOff;
        public static void SetAvatarInput(bool on)
        {
            if (on != _avatarInputOff) return; // already that way
            if (AvatarInputHandler == null) return;
            if (Get(AvatarInputHandler, null, "main") is Behaviour b && b != null) b.enabled = on;
            _avatarInputOff = !on;
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
                foreach (var c in SceneIndex.All(Vehicle))
                    list.Add(c.gameObject);
            if (SubRoot != null)
                foreach (var c in SceneIndex.All(SubRoot))
                    if (IsCyclops(c)) list.Add(c.gameObject);
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
            SetId(go, id);
            done(go);
        }

        internal static void TryDo(string what, Action a)
        {
            try { a(); }
            catch (Exception ex) { WarnOnce("try:" + what, $"{what} failed: {ex.GetBaseException().Message}"); }
        }
    }
}
