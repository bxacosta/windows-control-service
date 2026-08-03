<#
.SYNOPSIS
    Diagnostics at a glance. Works whether or not the service is installed.
#>
[CmdletBinding()]
param(
    [int] $LogLines = 10
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Continue'

Import-Module (Join-Path $PSScriptRoot 'WindowsControlService.psm1') -Force

$paths = Get-WcsPaths
$service = Get-Service $paths.ServiceName -ErrorAction SilentlyContinue

Write-WcsStep 'Service'
if ($service) {
    $service | Select-Object Name, Status, StartType | Format-Table -AutoSize | Out-String | Write-Host
}
else {
    Write-WcsStep 'not installed' -Level Info
}

Write-WcsStep 'Listening port'
$listening = Get-NetTCPConnection -LocalPort 5150 -State Listen -ErrorAction SilentlyContinue
if ($listening) {
    Write-WcsStep '5150 is listening' -Level Ok
}
else {
    Write-WcsStep '5150 is not listening' -Level Info
}

Write-WcsStep 'Health endpoint'
try {
    $health = Invoke-RestMethod $paths.HealthUrl -TimeoutSec 3
    Write-WcsStep "status=$($health.status) version=$($health.version) time=$($health.timestamp)" -Level Ok
}
catch {
    Write-WcsStep 'no answer' -Level Info
}

Write-WcsStep 'WDAC policy'
$policy = Get-WcsPolicyState
if (-not $policy.Queried) {
    # Not the same as "there is no policy": CiTool could not be asked. Distinguishing the two
    # is the whole reason the third state exists.
    Write-WcsStep 'could not be queried (Unknown)' -Level Warn
}
elseif ($policy.Present) {
    Write-WcsStep "installed, enforced=$($policy.Enforced)" -Level Ok
}
else {
    Write-WcsStep 'not installed' -Level Info
}

Write-WcsStep 'USB storage'
$start = Get-WcsUsbStart
switch ($start) {
    3       { Write-WcsStep 'USBSTOR Start = 3 (Manual, drives mount)' -Level Ok }
    4       { Write-WcsStep 'USBSTOR Start = 4 (Disabled, nothing mounts)' -Level Warn }
    default { Write-WcsStep "USBSTOR Start = $start (unexpected)" -Level Warn }
}

Write-WcsStep 'Database'
$database = Join-Path $paths.DataPath 'windows-control-service.db'
if (Test-Path $database) {
    $kb = [Math]::Round((Get-Item $database).Length / 1KB, 1)
    Write-WcsStep "$database ($kb KB)" -Level Ok
}
else {
    Write-WcsStep 'no database yet' -Level Info
}

Write-WcsStep "Last $LogLines log lines"
$log = Get-ChildItem (Join-Path $paths.LogPath '*.log') -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($log) {
    Get-Content $log.FullName -Tail $LogLines | ForEach-Object { Write-WcsStep $_ -Level Info }
}
else {
    Write-WcsStep 'no log files' -Level Info
}

Write-WcsStep 'Last event log entries'
# try/catch, not -ErrorAction: when the provider has never been registered Get-WinEvent
# reports "The parameter is incorrect" through a channel SilentlyContinue does not suppress.
$events = $null
try {
    $events = Get-WinEvent -FilterHashtable @{ LogName = 'Application'; ProviderName = $paths.ServiceName } -MaxEvents 5 -ErrorAction Stop
}
catch {
    $events = $null
}
if ($events) {
    $events | Select-Object TimeCreated, LevelDisplayName, @{ n = 'Message'; e = { ($_.Message -split "`r?`n")[0] } } |
        Format-Table -AutoSize | Out-String | Write-Host
}
else {
    Write-WcsStep 'none' -Level Info
}
