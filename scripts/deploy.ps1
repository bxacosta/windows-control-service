#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Builds, then installs the service or updates the installed one. The data is never touched.

.DESCRIPTION
    An update backs the database up first, to backups\ in the data directory, keeping the last
    three.

.PARAMETER From
    Deploy this already published folder instead of building. For a build inspected first.
#>
[CmdletBinding()]
param(
    [string] $From
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'WindowsControlService.psm1') -Force
$paths = Get-WcsPaths
$backupsToKeep = 3

if (-not $From) {
    & (Join-Path $PSScriptRoot 'build.ps1')
    $From = $paths.PublishPath
}
Assert-WcsArtifact -Path $From

$installed = [bool](Get-Service $paths.ServiceName -ErrorAction SilentlyContinue)

if ($installed) {
    Write-WcsStep 'Updating the installed service'

    Stop-Service $paths.ServiceName -Force -ErrorAction SilentlyContinue
    if (-not (Wait-WcsServiceStatus -Name $paths.ServiceName -Status Stopped)) {
        throw 'The service did not stop within 90 seconds. Nothing was replaced.'
    }
    Write-WcsStep 'stopped' -Level Ok

    # The new build migrates the database when it starts, and migrations only go forward. A copy
    # taken while the service is stopped is the way back from one that goes wrong. The -wal and
    # -shm files go with it: a committed write can still be only in the -wal.
    if (Test-Path $paths.DatabasePath) {
        $backup = Join-Path $paths.BackupPath (Get-Date -Format 'yyyy-MM-dd_HHmmss')
        New-Item -ItemType Directory -Force -Path $backup | Out-Null
        Copy-Item "$($paths.DatabasePath)*" $backup

        Get-ChildItem $paths.BackupPath -Directory | Sort-Object Name -Descending |
            Select-Object -Skip $backupsToKeep | Remove-Item -Recurse -Force
        Write-WcsStep "database backed up to $backup" -Level Ok
    }

    # Retried rather than slept: the SCM reports Stopped before the process has exited, and
    # e_sqlite3.dll stays locked for a moment after ("Access to the path is denied", then fine a
    # second later). A handle that is really stuck still fails, and names the file.
    $deadline = (Get-Date).AddSeconds(30)
    while ($true) {
        try {
            Get-ChildItem $paths.InstallPath -Force -ErrorAction SilentlyContinue |
                Remove-Item -Recurse -Force -ErrorAction Stop
            break
        }
        catch {
            if ((Get-Date) -ge $deadline) { throw "Could not replace the installed files: $($_.Exception.Message)" }
            Start-Sleep -Milliseconds 500
        }
    }
}
else {
    Write-WcsStep 'Installing the service'
}

New-Item -ItemType Directory -Force -Path $paths.InstallPath, $paths.DataPath | Out-Null
Copy-Item (Join-Path $From '*') $paths.InstallPath -Recurse -Force
Write-WcsStep "files in $($paths.InstallPath), data in $($paths.DataPath)" -Level Ok

if (-not $installed) {
    # Needs administrator rights, so it cannot be left to the service. Without it nothing reaches
    # Event Viewer, and "Service started successfully" still appears because ServiceBase.AutoLog
    # writes it by another route, which hides the problem.
    if (-not [System.Diagnostics.EventLog]::SourceExists($paths.ServiceName)) {
        [System.Diagnostics.EventLog]::CreateEventSource($paths.ServiceName, 'Application')
    }

    # No -Credential: the service runs as LocalSystem, deliberately. Writing HKLM and driving
    # CiTool need it.
    New-Service -Name $paths.ServiceName `
                -BinaryPathName "`"$($paths.ExePath)`"" `
                -DisplayName $paths.DisplayName `
                -Description $paths.Description `
                -StartupType Automatic | Out-Null

    # Restart on failure has no PowerShell equivalent.
    sc.exe failure $paths.ServiceName reset= 86400 actions= restart/5000/restart/10000/restart/30000 | Out-Null
    sc.exe failureflag $paths.ServiceName 1 | Out-Null
    Write-WcsStep 'registered: automatic start, restarts on failure, event log source' -Level Ok
}

Start-Service $paths.ServiceName
if (-not (Wait-WcsServiceStatus -Name $paths.ServiceName -Status Running)) {
    throw "The service did not start. See $($paths.LogPath)."
}
if (-not (Wait-WcsHealth)) {
    throw "The service is running but $($paths.HealthUrl) does not answer. See $($paths.LogPath)."
}

$version = (Invoke-RestMethod $paths.HealthUrl).version
Write-WcsStep "running $version on $($paths.Url)" -Level Ok

if (-not (Invoke-RestMethod "$($paths.Url)/api/auth/session").initialized) {
    Write-WcsStep "Next: open $($paths.Url) and set the password. Until then anyone on this machine can." -Level Warn
}
