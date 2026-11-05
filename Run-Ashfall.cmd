@echo off
setlocal
set "ROOT=%~dp0"
dotnet run --project "%ROOT%PixelEngine.Sandbox\PixelEngine.Sandbox.csproj" -c Debug -- "%ROOT%Games\Ashfall\Ashfall.lic"
