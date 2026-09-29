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
            ItemsContainer, InventoryItem, PDALog, PDAScanner, PingInstance, PingType, Openable, Inventory, Survival;

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

        // Like Get, but quiet when the member doesn't exist (for guessing between a few possible names).
        internal static object TryGet(Type type, object target, string name)
        {
            if (type == null) return null;
            var key = type.FullName + "." + name;
            if (!Getters.TryGetValue(key, out var getter)) Getters[key] = getter = MakeGetter(type, name);
            return getter?.Invoke(target);
        }

        internal static bool Set(Type type, object target, string name, object value)
        {
            if (type == null) return false;
            var f = AccessTools.Field(type, name);
            if (f != null) { f.SetValue(target, Coerce(value, f.FieldType)); return true; }
            var p = AccessTools.Property(type, name);
            if (p != null && p.GetSetMethod(true) != null) { p.SetValue(target, Coerce(value, p.PropertyType), null); return true; }
            return false;
        }

        static object Coerce(object value, Type type)
        {
            if (value == null || type.IsInstanceOfType(value)) return value;
            if (type.IsEnum) return Enum.ToObject(type, value);
            return Convert.ChangeType(value, type);
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
            var serializer = Activator.CreateInstance(ProtobufSerializer, true);
            var m = FindMethod(ProtobufSerializer, "SerializeObjectTree", typeof(System.IO.Stream), typeof(GameObject))
                    ?? throw new MissingMethodException("ProtobufSerializer", "SerializeObjectTree");
            using (var ms = new System.IO.MemoryStream())
            {
                m.Invoke(serializer, new object[] { ms, go });
                return ms.ToArray();
            }
        }

        public static IEnumerator Deserialize(byte[] data, Action<GameObject> done)
        {
            var serializer = Activator.CreateInstance(ProtobufSerializer, true);
            var stream = new System.IO.MemoryStream(data);
            var async = FindMethod(ProtobufSerializer, "DeserializeObjectTreeAsync", typeof(System.IO.Stream));
            if (async != null)
            {
                object result = TaskResultOfT != null ? Activator.CreateInstance(TaskResultOfT.MakeGenericType(typeof(GameObject))) : null;
                var ps = async.GetParameters();
                var args = new object[ps.Length];
                args[0] = stream;
                for (int i = 1; i < ps.Length; i++)
                {
                    var t = ps[i].ParameterType;
                    if (result != null && t.IsInstanceOfType(result)) args[i] = result;
                    else args[i] = t.IsValueType ? Activator.CreateInstance(t) : null; // false / 0
                }
                if (async.Invoke(serializer, args) is IEnumerator e) yield return e;
                done(result == null ? null : Get(result.GetType(), result, "Get") as GameObject);
                yield break;
            }

            var sync = FindMethod(ProtobufSerializer, "DeserializeObjectTree", typeof(System.IO.Stream));
            if (sync == null) throw new MissingMethodException("ProtobufSerializer", "DeserializeObjectTree");
            var sp = sync.GetParameters();
            var sargs = new object[sp.Length];
            sargs[0] = stream;
            for (int i = 1; i < sp.Length; i++) sargs[i] = sp[i].ParameterType.IsValueType ? Activator.CreateInstance(sp[i].ParameterType) : null;
            done(sync.Invoke(serializer, sargs) as GameObject);
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
                foreach (var o in UnityEngine.Object.FindObjectsOfType(Base)) list.Add(((Component)o).gameObject);
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

        internal static void TryDo(string what, Action a)
        {
            try { a(); }
            catch (Exception ex) { WarnOnce("try:" + what, $"{what} failed: {ex.GetBaseException().Message}"); }
        }
    }
}
