# ASHFALL validation record

## Passed in the build environment

- `Ashfall.lic` parses as JSON and contains one scene, one Lua script, 81 scene objects and 13 embedded assets.
- Scene object names are unique and all authored sprite references resolve.
- Embedded Lua source exactly matches `Game/game.lua`.
- PNG signatures and image files were validated; the pilot sprite sheet is 64x16 with four 16x16 frames.
- WAV RIFF/WAVE headers and decoded audio metadata were validated.
- All 16 `api.*` methods used by `game.lua` exist in PixelEngine's `LicScriptApi`.
- Spawn option fields used by Lua are supported by `LicRuntimeContext.Spawn`.
- Lua 5.4 syntax and simulation passed.
- Regression scenario passed: menu/start, XP upgrade flow, all three regular enemy types, projectile firing, Nova, boss spawn/defeat, victory, score persistence, defeat and return to menu.
- A 12,000-frame simulated session completed without Lua errors.
- ZIP packages passed archive integrity checks.

## Still requires a real Windows run

C# compilation, MoonSharp execution inside MonoGame, actual graphics/audio output, controller input and physical mouse/keyboard input were not run in this environment because the .NET SDK and Windows/MonoGame runtime are unavailable here. Use the commands in `README.md` on Windows to perform the real engine test.
