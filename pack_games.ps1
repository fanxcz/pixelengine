$ErrorActionPreference = 'Stop'

# Resolve every path from this script so local runs and GitHub Actions use the
# same folder layout. The previous script hard-coded C:\Lfall and silently
# depended on one developer workstation.
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

Write-Output "Publishing legacy PixelPlayer from $playerProject ..."
dotnet publish $playerProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $playerOutput --nologo -v q
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

    # Copy the whole self-contained publish output first, then give the player
    # a game-specific executable name and place that game's LIC beside it.
    Get-ChildItem -LiteralPath $playerOutput -File |
        Where-Object { $_.Extension -notin @('.pdb', '.png') } |
        Copy-Item -Destination $destination -Force

    $gameExe = Join-Path $destination ($name + '.exe')
    Copy-Item -LiteralPath $playerExe -Destination $gameExe -Force

    foreach ($extension in @('.runtimeconfig.json', '.deps.json')) {
        $source = Join-Path $playerOutput ('PixelPlayer' + $extension)
        if (Test-Path -LiteralPath $source -PathType Leaf) {
            Copy-Item -LiteralPath $source -Destination (Join-Path $destination ($name + $extension)) -Force
        }
    }

    Copy-Item -LiteralPath $licFile -Destination (Join-Path $destination 'game.lic') -Force
    $readmeText = @(
        "$name - an original Bloody Pixels game on PixelEngine."
        $game[2]
        "Double-click $name.exe. Self-contained Windows x64 build; no .NET SDK required. Esc exits."
    ) -join [Environment]::NewLine
    [System.IO.File]::WriteAllText(
        (Join-Path $destination 'README.txt'),
        $readmeText,
        [System.Text.UTF8Encoding]::new($false)
    )

    if (-not (Test-Path -LiteralPath $gameExe -PathType Leaf) -or
        -not (Test-Path -LiteralPath (Join-Path $destination 'game.lic') -PathType Leaf)) {
        throw "Game package is incomplete: $name"
    }
    Write-Output "Packed $name"
}

$packedGames = @(Get-ChildItem -LiteralPath $gamesOutput -Directory)
if ($packedGames.Count -ne $games.Count) {
    throw "Expected $($games.Count) game packages, created $($packedGames.Count)."
}
Write-Output "Created $($packedGames.Count) self-contained game packages in $gamesOutput"
