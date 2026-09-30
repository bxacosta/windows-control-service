<#
.SYNOPSIS
    Runs the service from source with hot reload, on its own port and data directory.

.DESCRIPTION
    Uses the "dev" profile in Properties\launchSettings.json: http://localhost:5151, and the
    database and logs in src\WindowsControlService\.localdata (dotnet watch runs from the project
    directory). The installed service's port and database are never touched. Ctrl+C stops it.
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'WindowsControlService.psm1') -Force

# Restart instead of asking when an edit cannot be hot reloaded: the question blocks the console.
$env:DOTNET_WATCH_RESTART_ON_RUDE_EDIT = 'true'

dotnet watch --project (Get-WcsPaths).Project --launch-profile dev
exit $LASTEXITCODE
