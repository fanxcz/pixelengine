$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

Write-Host "Building PixelEngine.Editor (Debug)..." -ForegroundColor Cyan
dotnet build (Join-Path $root "PixelEngine.Editor\PixelEngine.Editor.csproj") -c Debug
if ($LASTEXITCODE -ne 0) {
    throw "Editor build failed with exit code $LASTEXITCODE. Fix the build errors above first."
}

$exe = Join-Path $root "PixelEngine.Editor\bin\Debug\net8.0-windows\PixelEngine.Editor.exe"
if (-not (Test-Path $exe)) { throw "Editor executable was not created: $exe" }

Write-Host "Launching $exe" -ForegroundColor Cyan
$process = Start-Process -FilePath $exe -WorkingDirectory $root -PassThru
Start-Sleep -Milliseconds 1200
$process.Refresh()
if ($process.HasExited) {
    $log = Join-Path $env:LOCALAPPDATA "PixelEngine\Logs\editor-startup.log"
    Write-Host "Editor exited during startup (code $($process.ExitCode))." -ForegroundColor Red
    if (Test-Path $log) { Get-Content $log -Tail 80 }
    throw "Editor did not stay open. See startup log above."
}
Write-Host "PixelEngine Editor is running. PID: $($process.Id)" -ForegroundColor Green
