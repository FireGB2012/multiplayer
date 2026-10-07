RIG DUMP - get the Subnautica diver rig into Blender (no multiplayer mod needed)

1. Install BepInEx 5 (x64, Mono) into your Subnautica folder:
   - https://github.com/BepInEx/BepInEx/releases  -> BepInEx_win_x64_5.4.23.x.zip (NOT the 6.x / IL2CPP ones)
   - Extract it so "winhttp.dll" and the "BepInEx" folder sit next to Subnautica.exe
   - Start the game once and quit, so BepInEx creates its folders.
2. Put SubnauticaRigDump.dll in:  Subnautica\BepInEx\plugins\SubnauticaRigDump\
3. Start the game, load a world, press F10. A box says where the files went:
   Subnautica\BepInEx\plugins\RigExport\ (next to where the DLL sits)
     diver_rig.glb          mesh + skeleton + textures
     diver_with_anims.glb   same + all animation clips
     textures\*.png, rig_info.txt
4. Blender: File > Import > glTF 2.0 (.glb), then File > Save As to make your .blend.

Rebuild from source: dotnet build src/SubnauticaMP.RigDump -c Release -p:GameDir="<your Subnautica folder>"
