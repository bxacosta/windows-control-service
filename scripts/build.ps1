<#
.SYNOPSIS
    Publishes the service to .\publish with the win-x64 publish profile. Installs nothing.

.DESCRIPTION
    Separate from deploy so a build can be inspected before it is installed:
    .\wcs build, look at .\publish, then .\wcs deploy -From .\publish.
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'WindowsControlService.psm1') -Force
$paths = Get-WcsPaths

Write-WcsStep 'Building'

# Emptied first, so nothing from an earlier build can pass for part of this one.
if (Test-Path $paths.PublishPath) { Remove-Item $paths.PublishPath -Recurse -Force }

dotnet publish $paths.Project -p:PublishProfile=win-x64 --nologo --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)." }

# Checked because a publish that silently drops wwwroot still exits 0.
Assert-WcsArtifact -Path $paths.PublishPath

# UseStaticFiles reads no endpoints manifest: fifteen kilobytes that nothing ever opens.
Get-ChildItem $paths.PublishPath -Filter '*.staticwebassets.endpoints.json' | Remove-Item -Force

$exe = Get-Item (Join-Path $paths.PublishPath 'WindowsControlService.exe')
Write-WcsStep "$($exe.FullName) ($([Math]::Round($exe.Length / 1MB, 1)) MB)" -Level Ok
