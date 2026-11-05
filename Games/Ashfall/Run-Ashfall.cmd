@echo off
setlocal
for %%I in ("%~dp0..\..") do set "ENGINE_ROOT=%%~fI"
dotnet run --project "%ENGINE_ROOT%\PixelEngine.Sandbox\PixelEngine.Sandbox.csproj" -c Debug -- "%~dp0Ashfall.lic"
