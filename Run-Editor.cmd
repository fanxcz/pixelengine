@echo off
setlocal
cd /d "%~dp0"
dotnet run --project ".\PixelEngine.Editor\PixelEngine.Editor.csproj" -c Debug
if errorlevel 1 (
  echo.
  echo PixelEngine Editor exited with an error. Copy the build output when reporting it.
  pause
)
endlocal
