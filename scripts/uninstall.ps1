#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Removes the service and everything it changed on this machine.

.DESCRIPTION
    The most important script in the repository, because a WDAC policy outlives the service
    that deployed it. If the policy is not removed, the machine is left refusing to run
    applications with nothing installed to explain why. The order below is not negotiable.

.PARAMETER RemoveData
    Also delete the data directory, which holds the password and the access history. Without
    this flag the script asks before touching it.

.PARAMETER Force
    Emergency cleanup: ask nothing, keep nothing, and sweep what a validation run may have left
    behind. Implies -RemoveData. This is the mode for a machine where something stopped half way
    -- an install that failed, a validation that crashed, a service deleted by hand with its
    policy still in force -- and it is idempotent: on a machine that never had the service it
    does nothing and says so.
#>
[CmdletBinding()]
param(
    [switch] $RemoveData,
    [switch] $Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Continue'

Import-Module (Join-Path $PSScriptRoot 'WindowsControlService.psm1') -Force
Assert-WcsAdministrator

$paths = Get-WcsPaths
$policyRemoved = $true

if ($Force) {
    $RemoveData = $true
}

# 1. Stop the service and really wait for it.
$service = Get-Service $paths.ServiceName -ErrorAction SilentlyContinue
if ($service) {
    Write-WcsStep 'Stopping the service'
    Stop-Service $paths.ServiceName -Force -ErrorAction SilentlyContinue

    if (Wait-WcsServiceStatus -Name $paths.ServiceName -Status Stopped) {
        Write-WcsStep 'stopped' -Level Ok
    }
    else {
        Write-WcsStep 'did not stop within 90 seconds; continuing' -Level Warn
    }
}

# 2. The WDAC policy, before anything else is removed. This is the step that matters.
Write-WcsStep 'Removing the WDAC policy'
$policyRemoved = Remove-WcsPolicy

if ($policyRemoved) {
    Write-WcsStep 'no policy of ours remains' -Level Ok
}
else {
    Write-WcsStep 'THE WDAC POLICY COULD NOT BE REMOVED.' -Level Fail
    Write-WcsStep 'Applications may stay blocked with nothing installed to explain it.' -Level Fail
    Write-WcsStep "Remove it by hand: CiTool.exe --remove-policy `"{$($paths.PolicyId)}`"" -Level Fail
}

# 3. Registry back to normal. 3 is Manual: USB drives mount again.
Write-WcsStep 'Restoring the USB storage settings'
Set-ItemProperty $paths.UsbStorKey -Name Start -Value 3 -ErrorAction SilentlyContinue
Remove-ItemProperty $paths.StoragePolicyKey -Name WriteProtect -ErrorAction SilentlyContinue
Write-WcsStep "USBSTOR Start = $(Get-WcsUsbStart)" -Level Ok

# 4. The service registration and its event source.
if ($service) {
    Write-WcsStep 'Deleting the service'
    sc.exe delete $paths.ServiceName | Out-Null
    Write-WcsStep 'deleted' -Level Ok
}

if ([System.Diagnostics.EventLog]::SourceExists($paths.ServiceName)) {
    [System.Diagnostics.EventLog]::DeleteEventSource($paths.ServiceName)
    Write-WcsStep 'event log source removed' -Level Ok
}

# 5. Binaries, and under -Force whatever a validation run left in TEMP. validate-blocking.ps1
#    clears its own working directories in a finally block; this covers the run that died before
#    reaching it.
if (Test-Path $paths.InstallPath) {
    Write-WcsStep 'Deleting the binaries'
    Remove-Item $paths.InstallPath -Recurse -Force -ErrorAction SilentlyContinue
    Write-WcsStep $paths.InstallPath -Level Ok
}

if ($Force) {
    Remove-Item (Join-Path $env:TEMP 'wcs-blocking-validation') -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $env:TEMP 'wcs-blocking-validation-data') -Recurse -Force -ErrorAction SilentlyContinue
}

# 6. Data, only when asked. The password and the whole access history live there.
if (Test-Path $paths.DataPath) {
    $shouldRemove = $RemoveData

    if (-not $RemoveData) {
        Write-WcsStep "The data directory holds the password and the access history: $($paths.DataPath)" -Level Warn
        $answer = Read-Host 'Delete it? (y/N)'
        $shouldRemove = $answer -eq 'y'
    }

    if ($shouldRemove) {
        Remove-Item $paths.DataPath -Recurse -Force -ErrorAction SilentlyContinue
        Write-WcsStep 'data directory deleted' -Level Ok
    }
    else {
        Write-WcsStep "data kept at $($paths.DataPath)" -Level Info
    }
}

# 7. The real final state, printed rather than assumed.
$finalPolicy = Get-WcsPolicyState

Write-WcsStep 'Final state'
[PSCustomObject]@{
    Service        = if (Get-Service $paths.ServiceName -ErrorAction SilentlyContinue) { 'PRESENT' } else { 'absent' }
    WdacPolicy     = if (-not $finalPolicy.Queried) { 'UNKNOWN' } elseif ($finalPolicy.Present) { 'PRESENT' } else { 'absent' }
    UsbStorStart   = Get-WcsUsbStart
    EventLogSource = if ([System.Diagnostics.EventLog]::SourceExists($paths.ServiceName)) { 'PRESENT' } else { 'absent' }
    InstallPath    = Test-Path $paths.InstallPath
    DataPath       = Test-Path $paths.DataPath
} | Format-List

if (-not $policyRemoved) {
    Write-Error 'Uninstall finished with the WDAC policy still installed. See the message above.'
    exit 1
}
