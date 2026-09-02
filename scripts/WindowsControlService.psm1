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

    # The default in ServiceConstants.DefaultUrl. Written once here and derived from, so
    # that a diagnostic cannot end up watching a different port from the one it curls.
    $port = 5150

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

        # The description every restore point this project creates carries, so that one made
        # for a validation can be told apart from Windows' own scheduled checkpoints.
        RestorePointName = 'WindowsControlService checkpoint'

        CiToolPath   = Join-Path $env:SystemRoot 'System32\CiTool.exe'
        UsbStorKey   = 'HKLM:\SYSTEM\CurrentControlSet\Services\USBSTOR'
        StoragePolicyKey = 'HKLM:\SYSTEM\CurrentControlSet\Control\StorageDevicePolicies'
        Port         = $port
        HealthUrl    = "http://localhost:$port/api/health"
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

function Assert-WcsArtifact {
    <#
    .SYNOPSIS
        Refuses a folder that is not a build of this service.

    .DESCRIPTION
        The interface is part of the artefact, not an extra. Checking only for the .exe is how a
        publish that silently dropped wwwroot becomes an installed service that answers the API
        and serves nothing -- build.ps1 already refuses to produce one, and this is what stops a
        folder that never came from build.ps1 being deployed instead.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][string] $Path)

    if (-not (Test-Path $Path)) {
        throw "'$Path' does not exist. Run .\scripts\build.ps1 first; the deploy scripts do not compile."
    }

    if (-not (Test-Path (Join-Path $Path 'WindowsControlService.exe'))) {
        throw "'$Path' contains no WindowsControlService.exe. Run .\scripts\build.ps1 first."
    }

    if (-not (Test-Path (Join-Path $Path 'wwwroot\index.html'))) {
        throw "'$Path' contains no wwwroot\index.html. That build would answer the API and serve no interface."
    }
}

function Wait-WcsHealth {
    <#
    .SYNOPSIS
        Waits for the installed service to actually answer, not merely to report Running.

    .DESCRIPTION
        The Service Control Manager says Running as soon as the process is up, which is before
        Kestrel is listening and before the migrations have finished. A deploy that stops at
        Running reports success for a service that cannot serve a request.
    #>
    [CmdletBinding()]
    param([int] $TimeoutSeconds = 30)

    $url = (Get-WcsPaths).HealthUrl
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)

    while ((Get-Date) -lt $deadline) {
        try {
            Invoke-WebRequest $url -UseBasicParsing -TimeoutSec 2 | Out-Null
            return $true
        }
        catch {
            Start-Sleep -Milliseconds 500
        }
    }

    return $false
}

function Test-WcsSystemProtection {
    <#
    .SYNOPSIS
        Whether System Protection is on, which decides whether a restore point can exist at all.

    .DESCRIPTION
        RPSessionInterval is 0 when protection is off. Asked rather than assumed: on a machine
        with it disabled, Checkpoint-Computer fails with a message about the service, and the
        useful thing to say is that protection is off and how to turn it on.
    #>
    [CmdletBinding()]
    param()

    $configuration = Get-ItemProperty `
        'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore' `
        -ErrorAction SilentlyContinue

    if (-not $configuration) { return $false }
    if ($configuration.PSObject.Properties.Name -contains 'DisableSR' -and $configuration.DisableSR -eq 1) { return $false }

    return $configuration.RPSessionInterval -ne 0
}

function Get-WcsRestorePoint {
    <#
    .SYNOPSIS
        The newest restore point this project created, or $null.

    .DESCRIPTION
        Matched on the description rather than on being the newest point of any kind: Windows
        makes its own before updates, and one of those is not evidence that anybody prepared for
        a validation.

        CreationTime comes back in WMI's own format, yyyyMMddHHmmss.ffffffsUUU, and is parsed by
        its fixed prefix. The alternative is ManagementDateTimeConverter, which drags in an
        assembly that is not loaded in PowerShell 7 by default.

        That trailing sUUU is the offset from UTC in minutes, and on restore points it is -000:
        the stamp is UTC, not local. Parsing it as local time and subtracting it from Get-Date
        gave "-5 h ago" on a machine five hours behind UTC -- an age in the future for a point
        created one second earlier. Both sides of the subtraction are UTC here, and only the
        value handed back for display is converted.
    #>
    [CmdletBinding()]
    param()

    $name = (Get-WcsPaths).RestorePointName

    $points = @(Get-CimInstance -Namespace root/default -ClassName SystemRestore -ErrorAction SilentlyContinue |
        Where-Object { $_.Description -eq $name })

    if (-not $points) { return $null }

    $asUtc = {
        [datetime]::SpecifyKind(
            [datetime]::ParseExact($args[0].Substring(0, 14), 'yyyyMMddHHmmss', $null),
            [DateTimeKind]::Utc)
    }

    $newest = $points |
        Sort-Object { & $asUtc $_.CreationTime } |
        Select-Object -Last 1

    $createdAtUtc = & $asUtc $newest.CreationTime

    [PSCustomObject]@{
        SequenceNumber = $newest.SequenceNumber
        CreatedAt      = $createdAtUtc.ToLocalTime()
        Age            = [datetime]::UtcNow - $createdAtUtc
    }
}

function Get-WcsUsbStart {
    [CmdletBinding()]
    param()

    (Get-ItemProperty (Get-WcsPaths).UsbStorKey -ErrorAction SilentlyContinue).Start
}

Export-ModuleMember -Function Get-WcsPaths, Write-WcsStep, Assert-WcsAdministrator,
    Assert-WcsArtifact, Wait-WcsServiceStatus, Wait-WcsHealth, Get-WcsPolicyState,
    Remove-WcsPolicy, Get-WcsUsbStart, Test-WcsSystemProtection, Get-WcsRestorePoint
