[CmdletBinding()]
param(
    [switch]$StaticOnly,
    [switch]$SkipRestore
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $root

function Assert-Condition([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}

function Invoke-DotNet([string[]]$arguments, [string]$step) {
    Write-Host "== $step ==" -ForegroundColor Cyan
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "$step failed with exit code $LASTEXITCODE" }
}

try {
    Write-Host '== PixelEngine Stage 11: repository structure ==' -ForegroundColor Cyan
    $solutionPath = Join-Path $root 'PixelEngine.sln'
    Assert-Condition (Test-Path $solutionPath -PathType Leaf) 'PixelEngine.sln is missing.'

    $solutionText = Get-Content $solutionPath -Raw
    $projectMatches = [regex]::Matches($solutionText, 'Project\([^\r\n]+\)\s*=\s*"[^"]+"\s*,\s*"([^"]+\.csproj)"')
    Assert-Condition ($projectMatches.Count -ge 4) 'The solution must contain Core, Editor, Sandbox and SmokeTests projects.'
    foreach ($match in $projectMatches) {
        $relative = $match.Groups[1].Value -replace '/', '\'
        $projectPath = Join-Path $root $relative
        Assert-Condition (Test-Path $projectPath -PathType Leaf) "Solution project is missing: $relative"
        [xml]$projectXml = Get-Content $projectPath -Raw
        foreach ($reference in @($projectXml.SelectNodes('//ProjectReference'))) {
            $referencePath = [System.IO.Path]::GetFullPath((Join-Path (Split-Path $projectPath -Parent) $reference.Include))
            Assert-Condition (Test-Path $referencePath -PathType Leaf) "Project reference is missing: $($reference.Include) in $relative"
        }
    }

    Write-Host '== PixelEngine Stage 11: source regression guards ==' -ForegroundColor Cyan
    $required = @{
        'PixelEngine.Core/Scene/GameObject.cs' = @('_components.AsReadOnly()', 'component.NotifyRemoved()')
        'PixelEngine.Core/Scene/Scene.cs' = @('_objects.AsReadOnly()')
        'PixelEngine.Core/Scene/Transform.cs' = @('float.IsFinite', 'Sanitize')
        'PixelEngine.Core/Core/EngineTime.cs' = @('SanitizeDeltaTime', 'MaximumDeltaSeconds')
        'PixelEngine.Core/Core/EngineGame.cs' = @('EngineTime.SanitizeDeltaTime', 'elapsedSeconds')
        'PixelEngine.Core/Graphics/SpriteAtlasBuilder.cs' = @('ReadOnlyDictionary<string, SpriteAtlasRegion>')
        'PixelEngine.Core/Serialization/LicProjectReader.cs' = @('MaximumProjectBytes', 'ReadProjectText')
        'PixelEngine.Core/Serialization/LicProjectWriter.cs' = @('LicProjectReader.MaximumProjectBytes')
        'PixelEngine.Core/Serialization/LicAssetOperations.cs' = @('maximumBytes <= 0', 'total > maximumBytes', 'bytes.')
        'PixelEngine.Core/Diagnostics/RuntimeProfilerStore.cs' = @('MaximumSnapshotBytes', 'IsValidSnapshot')
        'PixelEngine.SmokeTests/Program.cs' = @('Stage 11 scene graph/time guards', 'Stage 11 profiler telemetry', 'failed-attachment cleanup', 'bounded PNG import')
    }
    foreach ($entry in $required.GetEnumerator()) {
        $path = Join-Path $root $entry.Key
        Assert-Condition (Test-Path $path -PathType Leaf) "Required source file is missing: $($entry.Key)"
        $content = Get-Content $path -Raw
        foreach ($marker in $entry.Value) {
            Assert-Condition ($content.Contains($marker)) "Regression guard '$marker' is missing from $($entry.Key)."
        }
    }

    Write-Host '== PixelEngine Stage 11: LIC fixtures and embedded payloads ==' -ForegroundColor Cyan
    $licFiles = @(Get-ChildItem (Join-Path $root 'Lic') -Recurse -Filter '*.lic' -File)
    Assert-Condition ($licFiles.Count -gt 0) 'No LIC fixture projects were found.'
    foreach ($file in $licFiles) {
        $project = Get-Content $file.FullName -Raw | ConvertFrom-Json
        Assert-Condition (-not [string]::IsNullOrWhiteSpace($project.name)) "LIC project has no name: $($file.Name)"
        Assert-Condition (@($project.scenes).Count -gt 0) "LIC project has no scenes: $($file.Name)"
        foreach ($asset in @($project.assets)) {
            if ([string]::IsNullOrWhiteSpace($asset.base64)) { continue }
            try { $bytes = [Convert]::FromBase64String($asset.base64) }
            catch { throw "Invalid Base64 in asset '$($asset.name)' from $($file.Name): $($_.Exception.Message)" }
            if ($asset.type -eq 'png') {
                Assert-Condition ($bytes.Length -ge 8 -and $bytes[0] -eq 137 -and $bytes[1] -eq 80 -and $bytes[2] -eq 78 -and $bytes[3] -eq 71) "Invalid PNG signature in '$($asset.name)' from $($file.Name)."
            }
            elseif ($asset.type -eq 'wav') {
                $riff = [System.Text.Encoding]::ASCII.GetString($bytes, 0, [Math]::Min(4, $bytes.Length))
                $wave = if ($bytes.Length -ge 12) { [System.Text.Encoding]::ASCII.GetString($bytes, 8, 4) } else { '' }
                Assert-Condition ($riff -eq 'RIFF' -and $wave -eq 'WAVE') "Invalid WAV signature in '$($asset.name)' from $($file.Name)."
            }
        }
    }
    Write-Host "Validated $($projectMatches.Count) solution projects and $($licFiles.Count) LIC fixtures." -ForegroundColor Green

    if ($StaticOnly) {
        Write-Host 'Static Stage 11 checks passed. Build and smoke tests were skipped by request.' -ForegroundColor Green
        return
    }

    if (-not $SkipRestore) {
        Invoke-DotNet @('restore', '.\PixelEngine.sln') 'NuGet restore'
    }
    Invoke-DotNet @('build', '.\PixelEngine.sln', '-c', 'Debug', '--no-restore') 'Debug solution build'
    Invoke-DotNet @('build', '.\PixelEngine.sln', '-c', 'Release', '--no-restore') 'Release solution build'
    Invoke-DotNet @('run', '--project', '.\PixelEngine.SmokeTests\PixelEngine.SmokeTests.csproj', '-c', 'Release', '--no-build', '--', $root) 'Smoke tests'

    Write-Host 'All Stage 11 static checks, Debug/Release builds and smoke tests passed.' -ForegroundColor Green
}
finally {
    Pop-Location
}
