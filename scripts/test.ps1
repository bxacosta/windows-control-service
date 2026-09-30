<#
.SYNOPSIS
    Runs the tests: .NET and the interface rules under node --test.

.PARAMETER Fast
    Skips the tests that touch the machine (registry, CiTool, event log). Implied when not elevated,
    because those tests cannot pass without administrator rights.

.PARAMETER Filter
    Runs only the tests whose full name contains this text, a class or a single test.
#>
[CmdletBinding()]
param(
    [switch] $Fast,
    [string] $Filter
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'WindowsControlService.psm1') -Force
$paths = Get-WcsPaths

$conditions = @()
if ($Filter) { $conditions += "FullyQualifiedName~$Filter" }

if (-not $Fast -and -not (Test-WcsAdministrator)) {
    Write-WcsStep 'Not elevated: skipping the tests that touch the machine' -Level Warn
    $Fast = $true
}
if ($Fast) { $conditions += 'Requires!=Admin' }

$arguments = @('test', (Join-Path $paths.Root 'WindowsControlService.slnx'))
if ($conditions) { $arguments += '--filter', ($conditions -join '&') }

dotnet @arguments
exit $LASTEXITCODE
