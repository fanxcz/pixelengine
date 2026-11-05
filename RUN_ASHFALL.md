# Run ASHFALL: The Last Beacon

Open PowerShell in this folder and run:

```powershell
.\Run-Ashfall.cmd
```

Or use `dotnet run` directly:

```powershell
dotnet run --project .\PixelEngine.Sandbox\PixelEngine.Sandbox.csproj -c Debug -- ".\Games\Ashfall\Ashfall.lic"
```

You can also launch the editor and open `Games\Ashfall\Ashfall.lic`:

```powershell
dotnet run --project .\PixelEngine.Editor\PixelEngine.Editor.csproj -c Debug
```

Controls and game details are in `Games\Ashfall\README.md`.
