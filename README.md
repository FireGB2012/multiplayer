# Subnautica Multiplayer

Co-op mod for Subnautica (the original, BepInEx 5) with a launcher app.

## What syncs
| Thing | Status |
|---|---|
| Party lobby in the main menu: pick your name + suit color, see who's in, host clicks Start | ✔ |
| Suit colors | ✔ everyone sees your diver in your color |
| Game mode picked in the launcher (Survival / Hardcore / Creative / Freedom) | ✔ |
| Players (position, facing, name tags) | ✔ |
| Chat | ✔ |
| Blueprints (unlocks, fragment scans) | ✔ everyone shares one tech tree |
| Databank / PDA entries | ✔ |
| Picked-up items + broken outcrops | ✔ gone for everyone |
| Time of day | ✔ server clock |
| Seamoth / Prawn suit / Cyclops | ✔ show up for everyone, whoever drives it moves it |
| Vehicle destroyed | ✔ |
| World saves on the server | ✔ keeps all of the above between sessions |
| Base building (build + deconstruct, furniture inside) | ✔ the builder sends the whole base, saved with the game's own save format |
| Lockers / storage (lifepod, Cyclops, base lockers, planters...) | ✔ full contents re-sent on every change |
| PDA data logs + fragment scan progress | ✔ |
| Teammates on your HUD (beacon-style marker with name + distance) | ✔ |
| Real diver models with swim animations | ✔ |
| Teammate health / food / water under their name | ✔ |
| Item in a teammate's hand | ✔ |
| Dropped / placed items (handing stuff over, beacons) | ✔ |
| Doors and hatches opening / closing | ✔ |
| Deaths: chat message + death beacon | ✔ |
| Vehicle health + battery / Cyclops power | ✔ from whoever drives it |
| Vehicle upgrades, power cells, colors, name, storage | ✔ whole vehicle re-sent when the driver gets out (not Cyclops yet) |
| Docking in moonpool / Cyclops bay | ✔ |
| Cyclops lights, floodlights, silent running, engine speed mode | ✔ |
| Teammates walking inside a moving Cyclops | ✔ positioned relative to the Cyclops |
| Story events (radio messages, Sunbeam, Precursor progress, story PDA) | ✔ every story goal fires for everyone, late joiners catch up |
| Aurora explosion | ✔ one shared timing, so it blows up for everyone at once |
| Your own inventory | ✖ each player keeps their own (like Nitrox) |
| Creatures / fauna AI | ✔ same spawns for everyone, one player runs each creature, attacks hit whoever they chase (new worlds / unexplored areas) |
| Plants / resources in entity slots | ✔ same spawns + same ids, so picking them up syncs |
| Alien containment: fish/eggs you put in, babies and hatchlings | ✔ the host's game breeds, everyone gets the baby |
| Cuddlefish follower, crabsquid EMP | ✖ not yet |
| Player models | ✔ real diver suit, swim animation, held tool |
| Suits: radiation suit, reinforced suit, stillsuit, fins, rebreather, tanks | ✔ you see what they wear |
| Tool animations (knife swing, scanner, builder, welder, PDA...) | ✔ |
| Beds | ✔ the night is skipped only when everyone is in bed together |
| Base power (solar, thermal, bioreactor, nuclear) | ✔ one player runs each generator, everyone's usage comes off the same power |
| Planters | ✔ via locker sync, growth progress kept; fruit picking syncs (wild plants too) |
| Fabricator / crafting animation | ✔ others see it build; only the crafter gets the item |
| Cyclops fires + extinguishing | ✔ the driver's game (or the host) starts fires, everyone sees and can put them out |
| Base / Cyclops leaks and welding | ✔ damage and repairs are shared |
| Build hologram while placing something | ✔ teammates see your green/red ghost |
| Server password, kick, ban | ✔ in the launcher, the in-game F8 window (host) and the dedicated server |
| In-game menus | ✔ made from copies of the game's own menus: the Multiplayer screen is the real Load/New game panels with save-slot rows, in game it's a page in the pause menu (Enter = chat, F8 = open it), chat uses the game's message feed |
| Loading screen when joining | ✔ other players' bases, lockers, items, vehicles and diver models load behind it; your oxygen/food don't drain meanwhile |
| Lag fixes | ✔ network sends never freeze the game, big data is compressed, no more whole-world searches every second |

