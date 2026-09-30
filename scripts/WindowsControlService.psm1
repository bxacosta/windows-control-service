<#
.SYNOPSIS
    Shared helpers for the wcs commands: paths, output, waits, and the machine state they check.

.DESCRIPTION
    One place decides where the service lives and what it is called. The day two scripts disagree
    on a path is the day an uninstall leaves half the machine behind.
#>

Set-StrictMode -Version Latest

function Get-WcsPaths {
    [CmdletBinding()]
    param()

    $root = Split-Path $PSScriptRoot -Parent
    $installPath = Join-Path $env:ProgramFiles 'WindowsControlService'
    $dataPath = Join-Path $env:ProgramData 'WindowsControlService'

    # ServiceConstants.DefaultUrl. Derived from here, so a diagnostic cannot watch one port and
    # curl another.
    $port = 5150

    [PSCustomObject]@{
        Root             = $root
        Project          = Join-Path $root 'src\WindowsControlService'
        PublishPath      = Join-Path $root 'publish'
        ServiceName      = 'WindowsControlService'
        DisplayName      = 'Windows Control Service'
        Description      = 'Controls application execution and device access on this computer'
        InstallPath      = $installPath
        ExePath          = Join-Path $installPath 'WindowsControlService.exe'
        DataPath         = $dataPath
        DatabasePath     = Join-Path $dataPath 'windows-control-service.db'
        BackupPath       = Join-Path $dataPath 'backups'
        LogPath          = Join-Path $dataPath 'logs'

        # Must match WdacPolicyDocument.PolicyId. Deliberately not the A1B2C3D4-... policy an
        # earlier installation left on this machine, so a leftover is never taken for ours.
        PolicyId         = '9E9BB70B-2BD8-4EE9-9031-30476FCF1FF3'

        # Tells our restore points apart from the ones Windows makes before its own updates.
        RestorePointName = 'WindowsControlService checkpoint'

        CiToolPath       = Join-Path $env:SystemRoot 'System32\CiTool.exe'
        UsbStorKey       = 'HKLM:\SYSTEM\CurrentControlSet\Services\USBSTOR'
        StoragePolicyKey = 'HKLM:\SYSTEM\CurrentControlSet\Control\StorageDevicePolicies'
        Port             = $port
        Url              = "http://localhost:$port"
        HealthUrl        = "http://localhost:$port/api/health"
    }
}

# --- Output -----------------------------------------------------------------------------------

function Write-WcsStep {
    <#
    .SYNOPSIS
        A heading ("==> Building") or, with -Level, one indented line under it.
    #>
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

function Write-WcsField {
    <#
    .SYNOPSIS
        One "label  value" line of a report, the value coloured by its level.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $Label,
        [Parameter(Mandatory)][AllowEmptyString()][string] $Value,
        [ValidateSet('Ok', 'Warn', 'Fail', 'Info')][string] $Level = 'Info'
    )

    $color = @{ Ok = 'Green'; Warn = 'Yellow'; Fail = 'Red'; Info = 'Gray' }[$Level]
    Write-Host ('  {0,-14}' -f $Label) -NoNewline
    Write-Host $Value -ForegroundColor $color
}

# --- Checks -----------------------------------------------------------------------------------

function Test-WcsAdministrator {
    [CmdletBinding()]
    param()

    $principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
    $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Assert-WcsAdministrator {
    [CmdletBinding()]
    param()

    if (-not (Test-WcsAdministrator)) {
        throw 'This needs administrator rights. Run it from an elevated terminal, or through .\wcs, which asks for them.'
    }
}

function Assert-WcsDotnetSdk {
    <#
    .SYNOPSIS
        Fails with the install command when the SDK that global.json asks for is missing.

    .DESCRIPTION
        Without it the first dotnet call fails with PowerShell's "not recognized", or with a
        global.json resolution error, and neither says what to install.
    #>
    [CmdletBinding()]
    param()

    $install = 'Install it with: winget install Microsoft.DotNet.SDK.10'

    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw "The .NET SDK is not installed. $install"
    }

    # Run from the repository root so global.json decides which SDK counts.
    Push-Location (Get-WcsPaths).Root
    try { dotnet --version *> $null } finally { Pop-Location }
    if ($LASTEXITCODE -ne 0) {
        throw "No .NET SDK matching global.json (10.0.1xx) is installed. $install"
    }
}

function Assert-WcsArtifact {
    <#
    .SYNOPSIS
        Refuses a folder that is not a complete build of this service.

    .DESCRIPTION
        The interface is part of the build. A folder with the .exe and no wwwroot installs a
        service that answers the API and serves nothing.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][string] $Path)

    foreach ($file in 'WindowsControlService.exe', 'wwwroot\index.html') {
        if (-not (Test-Path (Join-Path $Path $file))) {
            throw "'$Path' is not a complete build: $file is missing. Run .\wcs build."
        }
    }
}

# --- Waits ------------------------------------------------------------------------------------

function Wait-WcsServiceStatus {
    <#
    .SYNOPSIS
        Waits for a real status change instead of sleeping.

    .DESCRIPTION
        ShutdownTimeout is 70 seconds because a WDAC operation can take that long. A fixed sleep
        is a race whose symptom is Copy-Item failing on an executable still in use.
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

function Wait-WcsHealth {
    <#
    .SYNOPSIS
        Waits until GET /api/health answers.

    .DESCRIPTION
        Running is not serving: the Service Control Manager reports Running as soon as the process
        is up, before Kestrel listens and before the migrations have run.
    #>
    [CmdletBinding()]
    param(
        [string] $Url = (Get-WcsPaths).HealthUrl,
        [int] $TimeoutSeconds = 30
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            Invoke-WebRequest $Url -UseBasicParsing -TimeoutSec 2 | Out-Null
            return $true
        }
        catch {
            Start-Sleep -Milliseconds 500
        }
    }

    return $false
}

