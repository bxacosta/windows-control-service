#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Proves against real WDAC that blocking matches the binary, not the file name on disk.

.DESCRIPTION
    Builds a harmless test executable in two variants and tries to block both:

      A  carries an OriginalFilename  -> accepted, and Windows then refuses to run it
      B  has no version resource      -> refused by the service with a 400 that says why

    B used to be accepted: a FileName= deny rule compares against the OriginalFilename embedded in
    the binary, not the name on disk, so the rule matched nothing while the state read Enforced.

    It runs its own instance on another port with a throwaway data directory, so the installed
    service and its password are never touched, and removes everything it applied in a finally.

.PARAMETER Port
    Where the temporary instance listens. Anything but 5150.
#>
[CmdletBinding()]
param(
    [int] $Port = 5170
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Continue'

Import-Module (Join-Path $PSScriptRoot 'WindowsControlService.psm1') -Force
$paths = Get-WcsPaths

# This applies a real policy. No net, no policy.
$point = Get-WcsRestorePoint
if (-not $point -or $point.Age.TotalHours -ge 24) {
    throw 'This applies a real WDAC policy and needs a restore point from the last 24 hours. Run .\wcs restore-point first.'
}

$work = Join-Path $env:TEMP 'wcs-blocking-validation'
$data = Join-Path $env:TEMP 'wcs-blocking-validation-data'
$baseUrl = "http://localhost:$Port"
# Per run, never a literal: the instance is thrown away, and the service accepts letters and digits.
$password = 'v' + [Guid]::NewGuid().ToString('N').Substring(0, 15)

# Assigned before the try: the finally reads them, and under Set-StrictMode an unassigned variable
# would throw there and hide the failure that got it there.
$session = $null
$instance = $null

Remove-Item $work, $data -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $work | Out-Null

Write-WcsStep 'Building the test executable'
$targetProject = Join-Path $work 'wcs-test-target'
dotnet new console --output $targetProject --name wcs-test-target --force | Out-Null
Set-Content (Join-Path $targetProject 'Program.cs') 'Console.WriteLine("wcs-test-target running");'
dotnet publish $targetProject -c Release -o (Join-Path $work 'a') --nologo -v q | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Could not build the test executable (exit $LASTEXITCODE). Nothing was applied." }

$variantA = Join-Path $work 'a\wcs-test-target.exe'
$variantB = Join-Path $work 'b\wcs-test-target-bare.exe'
Copy-Item (Join-Path $work 'a') (Join-Path $work 'b') -Recurse
Rename-Item (Join-Path $work 'b\wcs-test-target.exe') 'wcs-test-target-bare.exe'

# BeginUpdateResource with deleteExistingResources drops every resource: the only way to get a
# runnable PE with no version information without a second toolchain.
Add-Type -Namespace Wcs -Name Resources -MemberDefinition @'
[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
public static extern IntPtr BeginUpdateResource(string fileName, bool deleteExistingResources);
[DllImport("kernel32.dll", SetLastError = true)]
public static extern bool EndUpdateResource(IntPtr handle, bool discard);
'@
$handle = [Wcs.Resources]::BeginUpdateResource($variantB, $true)
if ($handle -eq [IntPtr]::Zero) { throw 'Could not strip the resources of variant B.' }
[void][Wcs.Resources]::EndUpdateResource($handle, $false)

function Get-OriginalFilename([string] $Path) {
    $name = [Diagnostics.FileVersionInfo]::GetVersionInfo($Path).OriginalFilename
    if ([string]::IsNullOrWhiteSpace($name)) { '(none)' } else { $name }
}

function Test-Runs([string] $Path) {
    $output = cmd.exe /c "`"$Path`" 2>&1"
    $LASTEXITCODE -eq 0 -and "$output" -match 'running'
}

function Invoke-Api([string] $Method, [string] $Route, $Body) {
    $request = @{ Uri = "$baseUrl$Route"; Method = $Method; WebSession = $session; UseBasicParsing = $true; ContentType = 'application/json' }
    if ($Body) { $request.Body = $Body | ConvertTo-Json }
    try {
        $response = Invoke-WebRequest @request
        [PSCustomObject]@{ Status = [int]$response.StatusCode; Content = $response.Content }
    }
    catch {
        [PSCustomObject]@{ Status = [int]$_.Exception.Response.StatusCode; Content = $_.ErrorDetails.Message }
    }
}

Write-WcsStep "A: OriginalFilename $(Get-OriginalFilename $variantA)" -Level Info
Write-WcsStep "B: OriginalFilename $(Get-OriginalFilename $variantB)" -Level Info

$failures = 0

try {
    Write-WcsStep "Starting a temporary instance on port $Port"
    # --no-launch-profile: the dev profile would otherwise set the environment and the port.
    $instance = Start-Process 'dotnet' -PassThru -NoNewWindow `
        -ArgumentList 'run', '--project', $paths.Project, '--no-launch-profile', '--', "--data-dir=$data", "--urls=$baseUrl" `
        -RedirectStandardOutput (Join-Path $work 'service.log') -RedirectStandardError (Join-Path $work 'service-err.log')

    if (-not (Wait-WcsHealth -Url "$baseUrl/api/health" -TimeoutSeconds 120)) {
        throw "The temporary instance never answered. See $(Join-Path $work 'service-err.log')."
    }

    $session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    Invoke-Api Post '/api/auth/password' @{ password = $password } | Out-Null
    Invoke-Api Post '/api/auth/login' @{ password = $password } | Out-Null

    if (-not (Test-Runs $variantA) -or -not (Test-Runs $variantB)) {
        throw 'The test executable does not run before anything is blocked. Nothing to prove.'
    }

    Write-WcsStep 'Blocking A'
    $response = Invoke-Api Post '/api/applications' @{ executablePath = $variantA; name = 'Test target A' }
    # Enforcement is asynchronous: CiTool returns before the policy takes effect.
    Start-Sleep -Seconds 3
    if ($response.Status -lt 300 -and -not (Test-Runs $variantA)) {
        Write-WcsStep "accepted ($($response.Status)) and Windows refuses to run it" -Level Ok
    }
    else {
        Write-WcsStep "FAILED: status $($response.Status), still runs: $(Test-Runs $variantA). $($response.Content)" -Level Fail
        $failures++
    }

    Write-WcsStep 'Blocking B'
    $response = Invoke-Api Post '/api/applications' @{ executablePath = $variantB; name = 'Test target B' }
    if ($response.Status -eq 400) {
        Write-WcsStep "refused (400): $(($response.Content | ConvertFrom-Json).detail)" -Level Ok
    }
    else {
        Write-WcsStep "FAILED: expected 400, got $($response.Status). This is the regression. $($response.Content)" -Level Fail
        $failures++
    }
}
catch {
    Write-WcsStep $_.Exception.Message -Level Fail
    $failures++
}
finally {
    Write-WcsStep 'Cleaning up'

    # Through the API when possible; Remove-WcsPolicy below is what guarantees it either way.
    if ($session) {
        $list = Invoke-Api Get '/api/applications'
        if ($list.Status -eq 200) {
            foreach ($entry in ($list.Content | ConvertFrom-Json)) {
                Invoke-Api Delete "/api/applications/$($entry.id)" | Out-Null
            }
        }
    }

    # The whole tree: the service is a child of dotnet run, and a survivor's reconciliation worker
    # would put the policy back after it is removed below.
    if ($instance -and -not $instance.HasExited) {
        taskkill.exe /PID $instance.Id /T /F | Out-Null
        $instance.WaitForExit(10000) | Out-Null
    }

    $policyRemoved = Remove-WcsPolicy
    Remove-Item $work, $data -Recurse -Force -ErrorAction SilentlyContinue

    Write-Host ''
    Write-WcsField 'WDAC policy' (Format-WcsPolicyState (Get-WcsPolicyState)) $(if ($policyRemoved) { 'Ok' } else { 'Fail' })
    Write-WcsField 'USBSTOR Start' "$(Get-WcsUsbStart)" $(if ((Get-WcsUsbStart) -eq 3) { 'Ok' } else { 'Warn' })
    Write-WcsField 'Temp files' $(if (Test-Path $work) { 'still present' } else { 'removed' })
    Write-Host ''

    if (-not $policyRemoved) {
        Write-WcsStep "THE POLICY IS STILL INSTALLED. Run .\wcs uninstall -Force, or: CiTool.exe --remove-policy `"{$($paths.PolicyId)}`" -json" -Level Fail
        $failures++
    }
}

if ($failures) {
    Write-WcsStep "Validation FAILED ($failures)" -Level Fail
    exit 1
}
Write-WcsStep 'Validation passed' -Level Ok
