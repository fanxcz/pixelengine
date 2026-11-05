$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $root
try {
    Write-Host '== PixelEngine Stage 10: static regression guards ==' -ForegroundColor Cyan
    $requiredFiles = @(
        '.\PixelEngine.Core\Animation\SpriteSheetAnimationComponent.cs',
        '.\PixelEngine.Core\Graphics\LightingRenderer.cs',
        '.\PixelEngine.Core\Graphics\Light2DComponent.cs',
        '.\PixelEngine.Core\Graphics\SpriteAtlasBuilder.cs',
        '.\PixelEngine.Core\Graphics\SpriteRenderer.cs',
        '.\PixelEngine.Core\Physics\Collider2DComponent.cs',
        '.\PixelEngine.Core\Assets\AssetStore.cs',
        '.\PixelEngine.Core\Serialization\LicEditorData.cs',
        '.\PixelEngine.Core\Serialization\LicProjectReader.cs',
        '.\PixelEngine.Core\Diagnostics\ProjectDiagnostics.cs',
        '.\PixelEngine.Editor\ComponentManagerControl.cs',
        '.\PixelEngine.Editor\Stage8ToolsForm.cs',
        '.\PixelEngine.SmokeTests\Program.cs',
        '.\Lic\stage10-showcase.lic',
        '.\Run-Stage10-Showcase.cmd'
    )
    foreach ($relative in $requiredFiles) {
        if (-not (Test-Path $relative -PathType Leaf)) { throw "Required Stage 10 file is missing: $relative" }
    }

    $lighting = Get-Content '.\PixelEngine.Core\Graphics\LightingRenderer.cs' -Raw
    foreach ($pattern in @('BuildLightMapPixels', 'IsOccluded', 'MaximumLights = 16', 'MaximumSegmentsPerLight = 64', 'ambientLight')) {
        if ($lighting -notmatch [regex]::Escape($pattern)) { throw "Lighting regression guard missing: $pattern" }
    }
    $spriteSheet = Get-Content '.\PixelEngine.Core\Animation\SpriteSheetAnimationComponent.cs' -Raw
    foreach ($pattern in @('ResolveFrameIndex', 'PingPong', 'LicObjectComponent target', 'SpriteRenderer target')) {
        if ($spriteSheet -notmatch [regex]::Escape($pattern)) { throw "Sprite-sheet regression guard missing: $pattern" }
    }
    $atlas = Get-Content '.\PixelEngine.Core\Graphics\SpriteAtlasBuilder.cs' -Raw
    foreach ($pattern in @('Extrude edge texels', 'TryPack', 'SpriteAtlasRegion', 'x += width + padding * 2', 'rowHeight + padding * 2')) {
        if ($atlas -notmatch [regex]::Escape($pattern)) { throw "Sprite-atlas regression guard missing: $pattern" }
    }
    $assets = Get-Content '.\PixelEngine.Core\Assets\AssetStore.cs' -Raw
    if ($assets -notmatch 'BuildSpriteAtlas' -or $assets -notmatch 'GetSpriteAtlas') { throw 'AssetStore atlas ownership/lookup is not integrated.' }
    $physicsWorld = Get-Content '.\PixelEngine.Core\Physics\Physics2DWorld.cs' -Raw
    if ($physicsWorld -notmatch 'using SceneType = PixelEngine\.Scene\.Scene;' -or $physicsWorld -notmatch 'Step\(SceneType scene') { throw 'Physics2DWorld must explicitly alias the Scene type to avoid the PixelEngine.Scene namespace/type collision.' }
    $colliders = Get-Content '.\PixelEngine.Core\Physics\Collider2DComponent.cs' -Raw
    if ($colliders -notmatch 'CastsShadows' -or $colliders -notmatch 'EnqueueShadowSegment') { throw 'Collider shadow geometry integration is missing.' }
    $factory = Get-Content '.\PixelEngine.Core\Compatibility\LicSceneFactory.cs' -Raw
    if ($factory -notmatch 'AmbientLight' -or $factory -notmatch 'FlickerAmount' -or $factory -notmatch 'Falloff') { throw 'LIC scene ambient/light settings are not mapped into runtime.' }
    $smoke = Get-Content '.\PixelEngine.SmokeTests\Program.cs' -Raw
    foreach ($marker in @('Stage 10 light-map ray occlusion', 'Stage 10 LIC metadata', 'Sprite-sheet ping-pong animation sequence', 'zero smoothing', 'Component clone lost authoring properties', 'unsupported/conflicting component configuration', 'Sorting layer registration changed existing depths', 'stable sorting-layer budget', 'LIC transformed picking: rotation, scaling and moved polygons')) {
        if (-not $smoke.Contains($marker)) { throw "Stage 10/full-function smoke-test marker missing: $marker" }
    }
    $sortingRegistry = Get-Content '.\PixelEngine.Core\Graphics\SortingLayerRegistry.cs' -Raw
    if ($sortingRegistry -notmatch 'MaximumLayers = 256' -or $sortingRegistry -notmatch 'LayerStep = .90f / MaximumLayers' -or $sortingRegistry -notmatch 'catch \(InvalidOperationException\)') { throw 'Sorting layer capacity/stable-depth fallback is missing.' }
    $licObjectRenderer = Get-Content '.\PixelEngine.Core\Compatibility\LicObjectComponent.cs' -Raw
    if ($licObjectRenderer -notmatch 'Transform\.Rotation' -or $licObjectRenderer -notmatch 'object-local coordinates' -or $licObjectRenderer -notmatch 'Math\.Abs\(world\.X\)') { throw 'LIC render transform/polygon safety integration is missing.' }
    $licScriptRuntime = Get-Content '.\PixelEngine.Core\Scripting\LicScriptRuntime.cs' -Raw
    if ($licScriptRuntime -notmatch 'ContainsTransformed' -or $licScriptRuntime -notmatch 'inverse' -and $licScriptRuntime -notmatch 'object-local coordinates') { throw 'LIC transformed pointer-picking integration is missing.' }
    $cameraFollow = Get-Content '.\PixelEngine.Core\Graphics\CameraFollowComponent.cs' -Raw
    if ($cameraFollow -notmatch 'speed <= 0f \? 1f' -or $cameraFollow -notmatch 'float.IsFinite\(Offset.X\)' -or $cameraFollow -notmatch 'IsFinite\(desired\)') { throw 'CameraFollow zero-speed snap/finite-value guard is missing.' }
    $operations = Get-Content '.\PixelEngine.Core\Serialization\LicProjectOperations.cs' -Raw
    foreach ($property in @('LinearDamping = source.LinearDamping', 'CollisionMask = source.CollisionMask', 'SpritePingPong = source.SpritePingPong', 'CameraSmoothSpeed = source.CameraSmoothSpeed')) {
        if ($operations -notmatch [regex]::Escape($property)) { throw "Component clone regression guard missing: $property" }
    }
    $inputMap = Get-Content '.\PixelEngine.Core\Input\InputActionMap.cs' -Raw
    if ($inputMap -notmatch 'Enum.IsDefined' -or $inputMap -notmatch 'NormalizeAction') { throw 'InputActionMap validation/normalization guard is missing.' }

    Write-Host '== PixelEngine Stage 10: LIC and embedded asset checks ==' -ForegroundColor Cyan
    $licFiles = Get-ChildItem '.\Lic' -Recurse -Filter '*.lic' -File
    if ($licFiles.Count -eq 0) { throw 'No LIC fixtures were found.' }
    foreach ($file in $licFiles) {
        $project = Get-Content $file.FullName -Raw | ConvertFrom-Json
        if (-not $project.name -or -not $project.scenes) { throw "Invalid LIC project root: $($file.FullName)" }
        foreach ($asset in @($project.assets)) {
            if ([string]::IsNullOrWhiteSpace($asset.base64)) { continue }
            try { $bytes = [Convert]::FromBase64String($asset.base64) }
            catch { throw "Asset '$($asset.name)' in $($file.Name) has invalid Base64." }
            if ($asset.type -eq 'png') {
                if ($bytes.Length -lt 8 -or $bytes[0] -ne 137 -or $bytes[1] -ne 80 -or $bytes[2] -ne 78 -or $bytes[3] -ne 71) {
                    throw "Asset '$($asset.name)' in $($file.Name) is not a PNG payload."
                }
            }
        }
    }
    Write-Host "Checked $($licFiles.Count) LIC projects and embedded Base64/PNG payloads." -ForegroundColor Green
    Write-Host 'Static regression guards passed.' -ForegroundColor Green

    Write-Host '== PixelEngine Stage 10: restore ==' -ForegroundColor Cyan
    dotnet restore .\PixelEngine.sln
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE" }

    Write-Host '== PixelEngine Stage 10: Release build ==' -ForegroundColor Cyan
    dotnet build .\PixelEngine.sln -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Release build failed with exit code $LASTEXITCODE" }

    Write-Host '== PixelEngine Stage 10: Debug build ==' -ForegroundColor Cyan
    dotnet build .\PixelEngine.sln -c Debug --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Debug build failed with exit code $LASTEXITCODE" }

    Write-Host '== PixelEngine Stage 10: smoke tests ==' -ForegroundColor Cyan
    dotnet run --project .\PixelEngine.SmokeTests\PixelEngine.SmokeTests.csproj -c Release --no-build -- $root
    if ($LASTEXITCODE -ne 0) { throw "Smoke tests failed with exit code $LASTEXITCODE" }

    Write-Host 'All Stage 10 static, Release/Debug build and smoke-test checks passed.' -ForegroundColor Green
}
finally {
    Pop-Location
}