# --- Machine state ----------------------------------------------------------------------------

function Get-WcsPolicyState {
    <#
    .SYNOPSIS
        Whether our WDAC policy is installed and enforced, according to CiTool.

    .DESCRIPTION
        A failed query is Queried = $false, never "no policy". CiTool reports errors as well
        formed JSON with no Policies array, and reading that as "nothing installed" is how a guard
        ends up reinstalling a policy forever.
    #>
    [CmdletBinding()]
    param()

    $paths = Get-WcsPaths
    $unknown = [PSCustomObject]@{ Queried = $false; Present = $false; Enforced = $false }

    if (-not (Test-Path $paths.CiToolPath)) { return $unknown }

    $parsed = & $paths.CiToolPath --list-policies -json 2>$null | ConvertFrom-Json -ErrorAction SilentlyContinue
    if (-not $parsed -or $parsed.PSObject.Properties.Name -notcontains 'Policies') { return $unknown }

    $ours = $parsed.Policies | Where-Object { ($_.PolicyID -replace '[{}]', '') -eq $paths.PolicyId }

    [PSCustomObject]@{
        Queried  = $true
        Present  = [bool]$ours
        Enforced = [bool]($ours -and $ours.IsEnforced)
    }
}

function Format-WcsPolicyState {
    [CmdletBinding()]
    param([Parameter(Mandatory)] $State)

    if (-not $State.Queried) { return 'unknown (CiTool could not be queried)' }
    if (-not $State.Present) { return 'not installed' }
    if ($State.Enforced) { 'installed, enforced' } else { 'installed, not enforced' }
}

function Remove-WcsPolicy {
    <#
    .SYNOPSIS
        Removes our WDAC policy if it is installed. Returns $true when none is left.

    .DESCRIPTION
        Checks first, because --remove-policy errors when the policy is absent. Without -json
        CiTool prints "Press Enter to Continue" and waits on a stdin nobody watches; routing
        through cmd with <nul is the reliable way to give it EOF from PowerShell.
    #>
    [CmdletBinding()]
    param()

    $paths = Get-WcsPaths
    $state = Get-WcsPolicyState

    if (-not $state.Queried) { return $false }
    if (-not $state.Present) { return $true }

    cmd.exe /c "`"$($paths.CiToolPath)`" --remove-policy `"{$($paths.PolicyId)}`" -json <nul" | Out-Null

    $after = Get-WcsPolicyState
    return $after.Queried -and -not $after.Present
}

function Get-WcsUsbStart {
    [CmdletBinding()]
    param()

    (Get-ItemProperty (Get-WcsPaths).UsbStorKey -ErrorAction SilentlyContinue).Start
}

function Test-WcsSystemProtection {
    <#
    .SYNOPSIS
        Whether System Protection is on, which decides whether a restore point can exist at all.

    .DESCRIPTION
        Both values are read through the property list: either can be absent, and under
        Set-StrictMode reading a missing property throws, while a $null RPSessionInterval would
        compare as "on".
    #>
    [CmdletBinding()]
    param()

    $settings = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore' -ErrorAction SilentlyContinue
    if (-not $settings) { return $false }

    $names = $settings.PSObject.Properties.Name
    if ($names -contains 'DisableSR' -and $settings.DisableSR -eq 1) { return $false }
    if ($names -notcontains 'RPSessionInterval') { return $false }

    return $settings.RPSessionInterval -ne 0
}

function Get-WcsRestorePoint {
    <#
    .SYNOPSIS
        The newest restore point this project created, or $null.

    .DESCRIPTION
        CreationTime is WMI's yyyyMMddHHmmss.ffffffsUUU and, on restore points, UTC (offset
        -000). Parsed as local time it gave "-5 h ago" on a machine five hours behind UTC, so the
        age is computed in UTC and only the displayed time is converted.
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

    $newest = $points | Sort-Object { & $asUtc $_.CreationTime } | Select-Object -Last 1
    $createdAtUtc = & $asUtc $newest.CreationTime

    [PSCustomObject]@{
        SequenceNumber = $newest.SequenceNumber
        CreatedAt      = $createdAtUtc.ToLocalTime()
        Age            = [datetime]::UtcNow - $createdAtUtc
    }
}

function Format-WcsAge {
    [CmdletBinding()]
    param([Parameter(Mandatory)][TimeSpan] $Age)

    if ($Age.TotalHours -lt 48) { "$([int]$Age.TotalHours) h ago" } else { "$([int]$Age.TotalDays) days ago" }
}

Export-ModuleMember -Function Get-WcsPaths, Write-WcsStep, Write-WcsField, Test-WcsAdministrator,
    Assert-WcsAdministrator, Assert-WcsDotnetSdk, Assert-WcsArtifact, Wait-WcsServiceStatus, Wait-WcsHealth,
    Get-WcsPolicyState, Format-WcsPolicyState, Remove-WcsPolicy, Get-WcsUsbStart,
    Test-WcsSystemProtection, Get-WcsRestorePoint, Format-WcsAge
