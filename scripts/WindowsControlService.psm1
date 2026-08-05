<#
.SYNOPSIS
    Shared helpers for every WindowsControlService script.

.DESCRIPTION
    One place decides where the service lives, what it is called and how it is waited on.
    Without this, each script redeclares the paths and the service name, and the day two of
    them disagree is the day an uninstall leaves half the machine behind.
#>

Set-StrictMode -Version Latest

function Get-WcsPaths {
    <#
    .SYNOPSIS
        Every path and identifier the scripts need, decided once.
    #>
    [CmdletBinding()]
    param()

    $installPath = Join-Path $env:ProgramFiles 'WindowsControlService'

    [PSCustomObject]@{
        ServiceName  = 'WindowsControlService'
        DisplayName  = 'Windows Control Service'
        Description  = 'Controls application execution and device access on this computer'
        InstallPath  = $installPath
        ExePath      = Join-Path $installPath 'WindowsControlService.exe'
        DataPath     = Join-Path $env:ProgramData 'WindowsControlService'
        LogPath      = Join-Path (Join-Path $env:ProgramData 'WindowsControlService') 'logs'

        # Must match WdacPolicyDocument.PolicyId. Deliberately different from the
        # A1B2C3D4-... policy an earlier installation left on this machine, so a leftover is
        # never mistaken for ours.
        PolicyId     = '9E9BB70B-2BD8-4EE9-9031-30476FCF1FF3'

        CiToolPath   = Join-Path $env:SystemRoot 'System32\CiTool.exe'
        UsbStorKey   = 'HKLM:\SYSTEM\CurrentControlSet\Services\USBSTOR'
        StoragePolicyKey = 'HKLM:\SYSTEM\CurrentControlSet\Control\StorageDevicePolicies'
        HealthUrl    = 'http://localhost:5150/api/health'
    }
}

function Write-WcsStep {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $Message,
        [ValidateSet('Step', 'Ok', 'Warn', 'Fail', 'Info')][string] $Level = 'Step'
    )

    switch ($Level) {
        'Step' { Write-Host "==> $Message" -ForegroundColor Cyan }
        'Ok'   { Write-Host "    $Message" -ForegroundColor Green }
        'Warn' { Write-Host "    $Message" -ForegroundColor Yellow }
        'Fail' { Write-Host "    $Message" -ForegroundColor Red }
        'Info' { Write-Host "    $Message" }
    }
}

function Assert-WcsAdministrator {
    <#
    .SYNOPSIS
        Fails early and clearly. The service runs as LocalSystem and touches the registry and
        CiTool, so every one of these scripts needs elevation.
    #>
    [CmdletBinding()]
    param()

    $identity  = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)

    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'This script needs an elevated PowerShell session. Right click, Run as administrator.'
    }
}

function Wait-WcsServiceStatus {
    <#
    .SYNOPSIS
        Waits for a real status change instead of sleeping.

    .DESCRIPTION
        ShutdownTimeout is 70 seconds because a WDAC operation can take that long, so
        Start-Sleep -Seconds 2 is a race, not a wait. Its symptom is a Copy-Item failing on an
        executable still in use, with an error that mentions none of this.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $Name,
        [Parameter(Mandatory)][ValidateSet('Running', 'Stopped')][string] $Status,
        [int] $TimeoutSeconds = 90
    )

    $service = Get-Service $Name -ErrorAction SilentlyContinue
    if (-not $service) { return $false }

    try {
        $service.WaitForStatus($Status, [TimeSpan]::FromSeconds($TimeoutSeconds))
        return $true
    }
    catch [System.ServiceProcess.TimeoutException] {
        return $false
    }
}

function Get-WcsPolicyState {
    <#
    .SYNOPSIS
        Asks CiTool whether our policy is installed and enforced.

    .DESCRIPTION
        Failure is reported as Queried = $false, never as "no policy". CiTool signals errors
        with well formed JSON that simply has no Policies array, and reading that as "nothing
        installed" is how a guard ends up reinstalling a policy forever.
    #>
    [CmdletBinding()]
    param()

    $paths = Get-WcsPaths

    if (-not (Test-Path $paths.CiToolPath)) {
        return [PSCustomObject]@{ Queried = $false; Present = $false; Enforced = $false }
    }

    $raw = & $paths.CiToolPath --list-policies -json 2>$null
    $parsed = $raw | ConvertFrom-Json -ErrorAction SilentlyContinue

    if (-not $parsed -or -not ($parsed.PSObject.Properties.Name -contains 'Policies')) {
        return [PSCustomObject]@{ Queried = $false; Present = $false; Enforced = $false }
    }

    $ours = $parsed.Policies | Where-Object { ($_.PolicyID -replace '[{}]', '') -eq $paths.PolicyId }

    [PSCustomObject]@{
        Queried  = $true
        Present  = [bool]$ours
        Enforced = [bool]($ours -and $ours.IsEnforced)
    }
}

function Remove-WcsPolicy {
    <#
    .SYNOPSIS
        Removes our WDAC policy if it is installed. Returns $true when nothing is left.

    .DESCRIPTION
        Asks first, because --remove-policy errors when the policy is absent, and because
        without -json CiTool prints "Press Enter to Continue" and waits on a stdin nobody is
        watching. Routing through cmd with <nul is the only reliable way to give it EOF from
        PowerShell. A hung uninstall is worse than a failed one: it leaves the machine half
        done without saying so.
    #>
    [CmdletBinding()]
    param()

    $paths = Get-WcsPaths
    $state = Get-WcsPolicyState

    if (-not $state.Queried) { return $false }
    if (-not $state.Present) { return $true }

    cmd.exe /c "`"$($paths.CiToolPath)`" --remove-policy `"{$($paths.PolicyId)}`" -json <nul" | Out-Null

    return -not (Get-WcsPolicyState).Present
}

function Get-WcsUsbStart {
    [CmdletBinding()]
    param()

    (Get-ItemProperty (Get-WcsPaths).UsbStorKey -ErrorAction SilentlyContinue).Start
}

Export-ModuleMember -Function Get-WcsPaths, Write-WcsStep, Assert-WcsAdministrator,
    Wait-WcsServiceStatus, Get-WcsPolicyState, Remove-WcsPolicy, Get-WcsUsbStart
