#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Creates the restore point to take before applying a WDAC policy.

.DESCRIPTION
    For WDAC only: a policy built wrong can leave the machine refusing to run the very tools
    needed to undo it, and a restore point is the way back. A registry change does not need one;
    its recovery is one Set-ItemProperty.

    Windows refuses a second point within 24 hours of the last, silently: Checkpoint-Computer
    reports success anyway. So this compares the newest point of ours before and after.

.PARAMETER Force
    Lift the 24 hour limit for this one call. The original setting is put back in a finally.
#>
[CmdletBinding()]
param(
    [switch] $Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'WindowsControlService.psm1') -Force
$paths = Get-WcsPaths

if (-not (Test-WcsSystemProtection)) {
    throw 'System protection is off, so no restore point can exist. Turn it on with: Enable-ComputerRestore -Drive $env:SystemDrive'
}

$before = Get-WcsRestorePoint

$throttleKey = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore'
$throttleName = 'SystemRestorePointCreationFrequency'
# Absent by default, and under Set-StrictMode reading a missing property throws.
$settings = Get-ItemProperty $throttleKey -ErrorAction SilentlyContinue
$originalThrottle = if ($settings -and $settings.PSObject.Properties.Name -contains $throttleName) { $settings.$throttleName } else { $null }

Write-WcsStep 'Creating a restore point'
try {
    if ($Force) { Set-ItemProperty $throttleKey -Name $throttleName -Value 0 -Type DWord }
    Checkpoint-Computer -Description $paths.RestorePointName -RestorePointType 'MODIFY_SETTINGS'
}
finally {
    if ($Force) {
        if ($null -eq $originalThrottle) {
            Remove-ItemProperty $throttleKey -Name $throttleName -ErrorAction SilentlyContinue
        }
        else {
            Set-ItemProperty $throttleKey -Name $throttleName -Value $originalThrottle -Type DWord
        }
    }
}

$after = Get-WcsRestorePoint

if ($after -and (-not $before -or $after.SequenceNumber -ne $before.SequenceNumber)) {
    Write-WcsStep "created $($after.CreatedAt.ToString('yyyy-MM-dd HH:mm'))" -Level Ok
    exit 0
}

if ($before -and $before.Age.TotalHours -lt 24) {
    Write-WcsStep "Windows allows one a day. The existing one is from $(Format-WcsAge $before.Age) and is usually enough; -Force makes a new one." -Level Warn
    exit 0
}

throw 'No restore point was created. Check that the Volume Shadow Copy service is running.'