## In-game Multiplayer menu
Like Nitrox: the main menu gets a **Multiplayer** button next to Play. It lists **your worlds** and **servers you've played on**
(saved with their join codes), lets you **host a world** (name + game mode) straight from the game, and **add a server** by code.
Multiplayer saves are kept out of the normal single-player Load list. The launcher is optional.

## Quick start (players)
1. Download `SubnauticaMP-Launcher.exe` and run it.
2. **Setup tab**: check it found your Subnautica folder. If BepInEx is missing, hit **Install BepInEx for me**
   (or install BepInEx 5 x64 yourself). Start the game once after installing BepInEx, then close it.
3. **Play tab**: hit **PLAY**. The game opens on its main menu with the mod installed.
4. Click **Multiplayer** in the game's main menu:
   - **Host a world**: world name, password (optional), then click a game mode.
   - **Add a server**: your friend's join code (like `KQ7MX-3HD2P`) or IP, a name, and the password if they set one.
   - Server running in the launcher (**Server** tab)? It shows up as **Launcher server**: click it to join.
   - **New world**: everyone meets in the **party lobby**: type your name, click to change your suit color,
     and the host clicks **Start the game**. **Existing world**: it loads your save for that world.
5. In game, **Esc > Multiplayer** or **F8** (or the Multiplayer button in the pause menu) opens the multiplayer page: chat, join code, players, kick/ban if you host, leave. **Enter** jumps straight to chat.

The host is whoever plays on the server's PC (otherwise whoever joined first).
Each player's save for a world is remembered in `BepInEx\plugins\SubnauticaMP\worlds.txt`.

## Friends on a different wifi
The host's launcher asks the router to open the port automatically (UPnP). The Server tab tells you if it worked.
If it didn't:
- turn on UPnP in the router settings, or
- forward **TCP 11000** to the host PC by hand, or
- everyone installs **Radmin VPN** or **Tailscale** and joins with the host's VPN IP (works even when the ISP uses CGNAT).

## If something breaks
Send `Subnautica\BepInEx\LogOutput.log`. The mod logs which features hooked in
(`Synced features: ...`) and anything it couldn't find in the game (`Game member not found: ...`).

## Dedicated server
```
dotnet run --project src/SubnauticaMP.Server -- 11000 world.dat --password secret
```
Commands: `players`, `kick <name>`, `ban <name>`, `unban <name>`, `bans`, `save`, `quit`.

## Building
Needs the .NET 8 SDK.
```
dotnet test                                                   # 37 tests: networking, world sync, lobby, UPnP, launcher UI
dotnet publish src/SubnauticaMP.Launcher -c Release -r win-x64   # -> SubnauticaMP-Launcher.exe (mod packed inside)
dotnet build src/SubnauticaMP.Plugin -c Release -p:GameDir="C:\...\Subnautica"   # mod only, copies into the game
```
The mod finds Subnautica's code by name at runtime, so it builds without the game.
Without `GameDir` it compiles against `lib/BepInEx.Ref` (a compile-only copy of the BepInEx API) and public Unity/HarmonyX packages.

## Layout
| Folder | What |
|---|---|
| `src/SubnauticaMP.Shared` | Protocol, server (world state + saving), client, UPnP, join codes |
| `src/SubnauticaMP.Plugin` | The BepInEx mod: sync systems, Harmony hooks, F8 window |
| `src/SubnauticaMP.Launcher` | Windows launcher app (Avalonia) |
| `src/SubnauticaMP.Server` | Dedicated server console app |
| `lib/BepInEx.Ref` | Compile-only BepInEx API |
| `tests/` | Networking/sync tests + launcher UI tests |
