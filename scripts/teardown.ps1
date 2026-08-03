#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Emergency cleanup. Leaves the machine with no trace of WindowsControlService.

.DESCRIPTION
    Idempotent: running it twice is not an error, and running it on a machine that never had
    the service is a no-op. Unlike uninstall.ps1 it asks nothing and keeps nothing -- it exists
    so that no validation can ever leave a WDAC policy behind, and a stranded policy blocks
    applications with nothing on the machine left to explain why.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Continue'

Import-Module (Join-Path $PSScriptRoot 'WindowsControlService.psm1') -Force

$paths = Get-WcsPaths

# 1. Service. A real wait, not a sleep: ShutdownTimeout is 70 seconds because a WDAC
#    operation can take that long.
$service = Get-Service $paths.ServiceName -ErrorAction SilentlyContinue
if ($service) {
    Stop-Service $paths.ServiceName -Force -ErrorAction SilentlyContinue
    Wait-WcsServiceStatus -Name $paths.ServiceName -Status Stopped | Out-Null
    sc.exe delete $paths.ServiceName | Out-Null
}

# 2. WDAC policy. Asks first, and gives CiTool EOF; see the module for why both matter.
$policyRemoved = Remove-WcsPolicy

# 3. Registry. 3 is Manual, the normal state in which USB drives mount.
Set-ItemProperty $paths.UsbStorKey -Name Start -Value 3 -ErrorAction SilentlyContinue
Remove-ItemProperty $paths.StoragePolicyKey -Name WriteProtect -ErrorAction SilentlyContinue

# 4. Event log source, registered by the installer. Phase 0 did not know about this one: this
#    machine still carried a source left behind by an earlier installation.
if ([System.Diagnostics.EventLog]::SourceExists($paths.ServiceName)) {
    [System.Diagnostics.EventLog]::DeleteEventSource($paths.ServiceName)
}

# 5. Files, including the test target a WDAC validation may have built.
Remove-Item $paths.InstallPath -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $paths.DataPath    -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item "$env:TEMP\wcs-test-target" -Recurse -Force -ErrorAction SilentlyContinue

# 6. Verification. Prints the real state; assumes nothing.
$finalPolicy = Get-WcsPolicyState

[PSCustomObject]@{
    Service        = if (Get-Service $paths.ServiceName -ErrorAction SilentlyContinue) { 'PRESENT' } else { 'absent' }
    WdacPolicy     = if (-not $finalPolicy.Queried) { 'UNKNOWN' } elseif ($finalPolicy.Present) { 'PRESENT' } else { 'absent' }
    UsbStorStart   = Get-WcsUsbStart
    EventLogSource = if ([System.Diagnostics.EventLog]::SourceExists($paths.ServiceName)) { 'PRESENT' } else { 'absent' }
    InstallPath    = Test-Path $paths.InstallPath
    DataPath       = Test-Path $paths.DataPath
} | Format-List

if (-not $policyRemoved) {
    Write-Error "The WDAC policy is still installed. Remove it by hand: CiTool.exe --remove-policy `"{$($paths.PolicyId)}`""
}
