$ErrorActionPreference = 'Stop'

# Resolve every path from this script so local runs and GitHub Actions use the
# same folder layout. Never depend on a developer-specific C:\Lfall checkout.
$repoRoot = (Resolve-Path -LiteralPath $PSScriptRoot).Path
$publishRoot = Join-Path $repoRoot 'Publish'
$playerOutput = Join-Path $publishRoot 'PlayerSC'
$gamesOutput = Join-Path $publishRoot 'Games'
$playerProject = Join-Path $repoRoot 'PixelFight.Player\PixelFight.Player.csproj'
$licRoot = Join-Path $repoRoot 'Lic'

if (-not (Test-Path -LiteralPath $playerProject -PathType Leaf)) {
    throw "Legacy Player project not found: $playerProject"
}
if (-not (Test-Path -LiteralPath $licRoot -PathType Container)) {
    throw "LIC source directory not found: $licRoot"
}

New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null
Remove-Item -LiteralPath $playerOutput -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $gamesOutput -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $playerOutput -Force | Out-Null
New-Item -ItemType Directory -Path $gamesOutput -Force | Out-Null

Write-Output "Publishing one shared self-contained legacy Player..."
dotnet publish $playerProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $playerOutput --nologo -v q
if ($LASTEXITCODE -ne 0) {
    throw "Legacy Player publish failed with exit code $LASTEXITCODE."
}

$playerExe = Join-Path $playerOutput 'PixelPlayer.exe'
if (-not (Test-Path -LiteralPath $playerExe -PathType Leaf)) {
    throw "Expected self-contained Player executable was not produced: $playerExe"
}

$games = @(
    @('DemoGame', 'demo.lic', 'Demo platformer on PixelEngine.'),
    @('Ages', 'strategy.lic', 'Turn-based strategy: provinces, wars and AI.'),
    @('WorldAges', 'world.lic', 'World strategy: choose a country and expand.'),
    @('Merchant', 'merchant.lic', 'Merchant: trade goods and manage a shop.'),
    @('Tower', 'tower.lic', 'Tower defense with waves and upgradeable towers.'),
    @('Knights', 'knights.lic', 'Knights: build an army and destroy the enemy camp.')
)

foreach ($game in $games) {
    $name = $game[0]
    $licFile = Join-Path $licRoot $game[1]
    if (-not (Test-Path -LiteralPath $licFile -PathType Leaf)) {
        throw "Game source LIC file is missing: $licFile"
    }

    $destination = Join-Path $gamesOutput $name
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Copy-Item -LiteralPath $licFile -Destination (Join-Path $destination 'game.lic') -Force

    # Every small launcher targets the same self-contained Player. This avoids
    # duplicating the full .NET runtime inside each individual game directory.
    $launcher = @'
@echo off
setlocal
cd /d "%~dp0"
"%~dp0..\..\PlayerSC\PixelPlayer.exe" "%~dp0game.lic" %*
'@
    Set-Content -LiteralPath (Join-Path $destination ("Run-$name.cmd")) -Value $launcher -Encoding ASCII

    $readmeText = @(
        "$name - an original Bloody Pixels game on PixelEngine."
        $game[2]
        "Run: Run-$name.cmd"
        "Project: game.lic"
        "Runtime: shared self-contained Player in ..\..\PlayerSC"
    ) -join [Environment]::NewLine
    [System.IO.File]::WriteAllText(
        (Join-Path $destination 'README.txt'),
        $readmeText,
        [System.Text.UTF8Encoding]::new($false)
    )

    if (-not (Test-Path -LiteralPath (Join-Path $destination 'game.lic') -PathType Leaf) -or
        -not (Test-Path -LiteralPath (Join-Path $destination "Run-$name.cmd") -PathType Leaf)) {
        throw "Game package is incomplete: $name"
    }
    Write-Output "Packed $name"
}

$packedGames = @(Get-ChildItem -LiteralPath $gamesOutput -Directory)
if ($packedGames.Count -ne $games.Count) {
    throw "Expected $($games.Count) game packages, created $($packedGames.Count)."
}
Write-Output "Created $($packedGames.Count) game packages in $gamesOutput"
Write-Output "Shared legacy Player: $playerExe"
