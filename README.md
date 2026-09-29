# Subnautica Multiplayer (starter)

A BepInEx mod that lets you see your friends swimming around in Subnautica, plus chat.

## What works right now
- Host from inside the game, or run a dedicated server
- Join by IP
- See other players as colored capsules with name tags + distance
- Smooth movement (20 updates/sec, interpolated)
- Chat
- Up to 16 players

## What does NOT sync (yet)
Each player still has their own world. Not synced: bases, building, inventory, vehicles,
creatures, time of day, story progress, picked-up items. The map is the same for everyone,
so you can still meet up and explore together. For full co-op check out Nitrox.

## Build + install
1. Install **BepInEx 5** (x64) into your Subnautica folder and launch the game once.
2. Install the [.NET SDK](https://dotnet.microsoft.com/download) (8.0 or newer).
3. Build the mod:
   ```
   dotnet build src/SubnauticaMP.Plugin -c Release
   ```
   If the game isn't in the default Steam folder:
   ```
   dotnet build src/SubnauticaMP.Plugin -c Release -p:GameDir="D:\SteamLibrary\steamapps\common\Subnautica"
   ```
   The build copies `SubnauticaMP.dll` into `BepInEx\plugins\SubnauticaMP\` for you.
4. Every player needs the same version of the mod.

## Playing
- Press **F8** in game to open the multiplayer window (key is changeable in
  `BepInEx\config\com.firegb2012.subnauticamp.cfg`).
- **Host**: click Host. Friends join your IP on port `11000`. Over the internet you need to
  port forward TCP 11000, or use something like Tailscale / ZeroTier / Radmin VPN.
- **Join**: type the host's IP, click Join.

## Dedicated server
```
dotnet run --project src/SubnauticaMP.Server -- 11000
```
Type `players` to see who's on, `quit` to stop.

## Project layout
| Folder | What it is |
|---|---|
| `src/SubnauticaMP.Shared` | Network protocol, server, client (no Unity, fully tested) |
| `src/SubnauticaMP.Server` | Dedicated server console app |
| `src/SubnauticaMP.Plugin` | The BepInEx mod that runs inside the game |
| `tests/SubnauticaMP.Tests` | Tests for the networking |

Run tests with `dotnet test`. The plugin isn't in the `.sln` because it needs the game's
DLLs to build.
