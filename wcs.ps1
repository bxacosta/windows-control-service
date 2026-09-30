<#
.SYNOPSIS
    Develop, build, deploy and operate Windows Control Service. Run .\wcs with no command for help.

.DESCRIPTION
    Every command is one script in scripts\, and the options after the command are passed to it
    unchanged. Commands that change the machine ask for administrator rights when they lack them.

.EXAMPLE
    .\wcs dev

.EXAMPLE
    .\wcs deploy
#>

# No [CmdletBinding()]: an advanced script would collect the options as plain strings, and
# "-RemoveData" would reach uninstall.ps1 as a value instead of a switch. The automatic $args keeps
# them as parameters when splatted.
param(
    [ValidateSet('dev', 'test', 'build', 'deploy', 'status', 'uninstall', 'restore-point', 'validate', 'help')]
    [string] $Command = 'help'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$commands = [ordered]@{
    'dev'           = @{ Admin = $false; Usage = 'Run from source with hot reload on http://localhost:5151' }
    'test'          = @{ Admin = $false; Usage = 'Run the tests                        [-Fast] [-Filter <name>]' }
    'build'         = @{ Admin = $false; Usage = 'Publish to .\publish' }
    'deploy'        = @{ Admin = $true;  Usage = 'Build, then install or update        [-From <folder>]' }
    'status'        = @{ Admin = $false; Usage = 'Service, health, policy, USB, data   [-Logs <lines>]' }
    'uninstall'     = @{ Admin = $true;  Usage = 'Remove the service and its policy    [-RemoveData] [-Force]' }
    'restore-point' = @{ Admin = $true;  Usage = 'Create a restore point               [-Force]' }
    'validate'      = @{ Admin = $true;  Usage = 'Prove WDAC blocking on a test executable (applies a real policy)' }
}

if ($Command -eq 'help') {
    Write-Host 'Usage: .\wcs <command> [options]'
    Write-Host ''
    foreach ($name in $commands.Keys) {
        $marker = if ($commands[$name].Admin) { '*' } else { ' ' }
        Write-Host ('  {0,-15}{1} {2}' -f $name, $marker, $commands[$name].Usage)
    }
    Write-Host ''
    Write-Host '  * needs administrator rights, asked for when missing'
    return
}

Import-Module (Join-Path $PSScriptRoot 'scripts\WindowsControlService.psm1') -Force

if ($commands[$Command].Admin -and -not (Test-WcsAdministrator)) {
    # Windows 11's sudo keeps the output in this terminal. Without it, a new elevated window is
    # the only route, and -NoExit keeps it open so what happened can still be read.
    $shell = (Get-Process -Id $PID).Path
    if (Get-Command sudo.exe -ErrorAction SilentlyContinue) {
        sudo.exe $shell -NoProfile -File $PSCommandPath $Command @args
        exit $LASTEXITCODE
    }

    $arguments = @('-NoProfile', '-NoExit', '-File', "`"$PSCommandPath`"", $Command) + $args
    Start-Process $shell -Verb RunAs -ArgumentList $arguments
    Write-Host "Continuing in a new administrator window."
    return
}

& (Join-Path $PSScriptRoot "scripts\$Command.ps1") @args
exit $LASTEXITCODE
