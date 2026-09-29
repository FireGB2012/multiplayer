# Subnautica Multiplayer

Co-op mod for Subnautica (the original, BepInEx 5) with a launcher app.

## What syncs
| Thing | Status |
|---|---|
| Players (position, facing, name tags) | ✔ |
| Chat | ✔ |
| Blueprints (unlocks, fragment scans) | ✔ everyone shares one tech tree |
| Databank / PDA entries | ✔ |
| Picked-up items + broken outcrops | ✔ gone for everyone |
| Time of day | ✔ server clock |
| Seamoth / Prawn suit / Cyclops | ✔ show up for everyone, whoever drives it moves it |
| Vehicle destroyed | ✔ |
| World saves on the server | ✔ keeps all of the above between sessions |
| Base building | ✖ not yet |
| Inventory / lockers | ✖ each player keeps their own |
| Creatures / fauna AI | ✖ each player sees their own |
| Dropped items, crafting, story events | ✖ not yet |
| Player models | capsule placeholder for now |

## Quick start (players)
1. Download `SubnauticaMP-Launcher.exe` and run it.
2. **Setup tab**: check it found your Subnautica folder. If BepInEx is missing, hit **Install BepInEx for me**
   (or install BepInEx 5 x64 yourself). Start the game once after installing BepInEx, then close it.
3. **Play tab**:
   - **Host & Play**: starts a server on your PC and opens the game. Your join code is in the **Server** tab. Keep the launcher open.
   - **Join & Play**: paste a friend's join code (like `KQ7MX-3HD2P`) or IP.
4. Load a save or start a new game. You connect automatically a few seconds after it loads.
5. In game, **F8** opens the multiplayer window (chat, join code, leave).

Tip: the first person into a server's world "seeds" it with their save's blueprints and time.
Joiners usually start a **new game** so their world lines up with the host's.

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
dotnet run --project src/SubnauticaMP.Server -- 11000 world.dat
```
Commands: `players`, `save`, `quit`.

## Building
Needs the .NET 8 SDK.
```
dotnet test                                                   # 30 tests: networking, world sync, UPnP, launcher UI
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
