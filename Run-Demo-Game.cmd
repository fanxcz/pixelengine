@echo off
setlocal
cd /d "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 (
  echo .NET 8 SDK was not found. Install the .NET 8 SDK and retry.
  pause
  exit /b 1
)
dotnet run --project PixelEngine.Sandbox -- Lic/demo.lic
set "EXITCODE=%ERRORLEVEL%"
if not "%EXITCODE%"=="0" (
  echo PixelEngine Player exited with code %EXITCODE%.
  echo Check %LOCALAPPDATA%\PixelEngine\Logs\player-startup.log
  pause
)
exit /b %EXITCODE%
