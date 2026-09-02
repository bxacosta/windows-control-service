#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Proves against real WDAC that blocking matches the binary, not the file name on disk.

.DESCRIPTION
    Two variants of a purpose-built test executable, and the asymmetry between them is the whole
    point:

      A · carries an OriginalFilename -> is accepted, and Windows then refuses to run it
      B · no version resource at all  -> is refused when blocking, with a 400 that says why

    Version B used to be accepted. A deny rule with FileName= does not compare against the name
    of the file on disk, it compares against the OriginalFilename embedded in the binary, so the
    rule built from the path matched nothing: the policy deployed, the state read Enforced, and
    the application kept running. That is the regression this script exists to catch.

    It starts its own instance on a spare port with a throwaway data directory, so the installed
    service and its password are never touched. Everything it applies is removed in the finally
    block, and the final state is printed rather than assumed.

.PARAMETER Port
    Where the temporary instance listens. Anything but 5150, which is the installed service.
#>
[CmdletBinding()]
param(
    [int] $Port = 5170
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Continue'

Import-Module (Join-Path $PSScriptRoot 'WindowsControlService.psm1') -Force
Assert-WcsAdministrator

$repositoryRoot = Split-Path $PSScriptRoot -Parent
$work = Join-Path $env:TEMP 'wcs-blocking-validation'
$data = Join-Path $env:TEMP 'wcs-blocking-validation-data'
# Generated per run rather than written here. The instance and its database are thrown away in
# the finally block, and a password literal in a repository is a password literal whatever it
# guards. Letters and digits, because the service refuses anything else.
$password = 'v' + [Guid]::NewGuid().ToString('N').Substring(0, 15)

# Declared before the try, because the finally block uses it. Under Set-StrictMode -Version
# Latest, reading a variable that was never assigned is a terminating error -- so if the service
# failed to come up, the finally that exists to guarantee no policy is left behind would itself
# throw on line one and hide the failure that caused it.
$session = $null

Remove-Item $work, $data -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $work | Out-Null

Write-WcsStep 'Building the two variants of the test executable'

Push-Location $work
dotnet new console -n wcs-test-target --force | Out-Null
Set-Content (Join-Path $work 'wcs-test-target\Program.cs') 'Console.WriteLine("wcs-test-target running");'
dotnet publish (Join-Path $work 'wcs-test-target') -c Release -o (Join-Path $work 'a') --nologo -v q | Out-Null
$publishExitCode = $LASTEXITCODE
Pop-Location

# Checked here rather than left to surface as a missing-file error further down, where it would
# read as a WDAC problem instead of a build problem.
if ($publishExitCode -ne 0) {
    throw "Could not build the test target (dotnet publish exit $publishExitCode). Nothing was applied to this machine."
}

$variantA = Join-Path $work 'a\wcs-test-target.exe'
$variantB = Join-Path $work 'b\wcs-test-target-bare.exe'
New-Item -ItemType Directory -Force -Path (Join-Path $work 'b') | Out-Null
Copy-Item (Join-Path $work 'a\*') (Join-Path $work 'b') -Recurse
Rename-Item (Join-Path $work 'b\wcs-test-target.exe') 'wcs-test-target-bare.exe'

# BeginUpdateResource with bDeleteExistingResources drops every resource, which is the only way
# to get a runnable PE with no version information without shipping a second toolchain.
Add-Type -Namespace Wcs -Name Resources -MemberDefinition @'
[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
public static extern IntPtr BeginUpdateResource(string fileName, bool deleteExistingResources);
[DllImport("kernel32.dll", SetLastError = true)]
public static extern bool EndUpdateResource(IntPtr handle, bool discard);
'@
$handle = [Wcs.Resources]::BeginUpdateResource($variantB, $true)
if ($handle -eq [IntPtr]::Zero) { throw 'Could not open the copy to strip its resources.' }
[void][Wcs.Resources]::EndUpdateResource($handle, $false)

function Get-EmbeddedName([string] $path) {
    $name = $null
    try { $name = [Diagnostics.FileVersionInfo]::GetVersionInfo($path).OriginalFilename } catch { $name = $null }
    if ([string]::IsNullOrWhiteSpace($name)) { '(none)' } else { $name }
}

Write-WcsStep "variant A OriginalFilename : $(Get-EmbeddedName $variantA)" -Level Info
Write-WcsStep "variant B OriginalFilename : $(Get-EmbeddedName $variantB)" -Level Info

function Invoke-Target([string] $path) {
    $output = cmd.exe /c "`"$path`" 2>&1"
    if ($LASTEXITCODE -eq 0 -and $output -match 'running') { "RUNS      $output" } else { "REFUSED   $output" }
}

$service = Start-Process 'dotnet' `
    -ArgumentList 'run', '--project', (Join-Path $repositoryRoot 'src\WindowsControlService'), '--', "--data-dir=$data", "--urls=http://localhost:$Port" `
    -PassThru -NoNewWindow `
    -RedirectStandardOutput (Join-Path $work 'service.log') -RedirectStandardError (Join-Path $work 'service-err.log')

try {
    Write-WcsStep "Waiting for the temporary instance on port $Port"
    $up = $false
    foreach ($attempt in 1..90) {
        Start-Sleep -Seconds 1
        try {
            Invoke-WebRequest "http://localhost:$Port/api/health" -UseBasicParsing -TimeoutSec 2 | Out-Null
            $up = $true
            break
        }
        catch { }
    }

    # Said here rather than left to fail on the next call. A service that never came up produced
    # a 'cannot connect' on the password POST, which reads as an authentication fault.
    if (-not $up) {
        throw "The temporary instance never answered on port $Port. See $(Join-Path $work 'service-err.log')."
    }

    $session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $credentials = @{ password = $password } | ConvertTo-Json
    Invoke-WebRequest "http://localhost:$Port/api/auth/password" -Method Post -Body $credentials -ContentType 'application/json' -WebSession $session -UseBasicParsing | Out-Null
    Invoke-WebRequest "http://localhost:$Port/api/auth/login" -Method Post -Body $credentials -ContentType 'application/json' -WebSession $session -UseBasicParsing | Out-Null

    Write-WcsStep 'Before blocking'
    Write-WcsStep "A: $(Invoke-Target $variantA)" -Level Info
    Write-WcsStep "B: $(Invoke-Target $variantB)" -Level Info

    Write-WcsStep 'Blocking variant A, which carries an OriginalFilename'
    $request = @{ executablePath = $variantA; name = 'Test target A' } | ConvertTo-Json
    try {
        $response = Invoke-WebRequest "http://localhost:$Port/api/applications" -Method Post -Body $request -ContentType 'application/json' -WebSession $session -UseBasicParsing
        Write-WcsStep "status=$($response.StatusCode) $($response.Content)" -Level Info
    }
    catch {
        Write-WcsStep "status=$([int]$_.Exception.Response.StatusCode) $($_.ErrorDetails.Message)" -Level Error
    }

    Start-Sleep -Seconds 3
    Write-WcsStep "A now: $(Invoke-Target $variantA)" -Level Info

    Write-WcsStep 'Blocking variant B, which has no version resource'
    $request = @{ executablePath = $variantB; name = 'Test target B' } | ConvertTo-Json
    try {
        $response = Invoke-WebRequest "http://localhost:$Port/api/applications" -Method Post -Body $request -ContentType 'application/json' -WebSession $session -UseBasicParsing
        Write-WcsStep "status=$($response.StatusCode) $($response.Content) <- accepted, which is the regression" -Level Error
    }
    catch {
        Write-WcsStep "status=$([int]$_.Exception.Response.StatusCode) $($_.ErrorDetails.Message)" -Level Info
    }

    Write-WcsStep 'What the service recorded'
    (Invoke-WebRequest "http://localhost:$Port/api/applications" -WebSession $session -UseBasicParsing).Content
}
finally {
    Write-WcsStep 'Teardown'

    # Only worth trying if there was ever a session. Removing the policy directly, below, is the
    # step that actually guarantees the machine is left alone; unblocking through the API is the
    # tidier route when it is available.
    if ($session) {
        try {
            $blocked = (Invoke-WebRequest "http://localhost:$Port/api/applications" -WebSession $session -UseBasicParsing).Content | ConvertFrom-Json
            foreach ($entry in $blocked) {
                Invoke-WebRequest "http://localhost:$Port/api/applications/$($entry.id)" -Method Delete -WebSession $session -UseBasicParsing | Out-Null
            }
        }
        catch {
            Write-WcsStep 'Could not unblock through the API, falling back to removing the policy directly.' -Level Error
        }
    }

    if ($service -and -not $service.HasExited) { Stop-Process -Id $service.Id -Force }
    Start-Sleep -Seconds 2

    Remove-WcsPolicy | Out-Null
    Remove-Item $work, $data -Recurse -Force -ErrorAction SilentlyContinue

    $state = Get-WcsPolicyState
    Write-WcsStep "policy state : $state" -Level Info
    Write-WcsStep "USBSTOR Start: $(Get-WcsUsbStart)" -Level Info
}
