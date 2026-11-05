[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$ProjectFile,

    [Parameter(Mandatory = $true)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$LicFile,

    [string]$OutputDirectory = '.\Publish\Game',
    [string]$GameName = 'MyGame',

    [ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')]
    [string]$RuntimeIdentifier = 'win-x64',

    [switch]$FrameworkDependent
)

$ErrorActionPreference = 'Stop'
$project = (Resolve-Path -LiteralPath $ProjectFile).Path
$lic = (Resolve-Path -LiteralPath $LicFile).Path
$name = ($GameName -replace '[^A-Za-z0-9_-]', '_').Trim('_')
if ([string]::IsNullOrWhiteSpace($name)) { throw 'GameName must contain at least one letter or number.' }

$destination = [System.IO.Path]::GetFullPath($OutputDirectory)
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '.'))
if ($destination -eq [System.IO.Path]::GetPathRoot($destination) -or $destination -eq $repoRoot) {
    throw 'Refusing to clear a drive root or the repository root as an output directory.'
}

if (Test-Path -LiteralPath $destination -PathType Container) {
    $existingItems = @(Get-ChildItem -LiteralPath $destination -Force)
    if ($existingItems.Count -gt 0) { throw 'OutputDirectory must be empty to avoid deleting or mixing existing game files.' }
} else {
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
}

$publishDirectory = Join-Path $destination '.runtime'

$arguments = @(
    'publish', $project,
    '--configuration', 'Release',
    '--runtime', $RuntimeIdentifier,
    '--output', $publishDirectory,
    '--nologo',
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true'
)
if ($FrameworkDependent) {
    $arguments += '--no-self-contained'
    $arguments += '-p:SelfContained=false'
} else {
    $arguments += '--self-contained'
    $arguments += 'true'
}

Write-Host "Publishing $name for $RuntimeIdentifier..." -ForegroundColor Cyan
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

$exeExtension = if ($RuntimeIdentifier.StartsWith('win-')) { '.exe' } else { '' }
$appHost = Join-Path $publishDirectory ("PixelEngine.Sandbox" + $exeExtension)
if (-not (Test-Path -LiteralPath $appHost -PathType Leaf)) {
    $candidates = Get-ChildItem -LiteralPath $publishDirectory -File | Where-Object {
        $_.Name -eq 'PixelEngine.Sandbox' -or $_.Name -eq 'PixelEngine.Sandbox.exe'
    }
    if ($candidates.Count -ne 1) { throw 'Could not identify the published PixelEngine.Sandbox app host.' }
    $appHost = $candidates[0].FullName
}

$hostFileName = [System.IO.Path]::GetFileName($appHost)
$gameHost = Join-Path $destination $hostFileName
Copy-Item -LiteralPath $appHost -Destination $gameHost -Force
Get-ChildItem -LiteralPath $publishDirectory -File | Where-Object {
    $_.Name -ne [System.IO.Path]::GetFileName($appHost)
} | Copy-Item -Destination $destination -Force
Copy-Item -LiteralPath $lic -Destination (Join-Path $destination ($name + '.lic')) -Force
Remove-Item -LiteralPath $publishDirectory -Recurse -Force

if ($RuntimeIdentifier.StartsWith('win-')) {
    @"
@echo off
cd /d "%~dp0"
"%~dp0$hostFileName" "%~dp0$name.lic" %*
"@ | Set-Content -LiteralPath (Join-Path $destination ("Run-" + $name + '.cmd')) -Encoding ASCII
} else {
    @"
#!/usr/bin/env sh
set -eu
cd "`$(dirname "`$0")"
chmod +x "./$hostFileName"
exec "./$hostFileName" "./$name.lic" "`$@"
"@ | Set-Content -LiteralPath (Join-Path $destination ("Run-" + $name + '.sh')) -Encoding ASCII
}

$dependencyNote = if ($FrameworkDependent) { 'Requires .NET 8 Desktop Runtime on the target machine.' } else { 'Self-contained .NET deployment; native desktop audio/graphics dependencies may still be required by the OS.' }
@"
$name — PixelEngine game

Run: $(if ($RuntimeIdentifier.StartsWith('win-')) { 'Run-' + $name + '.cmd' } else { 'sh Run-' + $name + '.sh' })
Target: $RuntimeIdentifier
$dependencyNote
Project data: $name.lic

This folder contains the published PixelEngine Sandbox runtime and the selected LIC game.
"@ | Set-Content -LiteralPath (Join-Path $destination 'README.txt') -Encoding UTF8

Write-Host "Game package created: $destination" -ForegroundColor Green
