# Prepares a fresh clone for building: fetches the WolvenKit submodule at the pinned commit and applies the
# patch that adds patch 2.5 save support (patches/wolvenkit-patch-2.5.patch). Safe to run more than once.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$patch = Join-Path $root 'patches\wolvenkit-patch-2.5.patch'

git -C $root submodule update --init WolvenKit
if ($LASTEXITCODE -ne 0) { throw 'could not fetch the WolvenKit submodule' }

git -C (Join-Path $root 'WolvenKit') apply --check --reverse $patch 2>$null
if ($LASTEXITCODE -eq 0) {
    Write-Host 'WolvenKit patch already applied.'
}
else {
    git -C (Join-Path $root 'WolvenKit') apply --whitespace=nowarn $patch
    if ($LASTEXITCODE -ne 0) { throw 'the WolvenKit patch does not apply; is the submodule at the pinned commit?' }
    Write-Host 'WolvenKit patch applied.'
}

Write-Host ''
Write-Host 'Build:   dotnet build CP2077SaveEditor.Wpf -c Release'
Write-Host 'Release: dotnet publish CP2077SaveEditor.Wpf -c Release -r win-x64 -o publish\wpf'
