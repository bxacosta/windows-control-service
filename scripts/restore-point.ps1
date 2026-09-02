#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Creates the restore point the destructive validations expect, under a name status.ps1 can find.

.DESCRIPTION
    The net is for WDAC, and only for WDAC. A policy built wrong can leave this machine refusing
    to run applications -- including whatever you would reach for to undo it -- and no script in
    this repository can promise to talk its way out of that. A system restore point can.

    It is not the net for a registry change. UsbStorageSwitchWriteTests writes two DWORDs and
    puts them back in a finally block, and the recovery when that fails is one Set-ItemProperty,
    not a rollback of the whole machine. Creating a point before that is not caution, it is
    ceremony. Run this before validate-blocking.ps1 and before anything else that applies a
    policy; skip it for the registry tests.

    Windows refuses to create a second point within 24 hours of the last one, and it refuses
    silently. So this reads the newest point of ours before and after, and reports what actually
    happened rather than what was asked for.

.PARAMETER Force
    Lift the 24 hour throttle for this one call. The original setting is captured first and put
    back in a finally block, even when the checkpoint fails.
#>
[CmdletBinding()]
param(
    [switch] $Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'WindowsControlService.psm1') -Force
Assert-WcsAdministrator

$paths = Get-WcsPaths

if (-not (Test-WcsSystemProtection)) {
    throw @'
System protection is off on this machine, so no restore point can be created. Turn it on first:

    Enable-ComputerRestore -Drive $env:SystemDrive

Then run this again. Until then, do not run a validation that applies a WDAC policy.
'@
}

$before = Get-WcsRestorePoint
if ($before) {
    Write-WcsStep "Newest existing point: $($before.CreatedAt.ToString('yyyy-MM-dd HH:mm')), $([int]$before.Age.TotalHours) h ago" -Level Info
}

# The throttle, in minutes. Absent means the Windows default of 1440.
$throttleKey = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore'
$throttleName = 'SystemRestorePointCreationFrequency'
# Read through the property list rather than by name. The value is absent on a machine that has
# never had it set -- which is the default -- and under Set-StrictMode -Version Latest, reading a
# property that is not there is a terminating error, not a $null.
$throttleSettings = Get-ItemProperty $throttleKey -ErrorAction SilentlyContinue
$originalThrottle =
    if ($throttleSettings -and $throttleSettings.PSObject.Properties.Name -contains $throttleName) {
        $throttleSettings.$throttleName
    }
    else {
        $null
    }

try {
    if ($Force) {
        # Captured above and restored in the finally below, whatever happens here. This is the
        # one registry value this script writes, and it is Windows' own throttle, not anything
        # the service controls.
        Write-WcsStep 'Lifting the 24 hour throttle for this call'
        Set-ItemProperty $throttleKey -Name $throttleName -Value 0 -Type DWord
    }

    Write-WcsStep "Creating '$($paths.RestorePointName)'"
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

# Checkpoint-Computer reports success for a call Windows threw away, so the only honest check is
# whether a newer point of ours now exists.
$after = Get-WcsRestorePoint

$created = $after -and (-not $before -or $after.SequenceNumber -ne $before.SequenceNumber)

if ($created) {
    Write-WcsStep "created, sequence $($after.SequenceNumber), $($after.CreatedAt.ToString('yyyy-MM-dd HH:mm'))" -Level Ok
    exit 0
}

if ($before -and $before.Age.TotalHours -lt 24 -and -not $Force) {
    Write-WcsStep 'Windows refused: it will not create a second point within 24 hours of the last one.' -Level Warn
    Write-WcsStep "The existing one is $([int]$before.Age.TotalHours) h old and is probably good enough." -Level Info
    Write-WcsStep 'If you need a fresh one, run this again with -Force.' -Level Info
    exit 0
}

Write-Error 'No restore point was created, and the 24 hour throttle does not explain it. Check that the Volume Shadow Copy service is running.'
exit 1
