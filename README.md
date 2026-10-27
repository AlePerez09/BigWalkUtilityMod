# Big Walk Noclip

A BepInEx 6 (IL2CPP) mod for **Big Walk** that adds toggleable noclip flight and button helpers.

## Features

| Key | Action |
|-----|--------|
| **F** | Toggle noclip fly |
| **W / A / S / D** | Fly in the direction you're looking |
| **Space / Left Ctrl** | Fly up / down |
| **Left Shift** | Fly faster |
| **Y** | Press the button you're aiming at |
| **U** | Toggle auto-press (presses buttons as you walk near them) |
| **H** | Inspect what you're aiming at (logs its scripts to the console, for debugging) |

All keys, fly speed and ranges can be changed in `BepInEx/config/com.ale.bigwalk.noclip.cfg` (created after the first launch; edit it with the game closed).

## Install

1. Install **BepInEx 6 bleeding edge, Unity IL2CPP x64**. Big Walk is an IL2CPP game, so BepInEx 5 and the GitHub `6.0.0-pre` releases will not work.
   - I used the **BepInExPack IL2CPP** package from Thunderstore: [old.thunderstore.io/c/megabonk/p/BepInEx/BepInExPack_IL2CPP](https://old.thunderstore.io/c/megabonk/p/BepInEx/BepInExPack_IL2CPP/). It's listed under another game's page, but it's the same generic BepInEx pack and works for Big Walk.
   - Click **Manual Download**, extract the zip into its own folder, then copy everything inside its `BepInExPack` folder into the folder that contains `Big Walk.exe`.
2. Launch the game once and wait for it to reach the main menu (the first launch generates files in `BepInEx/interop/`), then close it.
3. Put `BigWalkNoclip.dll` in `Big Walk/BepInEx/plugins/`.

## Build from source

Requires the .NET SDK 6 or newer, and BepInEx installed and run once (see above).

```
dotnet build -c Release
```

If Big Walk isn't in the default Steam folder, pass its path:

```
dotnet build -c Release -p:GameDir="D:\SteamLibrary\steamapps\common\Big Walk"
```

The DLL is written to `bin/Release/net6.0/BigWalkNoclip.dll`.

## Notes

- Button pressing works through the host. Use it solo or in a lobby you're hosting.
- Game updates can break the mod. If something stops working, check `BepInEx/LogOutput.log`.
