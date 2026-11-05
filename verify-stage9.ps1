$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $root
try {
    Write-Host '== PixelEngine Stage 9: static regression guards ==' -ForegroundColor Cyan
    $requiredFiles = @(
        '.\PixelEngine.Core\Physics\Physics2DWorld.cs',
        '.\PixelEngine.Core\Physics\Rigidbody2DComponent.cs',
        '.\PixelEngine.Core\Physics\Collider2DComponent.cs',
        '.\PixelEngine.Core\Input\InputActionMap.cs',
        '.\PixelEngine.Core\Audio\AudioSourceComponent.cs',
        '.\PixelEngine.Core\Audio\AudioMixer.cs',
        '.\PixelEngine.Core\Storage\PersistentSaveStore.cs',
        '.\PixelEngine.Core\Graphics\CameraFollowComponent.cs',
        '.\PixelEngine.Core\Graphics\SortingLayerRegistry.cs',
        '.\PixelEngine.Core\Core\EngineGame.cs',
        '.\PixelEngine.Core\Serialization\LicEditorData.cs',
        '.\PixelEngine.Core\Compatibility\LicSceneFactory.cs',
        '.\PixelEngine.Core\Scripting\LicScriptRuntime.cs',
        '.\PixelEngine.Editor\Stage8ToolsForm.cs',
        '.\PixelEngine.SmokeTests\Program.cs',
        '.\Publish-Game.ps1'
    )
    foreach ($relative in $requiredFiles) {
        if (-not (Test-Path $relative -PathType Leaf)) { throw "Required Stage 9 file is missing: $relative" }
    }

    $physics = Get-Content '.\PixelEngine.Core\Physics\Physics2DWorld.cs' -Raw
    foreach ($pattern in @('BoxBox', 'BoxCircle', 'CircleCircle', 'PositionCorrectionPercent', 'OnCollisionEnter2D', 'OnTriggerEnter2D')) {
        if ($physics -notmatch [regex]::Escape($pattern)) { throw "Physics regression guard missing: $pattern" }
    }
    $sceneFactory = Get-Content '.\PixelEngine.Core\Compatibility\LicSceneFactory.cs' -Raw
    if ($sceneFactory -notmatch 'using PixelEngine\.Physics;' -or $sceneFactory -notmatch 'AddTileMapCollisionGeometry') {
        throw 'LIC TileMap collision integration/import is missing.'
    }
    $scriptRuntime = Get-Content '.\PixelEngine.Core\Scripting\LicScriptRuntime.cs' -Raw
    foreach ($pattern in @('GetAuthoredColliderSize', 'GetActionAxis', 'PersistentSaveStore', 'AudioSourceComponent')) {
        if ($scriptRuntime -notmatch [regex]::Escape($pattern)) { throw "LIC runtime integration missing: $pattern" }
    }
    $engine = Get-Content '.\PixelEngine.Core\Core\EngineGame.cs' -Raw
    if ($engine -notmatch 'RuntimeProfilerStore\.WriteSnapshot' -or $engine -notmatch 'TotalSeconds >= 1d') {
        throw 'Player-to-Studio profiler telemetry is not rate-limited and connected.'
    }
    $tileData = Get-Content '.\PixelEngine.Core\Serialization\LicEditorData.cs' -Raw
    if ($tileData -notmatch 'void\s+GenerateCave') { throw 'Seeded TileMap cave generation is missing.' }

    $smoke = Get-Content '.\PixelEngine.SmokeTests\Program.cs' -Raw
    foreach ($marker in @(
        'component Start/OnRemoved lifecycle',
        '2D physics broadphase',
        'input action bindings, render sorting, camera bounds and audio mixer levels',
        'project save persistence, safe file recovery and in-memory fallback',
        'TileMap collision baker did not merge adjacent solid cells',
        'Seeded cave generation was not deterministic'
    )) {
        if (-not $smoke.Contains($marker)) { throw "Stage 9 smoke-test marker missing: $marker" }
    }
    Write-Host 'Static regression guards passed.' -ForegroundColor Green

    Write-Host '== PixelEngine Stage 9: restore ==' -ForegroundColor Cyan
    dotnet restore .\PixelEngine.sln
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE" }

    Write-Host '== PixelEngine Stage 9: Release build ==' -ForegroundColor Cyan
    dotnet build .\PixelEngine.sln -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Release build failed with exit code $LASTEXITCODE" }

    Write-Host '== PixelEngine Stage 9: Debug build ==' -ForegroundColor Cyan
    dotnet build .\PixelEngine.sln -c Debug --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Debug build failed with exit code $LASTEXITCODE" }

    Write-Host '== PixelEngine Stage 9: smoke tests ==' -ForegroundColor Cyan
    dotnet run --project .\PixelEngine.SmokeTests\PixelEngine.SmokeTests.csproj -c Release --no-build -- $root
    if ($LASTEXITCODE -ne 0) { throw "Smoke tests failed with exit code $LASTEXITCODE" }

    Write-Host 'All Stage 9 static, Release/Debug build and smoke-test checks passed.' -ForegroundColor Green
}
finally {
    Pop-Location
}
