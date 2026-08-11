#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Deploys a new build over an existing installation, keeping the database, the password and
    the history.

.PARAMETER From
    Folder produced by build.ps1. Required.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $From
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'WindowsControlService.psm1') -Force
Assert-WcsAdministrator

$paths = Get-WcsPaths

if (-not (Test-Path (Join-Path $From 'WindowsControlService.exe'))) {
    throw "'$From' contains no WindowsControlService.exe. Run .\scripts\build.ps1 first."
}

if (-not (Get-Service $paths.ServiceName -ErrorAction SilentlyContinue)) {
    throw "$($paths.ServiceName) is not installed. Use install.ps1."
}

Write-WcsStep 'Stopping the service'
Stop-Service $paths.ServiceName -Force -ErrorAction SilentlyContinue

# A real wait, not a sleep. An executable still in use cannot be overwritten, and the failure
# surfaces as a Copy-Item error that never mentions the service.
if (-not (Wait-WcsServiceStatus -Name $paths.ServiceName -Status Stopped)) {
    throw "$($paths.ServiceName) did not stop within 90 seconds. Not overwriting a running binary."
}
Write-WcsStep 'stopped' -Level Ok

Write-WcsStep 'Replacing the binaries'

# Only the install directory is touched. The data directory holds the password and the access
# history, and an update must never be the thing that loses them.
#
# Retried, because the Service Control Manager reports Stopped before the process has finished
# exiting, and a native library it loaded stays locked for a moment after. Measured here: the
# delete of e_sqlite3.dll failed with "Access to the path is denied" and succeeded a second
# later, leaving the install half replaced and the service down. A retry rather than a fixed
# sleep, so a handle that is genuinely stuck still fails, and says which file.
$deadline = (Get-Date).AddSeconds(30)
while ($true) {
    try {
        Get-ChildItem $paths.InstallPath -Force -ErrorAction SilentlyContinue |
            Remove-Item -Recurse -Force -ErrorAction Stop
        break
    }
    catch {
        if ((Get-Date) -ge $deadline) {
            throw "Could not replace the installed files after 30 seconds: $($_.Exception.Message)"
        }

        Start-Sleep -Milliseconds 500
    }
}

Copy-Item (Join-Path $From '*') $paths.InstallPath -Recurse -Force
Write-WcsStep "data left untouched at $($paths.DataPath)" -Level Ok

Write-WcsStep 'Starting'
Start-Service $paths.ServiceName

if (-not (Wait-WcsServiceStatus -Name $paths.ServiceName -Status Running)) {
    throw "$($paths.ServiceName) did not reach Running after the update. Check $($paths.LogPath)."
}

Write-WcsStep 'updated and running' -Level Ok
