#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Removes the service and everything it changed on this machine. Keeps the data unless asked.

.DESCRIPTION
    A WDAC policy outlives the service that deployed it: left behind, the machine keeps refusing
    applications with nothing installed to explain why. So the policy goes right after the service
    stops, and the order below does not change.

.PARAMETER RemoveData
    Also delete the data directory: the password and the whole history.

.PARAMETER Force
    Emergency cleanup for a machine left half way (a failed install, a crashed validation, a service
    deleted by hand with its policy in force). Implies -RemoveData and also clears what a validation
    run leaves in TEMP. Idempotent.
#>
[CmdletBinding()]
param(
    [switch] $RemoveData,
    [switch] $Force
)

Set-StrictMode -Version Latest
# Continue, not Stop: one step failing must not skip the ones after it.
$ErrorActionPreference = 'Continue'

Import-Module (Join-Path $PSScriptRoot 'WindowsControlService.psm1') -Force
$paths = Get-WcsPaths

if ($Force) { $RemoveData = $true }

$service = Get-Service $paths.ServiceName -ErrorAction SilentlyContinue
if ($service) {
    Write-WcsStep 'Stopping the service'
    Stop-Service $paths.ServiceName -Force -ErrorAction SilentlyContinue
    if (Wait-WcsServiceStatus -Name $paths.ServiceName -Status Stopped) {
        Write-WcsStep 'stopped' -Level Ok
    }
    else {
        Write-WcsStep 'did not stop within 90 seconds, continuing' -Level Warn
    }
}

Write-WcsStep 'Removing the WDAC policy'
$policyRemoved = Remove-WcsPolicy
if ($policyRemoved) {
    Write-WcsStep 'none of ours remains' -Level Ok
}
else {
    Write-WcsStep 'FAILED. Applications may stay blocked. Remove it by hand:' -Level Fail
    Write-WcsStep "CiTool.exe --remove-policy `"{$($paths.PolicyId)}`" -json" -Level Fail
}

Write-WcsStep 'Restoring USB storage'
Set-ItemProperty $paths.UsbStorKey -Name Start -Value 3 -ErrorAction SilentlyContinue
Remove-ItemProperty $paths.StoragePolicyKey -Name WriteProtect -ErrorAction SilentlyContinue
Write-WcsStep 'drives mount and are writable' -Level Ok

Write-WcsStep 'Removing the service'
if ($service) { sc.exe delete $paths.ServiceName | Out-Null }
if ([System.Diagnostics.EventLog]::SourceExists($paths.ServiceName)) {
    [System.Diagnostics.EventLog]::DeleteEventSource($paths.ServiceName)
}
Remove-Item $paths.InstallPath -Recurse -Force -ErrorAction SilentlyContinue
Write-WcsStep 'registration, event log source and binaries' -Level Ok

# validate.ps1 clears these in its own finally; this covers a run that died before reaching it.
if ($Force) {
    Remove-Item (Join-Path $env:TEMP 'wcs-blocking-validation*') -Recurse -Force -ErrorAction SilentlyContinue
}

if (Test-Path $paths.DataPath) {
    if ($RemoveData) {
        Remove-Item $paths.DataPath -Recurse -Force -ErrorAction SilentlyContinue
        Write-WcsStep 'Data deleted' -Level Ok
    }
    else {
        Write-WcsStep "Data kept in $($paths.DataPath) (-RemoveData deletes it)" -Level Info
    }
}

# The real final state, read back rather than assumed.
$serviceLeft = [bool](Get-Service $paths.ServiceName -ErrorAction SilentlyContinue)
$policy = Get-WcsPolicyState
$policyGone = $policy.Queried -and -not $policy.Present
$usbStart = Get-WcsUsbStart
$binariesLeft = Test-Path $paths.InstallPath

Write-Host ''
Write-WcsField 'Service' $(if ($serviceLeft) { 'still registered' } else { 'removed' }) $(if ($serviceLeft) { 'Fail' } else { 'Ok' })
Write-WcsField 'WDAC policy' (Format-WcsPolicyState $policy) $(if ($policyGone) { 'Ok' } else { 'Fail' })
Write-WcsField 'USBSTOR Start' "$usbStart" $(if ($usbStart -eq 3) { 'Ok' } else { 'Fail' })
Write-WcsField 'Binaries' $(if ($binariesLeft) { 'still present' } else { 'removed' }) $(if ($binariesLeft) { 'Warn' } else { 'Ok' })
Write-WcsField 'Data' $(if (Test-Path $paths.DataPath) { 'kept' } else { 'removed' })
Write-Host ''

if (-not $policyRemoved) { exit 1 }
