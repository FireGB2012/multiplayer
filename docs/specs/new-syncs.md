# New syncs: design (approach A)

Status: waiting for review. Nothing is built yet.

## Goal

Everyone in a world sees the same:
- vehicle upgrades, names and colors
- signs, locker labels and beacon names
- lights on divers and vehicles, plus the main tool effects
- base lights and scanner room settings

Each of these is currently local to each PC.

## Pieces

### 1. Settings channel (shared base for most of this)

**New packet:** `ObjectSettingPacket { string Id; SettingKind Kind; byte[] Data; int WriterId }`.

**Server:**
- Keeps the latest value for each (Id, Kind) in the world save (`WorldState.Settings`, file version 12).
- Relays each update to everyone else and sends the whole table to anyone who joins.
- Drops it when the object is removed, using the existing `EntityRemoved` and base-deconstruct paths.

**Plugin (`SettingsSync.cs`):**
- One class with a table of kinds. Each kind has a *read* (game object → bytes), an *apply* (bytes → game object) and the hook that says it changed.
- If the object isn't loaded yet, the value waits in a pending list. The existing streaming check (the same one bases use) applies it when the object loads.
- While applying a remote value, `ApplyingRemote` is set, so the change isn't echoed back.

**Kinds:**

| Kind | Game class / hook | Data |
|---|---|---|
| SignText | `Sign`: postfix `uGUI_SignInput.OnDeselect` | text, scaleIndex, colorIndex, elements[], backgroundEnabled |
| LabelText | `ColoredLabel`: same deselect hook | text, colorIndex |
| BeaconName | `BeaconLabel.SetLabel` | label |
| VehicleName | `SubName.SetName` (Seamoth, Prawn, Cyclops) | name |
| VehicleColors | `SubName.SetColor` (debounced 0.5 s while dragging the slider) | HSB per color slot |
| VehicleLights | `ToggleLights.SetLightsActive` on vehicles | bool |
| BaseLights | `SubRoot.ForceLightingState` (base light switch) | bool |
| ScanTarget | `MapRoomFunctionality.StartScanning` | TechType name |

Scanner room *upgrades* already sit in a storage container, which locker sync already covers. I'll check that in testing, not rebuild it.

**Conflicts:** last write wins. That's fine for text, colors and switches.

### 2. Upgrade slots (Equipment)

Vehicle module slots, Cyclops upgrade consoles and Prawn arms all use the game's `Equipment` class, not `ItemsContainer`. That's why locker sync misses them.

- **Hooks:** `Equipment.AddItem` (postfix, on success) and `Equipment.RemoveItem(string slot, ...)`.
- **Messages:** reuse `ContainerPacket` with the container id = owner object id + `"#equip"`, and items listed as `slot=TechType`. The server already stores container contents per id.
- **Applying a remote change:** add or remove the item in that slot with `forced` set, under `ApplyingRemote`. The game's own events then fire, so depth limits, arm models and torpedo bays update by themselves.
- **No duplicates:** the module you hand over leaves your inventory as normal. Other PCs spawn their own copy only inside the slot, the same way lockers work now.

### 3. Lights and tool effects on divers

**Steady state:** goes in `PlayerStatePacket.Flags` (2 new bits, no new packet):
- `LightOn`: flashlight / seaglide / any held tool's `ToggleLights` is on → the remote diver's held-item copy gets its light switched on.

**One-off effects:** new `ToolFxPacket { int PlayerId; FxKind Kind; Vec3 Pos; Vec3 Dir; string Extra }`. The server just relays it and stores nothing.

| Fx | Trigger | What other PCs do |
|---|---|---|
| Torpedo | `Vehicle.TorpedoShot` | spawn the torpedo, visual only (damage is already decided by whoever owns the creature) |
| Stasis | stasis rifle shot | spawn a `StasisSphere` at that spot (creatures freeze on every PC) |
| Drill | Prawn drill hitting a `Drillable` | sparks plus deposit health (chunks come through the existing drop sync) |
| Grapple | grapple hook fired / released | show the hook and line from that Prawn |
| Propulsion | cannon grab / shoot | the grabbed item already moves through item sync. Only the beam visual is new (**cut if it gets fiddly**) |

### 4. Protocol and compatibility

- Protocol version 17 → 18. Old versions get the usual "update the mod" message.
- World file version 12 still loads version-11 saves (an empty settings table).

## Failure handling

- Every hook goes through the existing `Patches.Hook`. If a game method is missing, that one feature turns itself off with a log line and everything else keeps working. The "Synced features:" log line lists what's on.
- Every apply is wrapped in `Game.TryDo`, so one bad value can't break the rest.

## Testing

**Server unit tests:**
- Settings are stored, replayed to a late joiner, survive save/load and get removed with their object.
- Equipment slot add/remove round-trips.
- ToolFx is relayed to everyone but never to the sender.

**Name checker:** run against the decompiled game for every new hook and member.

**In-game checklist for you (2 PCs):**
- name and paint a Seamoth
- put in a depth module
- write on a sign
- name a beacon
- switch the base lights off
- set the scanner room to scan for something
- shoot a torpedo and the stasis rifle
- drill an ore deposit

## Build order

1. Settings channel + signs, labels, beacons, vehicle names and colors (one release)
2. Upgrade slots (one release)
3. Lights and tool effects (one release)

Each step: build, tests, name check, push, release.

## Out of scope (separate specs later)

- Mod compatibility
- Launcher redesign
