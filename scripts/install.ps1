#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Deploys an already published build and registers the Windows service.

.DESCRIPTION
    This script never compiles. Run build.ps1 first, look at what it produced, then install it.

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

if (-not (Test-Path $From)) {
    throw "'$From' does not exist. Run .\scripts\build.ps1 first; this script does not compile."
}

$source = Join-Path $From 'WindowsControlService.exe'
if (-not (Test-Path $source)) {
    throw "'$From' contains no WindowsControlService.exe. Run .\scripts\build.ps1 first."
}

if (Get-Service $paths.ServiceName -ErrorAction SilentlyContinue) {
    throw "$($paths.ServiceName) is already installed. Use update.ps1 to deploy over it, or uninstall.ps1 first."
}

Write-WcsStep 'Copying files'
New-Item -ItemType Directory -Force -Path $paths.InstallPath | Out-Null
New-Item -ItemType Directory -Force -Path $paths.DataPath | Out-Null
Copy-Item (Join-Path $From '*') $paths.InstallPath -Recurse -Force
Write-WcsStep "installed to $($paths.InstallPath)" -Level Ok

# Registering an event source needs administrator rights, which is why it belongs here and not
# in the service itself. Without it the application logs nothing to Event Viewer, and the
# symptom lies: "Service started successfully" keeps appearing because ServiceBase.AutoLog
# writes it by another route entirely.
Write-WcsStep 'Registering the event log source'
if (-not [System.Diagnostics.EventLog]::SourceExists($paths.ServiceName)) {
    [System.Diagnostics.EventLog]::CreateEventSource($paths.ServiceName, 'Application')
    Write-WcsStep 'created' -Level Ok
}
else {
    Write-WcsStep 'already present' -Level Info
}

Write-WcsStep 'Registering the service'

# New-Service rather than sc.exe create: it returns objects and fails detectably.
#
# No -Credential, so the service runs as LocalSystem. That is the highest privilege on this
# machine and it is deliberate: writing HKLM and driving CiTool need it. Stated here rather
# than left to be inferred from an absent parameter.
New-Service -Name $paths.ServiceName `
            -BinaryPathName "`"$($paths.ExePath)`"" `
            -DisplayName $paths.DisplayName `
            -Description $paths.Description `
            -StartupType Automatic | Out-Null

# No PowerShell equivalent exists for these two, so sc.exe stays.
sc.exe failure $paths.ServiceName reset= 86400 actions= restart/5000/restart/10000/restart/30000 | Out-Null
sc.exe failureflag $paths.ServiceName 1 | Out-Null
Write-WcsStep 'registered, automatic start, restart on failure' -Level Ok

Write-WcsStep 'Starting'
Start-Service $paths.ServiceName

if (-not (Wait-WcsServiceStatus -Name $paths.ServiceName -Status Running)) {
    throw "$($paths.ServiceName) did not reach Running. Check $($paths.LogPath)."
}

$healthy = $false
for ($attempt = 0; $attempt -lt 60; $attempt++) {
    try {
        Invoke-WebRequest $paths.HealthUrl -UseBasicParsing -TimeoutSec 2 | Out-Null
        $healthy = $true
        break
    }
    catch {
        Start-Sleep -Milliseconds 500
    }
}

if ($healthy) {
    Write-WcsStep "running, and $($paths.HealthUrl) answers" -Level Ok
}
else {
    Write-WcsStep "running, but $($paths.HealthUrl) did not answer. Check $($paths.LogPath)." -Level Warn
}

Write-WcsStep 'Done. Configure a password with POST /api/auth/password before anything else.' -Level Info
