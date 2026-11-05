# ASHFALL: The Last Beacon

A top-down arena-survival game made for **PixelEngine 2.0 / Stage 11**. The project is self-contained: scene, Lua gameplay, pixel-art sprites and sound effects are embedded in `Ashfall.lic`.

## Install

If you downloaded `Ashfall-Game-Package.zip`, extract it and copy the resulting `Ashfall` folder into your PixelEngine repository at `Games\Ashfall`.

## Run from the PixelEngine repository root

```powershell
dotnet run --project .\PixelEngine.Sandbox\PixelEngine.Sandbox.csproj -c Debug -- ".\Games\Ashfall\Ashfall.lic"
```

You can also run `Games\Ashfall\Run-Ashfall.cmd` after placing the folder in that location, or open `Games\Ashfall\Ashfall.lic` in PixelEngine Editor.

## Controls

- **WASD / Arrow keys** — move (left stick / D-pad supported)
- **Mouse click** — fire toward the pointer
- **J or F (hold)** — continuous fire toward the nearest enemy
- **Left Shift / Space** — dash with brief invulnerability
- **Q** — Nova pulse; damages nearby enemies and destroys enemy shots
- **P** — pause; **Enter** resumes
- **1 / 2 / 3** — choose a level-up upgrade
- **Gamepad** — left stick/D-pad move, right stick aim, Right Shoulder fire, B dash, Y nova, Start pause

## Game loop

Survive four increasingly difficult sectors, defeat three enemy types, collect energy shards and repair kits, and choose an upgrade whenever the Beacon levels up. Sector five ends with the Rift Tyrant boss. Your best score is saved between runs.

## Project contents

- `Ashfall.lic` — portable PixelEngine project with scene, Lua logic and embedded PNG/WAV assets.
- `Game/game.lua` — editable Lua gameplay source (the same source is embedded in the LIC file).
- `Assets/` — editable original pixel sprites and sound effects.
- `Preview.png` — visual preview of the game.
- `VALIDATION.md` — what has and has not been tested.

The game uses only the PixelEngine LIC Lua API and has no external runtime dependency beyond PixelEngine itself.
