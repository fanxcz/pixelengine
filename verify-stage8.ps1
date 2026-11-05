$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $root
try {
    Write-Host '== PixelEngine Stage 8: static regression guards ==' -ForegroundColor Cyan
    $requiredFiles = @(
        '.\PixelEngine.Core\Animation\AnimationEvaluator.cs',
        '.\PixelEngine.Core\Animation\AnimationPlayerComponent.cs',
        '.\PixelEngine.Core\Diagnostics\ProjectDiagnostics.cs',
        '.\PixelEngine.Core\Diagnostics\EngineProfiler.cs',
        '.\PixelEngine.Core\Graphics\LightingRenderer.cs',
        '.\PixelEngine.Core\Particles\ParticleEmitterComponent.cs',
        '.\PixelEngine.Core\Tilemaps\TileMapComponent.cs',
        '.\PixelEngine.Core\Serialization\LicEditorData.cs',
        '.\PixelEngine.Core\Compatibility\LicSceneFactory.cs',
        '.\PixelEngine.Core\Scene\GameObject.cs',
        '.\PixelEngine.Core\Scene\Scene.cs',
        '.\PixelEngine.Core\Graphics\RenderContext.cs',
        '.\PixelEngine.Core\Scripting\LicScriptRuntime.cs',
        '.\PixelEngine.Core\Core\EngineGame.cs',
        '.\PixelEngine.Editor\Stage8ToolsForm.cs',
        '.\Lic\stage8-showcase.lic'
    )
    foreach ($relative in $requiredFiles) {
        if (-not (Test-Path $relative -PathType Leaf)) { throw "Required Stage 8 file is missing: $relative" }
    }

    $mainForm = Get-Content '.\PixelEngine.Editor\MainForm.cs' -Raw
    $shortcutAssignments = [regex]::Matches($mainForm, 'ShortcutKeys\s*=').Count
    if ($shortcutAssignments -ne 1) { throw "Expected exactly one centralized ShortcutKeys assignment; found $shortcutAssignments." }
    if ($mainForm -notmatch 'var isBarePrintableKey' -or
        $mainForm -notmatch '(?s)if\s*\(shortcut\s*!=\s*Keys\.None\s*&&\s*!isBarePrintableKey\)\s*item\.ShortcutKeys\s*=\s*shortcut;') {
        throw 'The centralized menu shortcut guard is missing; a bare printable key could crash WinForms menu creation.'
    }

    $gameObjectSource = Get-Content '.\PixelEngine.Core\Scene\GameObject.cs' -Raw
    if ($gameObjectSource -notmatch '_componentSnapshot\s*\?\?=\s*_components\.ToArray\(\)' -or
        $gameObjectSource -notmatch '_componentSnapshot\s*=\s*null') {
        throw 'GameObject component snapshot caching/invalidation regression guard failed.'
    }
    $sceneSource = Get-Content '.\PixelEngine.Core\Scene\Scene.cs' -Raw
    if ($sceneSource -notmatch '_objectSnapshot\s*\?\?=\s*_objects\.ToArray\(\)' -or
        $sceneSource -notmatch '_objectSnapshot\s*=\s*null') {
        throw 'Scene object snapshot caching/invalidation regression guard failed.'
    }
    $renderContextSource = Get-Content '.\PixelEngine.Core\Graphics\RenderContext.cs' -Raw
    $engineGameSource = Get-Content '.\PixelEngine.Core\Core\EngineGame.cs' -Raw
    if ($renderContextSource -notmatch 'void\s+ResetFrame\s*\(' -or
        $engineGameSource -notmatch '_renderContext\.ResetFrame\(\)' -or
        [regex]::Matches($engineGameSource, 'new\s+RenderContext\s*\(').Count -ne 1) {
        throw 'Reusable render-context regression guard failed.'
    }
    $legacyFactorySource = Get-Content '.\PixelEngine.Core\Compatibility\LicSceneFactory.cs' -Raw
    if ($legacyFactorySource -match '_entities\.ToArray\s*\(') {
        throw 'Legacy compatibility physics loop regressed to per-step entity array allocations.'
    }
    $scriptRuntimeSource = Get-Content '.\PixelEngine.Core\Scripting\LicScriptRuntime.cs' -Raw
    if ($scriptRuntimeSource -notmatch '(?s)internal\s+void\s+Destroy\([^)]*\).*?_scene\.Remove\(entity\.GameObject\)') {
        throw 'Destroyed legacy entities must always be detached from the scene, including already-flagged entities.'
    }
    $lightingSource = Get-Content '.\PixelEngine.Core\Graphics\LightingRenderer.cs' -Raw
    if ($lightingSource -match 'foreach\s*\(var\s+light\s+in\s+lights\)') {
        throw 'Lighting draw loop regressed to interface enumeration; use an indexed loop to avoid enumerator boxing.'
    }

    $readerSource = Get-Content '.\PixelEngine.Core\Serialization\LicProjectReader.cs' -Raw
    if ($readerSource -match 'map\.EnsureCellStorage\s*\(') {
        throw 'LIC reader must preserve malformed TileMap buffers for Diagnostics instead of silently normalizing them.'
    }

    $smokeSource = Get-Content '.\PixelEngine.SmokeTests\Program.cs' -Raw
    foreach ($marker in @(
        'PASS scene/component update snapshots invalidate on mutation without per-frame array rebuilds',
        'PASS destroyed legacy entities are detached from Scene during idempotent purge',
        'PASS animation sparse channels, pre-key live baseline, partial channels and duplicate-time determinism',
        'PASS prefab propagation across scenes and per-instance transform preservation',
        'PASS particle emitter bounded reusable pool and deterministic update',
        'PASS radial lighting mask falloff and additive-blend premultiplication',
        'PASS ProjectDiagnostics references, prefab templates, asset signatures/formats, C# runtime warning, component values and animation/tilemap channels',
        'PASS Stage 8 showcase animation, lights, particles and tilemap wiring'
    )) {
        if (-not $smokeSource.Contains($marker)) { throw "Regression test marker missing: $marker" }
    }

    if ($smokeSource -notmatch 'invalidTileDimensions' -or $smokeSource -notmatch 'shortTileBuffer') {
        throw 'Malformed TileMap import regression tests are missing.'
    }
    Write-Host 'Static regression guards passed.' -ForegroundColor Green

    Write-Host '== PixelEngine Stage 8: restore ==' -ForegroundColor Cyan
    dotnet restore .\PixelEngine.sln
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE" }

    Write-Host '== PixelEngine Stage 8: Release build ==' -ForegroundColor Cyan
    dotnet build .\PixelEngine.sln -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Release build failed with exit code $LASTEXITCODE" }

    Write-Host '== PixelEngine Stage 8: Debug build ==' -ForegroundColor Cyan
    dotnet build .\PixelEngine.sln -c Debug --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Debug build failed with exit code $LASTEXITCODE" }

    Write-Host '== PixelEngine Stage 8: smoke tests ==' -ForegroundColor Cyan
    dotnet run --project .\PixelEngine.SmokeTests\PixelEngine.SmokeTests.csproj -c Release --no-build -- $root
    if ($LASTEXITCODE -ne 0) { throw "Smoke tests failed with exit code $LASTEXITCODE" }

    Write-Host 'All Stage 8 static, Release/Debug build and smoke-test checks passed.' -ForegroundColor Green
}
finally {
    Pop-Location
}
