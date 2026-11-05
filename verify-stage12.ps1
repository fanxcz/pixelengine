$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    Write-Host "ERROR: .NET SDK was not found. Install the .NET 8 SDK and run this script again." -ForegroundColor Red
    exit 2
}

Write-Host "[1/4] Restore solution" -ForegroundColor Cyan
dotnet restore .\PixelEngine.sln
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "[2/4] Build Debug" -ForegroundColor Cyan
dotnet build .\PixelEngine.sln -c Debug --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "[3/4] Build Release" -ForegroundColor Cyan
dotnet build .\PixelEngine.sln -c Release --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "[4/4] Run smoke tests" -ForegroundColor Cyan
dotnet run --project .\PixelEngine.SmokeTests\PixelEngine.SmokeTests.csproj -c Release --no-build -- .
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Stage 12 verification completed successfully." -ForegroundColor Green
