<#
.SYNOPSIS
    The state of the service and of everything it touches. Works installed or not.

.PARAMETER Logs
    Also show this many of the latest log lines, and the latest Event Viewer entries.
#>
[CmdletBinding()]
param(
    [int] $Logs = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Continue'

Import-Module (Join-Path $PSScriptRoot 'WindowsControlService.psm1') -Force
$paths = Get-WcsPaths

Write-Host ''

$service = Get-Service $paths.ServiceName -ErrorAction SilentlyContinue
if ($service) {
    $level = if ($service.Status -eq 'Running') { 'Ok' } else { 'Warn' }
    Write-WcsField 'Service' "$($service.Status), $($service.StartType.ToString().ToLower()) start" $level
}
else {
    Write-WcsField 'Service' 'not installed'
}

try {
    $health = Invoke-RestMethod $paths.HealthUrl -TimeoutSec 3
    Write-WcsField 'Health' "$($paths.Url) answers" 'Ok'

    # The version carries the commit it was built from, so an out of date install is visible here.
    $built = ($health.version -split '\+')[-1]
    $head = git -C $paths.Root rev-parse HEAD 2>$null
    if ($head -and $built -ne $head) {
        Write-WcsField 'Version' "$($built.Substring(0, 7)), behind HEAD $($head.Substring(0, 7)): .\wcs deploy" 'Warn'
    }
    else {
        Write-WcsField 'Version' $health.version 'Ok'
    }
}
catch {
    $level = if ($service) { 'Fail' } else { 'Info' }
    Write-WcsField 'Health' "$($paths.Url) does not answer" $level
}

$policy = Get-WcsPolicyState
$level = if (-not $policy.Queried) { 'Warn' } elseif ($policy.Present) { 'Ok' } else { 'Info' }
Write-WcsField 'WDAC policy' (Format-WcsPolicyState $policy) $level

switch (Get-WcsUsbStart) {
    3       { Write-WcsField 'USB storage' 'allowed (USBSTOR Start = 3)' 'Ok' }
    4       { Write-WcsField 'USB storage' 'blocked (USBSTOR Start = 4)' 'Warn' }
    default { Write-WcsField 'USB storage' "unexpected USBSTOR Start = $_" 'Warn' }
}

# Only ours count: a Windows Update checkpoint is not evidence that anybody prepared.
if (-not (Test-WcsSystemProtection)) {
    Write-WcsField 'Restore point' 'impossible, system protection is off' 'Warn'
}
elseif ($point = Get-WcsRestorePoint) {
    Write-WcsField 'Restore point' "$($point.CreatedAt.ToString('yyyy-MM-dd HH:mm')) ($(Format-WcsAge $point.Age))" 'Ok'
}
else {
    Write-WcsField 'Restore point' 'none, create one before applying a policy: .\wcs restore-point'
}

if (Test-Path $paths.DatabasePath) {
    $kb = [Math]::Round((Get-Item $paths.DatabasePath).Length / 1KB)
    Write-WcsField 'Database' "$($paths.DatabasePath) ($kb KB)"
}
else {
    Write-WcsField 'Database' 'none yet'
}

$log = Get-ChildItem (Join-Path $paths.LogPath '*.log') -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
Write-WcsField 'Log' $(if ($log) { $log.FullName } else { 'none yet' })

Write-Host ''

if ($Logs -gt 0) {
    if ($log) {
        Write-WcsStep "Last $Logs log lines"
        Get-Content $log.FullName -Tail $Logs | ForEach-Object { Write-Host "    $_" }
    }

    # try/catch because, when the source has never been registered, Get-WinEvent reports "The
    # parameter is incorrect" through a channel -ErrorAction SilentlyContinue does not suppress.
    Write-WcsStep 'Last Event Viewer entries'
    try {
        Get-WinEvent -FilterHashtable @{ LogName = 'Application'; ProviderName = $paths.ServiceName } -MaxEvents 5 -ErrorAction Stop |
            ForEach-Object { Write-Host "    $($_.TimeCreated.ToString('yyyy-MM-dd HH:mm'))  $($_.LevelDisplayName)  $(($_.Message -split "`r?`n")[0])" }
    }
    catch {
        Write-WcsStep 'none' -Level Info
    }
}
