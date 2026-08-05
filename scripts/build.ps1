<#
.SYNOPSIS
    Publishes the service to .\publish as a single self-contained executable.

.DESCRIPTION
    Building and deploying are separate on purpose. An install script that also compiles makes
    it impossible to deploy a binary that was built earlier and inspected before installing it.
    This script only builds; install.ps1 only installs.

.PARAMETER Output
    Where to publish. Defaults to .\publish at the repository root.
#>
[CmdletBinding()]
param(
    [string] $Output
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'WindowsControlService.psm1') -Force

$repositoryRoot = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repositoryRoot 'src\WindowsControlService\WindowsControlService.csproj'
if (-not $Output) { $Output = Join-Path $repositoryRoot 'publish' }

Write-WcsStep "Publishing $project"

if (Test-Path $Output) {
    Remove-Item $Output -Recurse -Force
}

# SelfContained is passed here rather than set in the .csproj: a self-contained project cannot
# be referenced by the test projects (NETSDK1150).
#
# PublishTrimmed is deliberately off, and the reason was measured, not guessed: publishing with
# it on fails outright with IL2026 on ValidateDataAnnotations, MaxLength and MinLength, because
# DataAnnotations resolves members by reflection. It is NOT Microsoft.Data.Sqlite, which is the
# usual suspect. Suppressing those warnings would trade options validation that might quietly
# stop validating for a smaller binary, on a service installed on one machine. If it is ever
# turned on, the published binary has to be run and checked against a real database, a real
# policy and a real event log before the change is kept.
dotnet publish $project `
    --configuration Release `
    --runtime win-x64 `
    --self-contained `
    -p:PublishSingleFile=true `
    -p:PublishTrimmed=false `
    --output $Output

$exe = Join-Path $Output 'WindowsControlService.exe'
if (-not (Test-Path $exe)) {
    throw "Publish finished but $exe is missing."
}

$sizeMb = [Math]::Round((Get-Item $exe).Length / 1MB, 1)
$totalMb = [Math]::Round(((Get-ChildItem $Output -Recurse -File | Measure-Object Length -Sum).Sum) / 1MB, 1)

Write-WcsStep 'Published' -Level Ok
Write-WcsStep "executable : $exe" -Level Info
Write-WcsStep "size       : $sizeMb MB (output folder $totalMb MB)" -Level Info
Write-WcsStep "next       : .\scripts\install.ps1 -From `"$Output`"" -Level Info
