#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Leaves the machine with no trace of WindowsControlService. Idempotent: running it twice
    is not an error, and running it on a machine that never had the service is a no-op.

.DESCRIPTION
    This exists so that no validation can leave a WDAC policy behind. A stranded policy blocks
    applications with nothing on the machine left to explain why.
#>

$ErrorActionPreference = "Continue"

# The policy this service deploys. Deliberately different from the A1B2C3D4-... residue that
# phase 0 looks for, so the two can never be confused.
$policyId  = "9E9BB70B-2BD8-4EE9-9031-30476FCF1FF3"
$serviceName = "WindowsControlService"
$ciTool    = "$env:SystemRoot\System32\CiTool.exe"

# 1. Service. Wait for a real state rather than sleeping: ShutdownTimeout is 70 seconds
#    because a WDAC operation can take that long, so two seconds is a race, not a wait.
$service = Get-Service $serviceName -ErrorAction SilentlyContinue
if ($service) {
    Stop-Service $serviceName -Force -ErrorAction SilentlyContinue
    try { $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(90)) } catch { }
    sc.exe delete $serviceName | Out-Null
}

# 2. WDAC policy. Ask first: --remove-policy errors when the policy is not installed, and
#    without -json CiTool prints "Press Enter to Continue" and waits on stdin forever. Piping
#    from cmd with <nul is the only reliable way to give it EOF from PowerShell.
if (Test-Path $ciTool) {
    $installed = & $ciTool --list-policies -json 2>$null | ConvertFrom-Json -ErrorAction SilentlyContinue
    if ($installed.Policies | Where-Object { $_.PolicyID -replace '[{}]', '' -eq $policyId }) {
        cmd.exe /c "`"$ciTool`" --remove-policy `"{$policyId}`" -json <nul" | Out-Null
    }
}

# 3. Registry. 3 is Manual, the normal state in which USB drives mount.
Set-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Services\USBSTOR" -Name Start -Value 3 -ErrorAction SilentlyContinue
Remove-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Control\StorageDevicePolicies" `
    -Name WriteProtect -ErrorAction SilentlyContinue

# 4. Event log source, registered by the installer. Phase 0 did not know about this one: the
#    machine still carried a source left behind by an earlier installation.
if ([System.Diagnostics.EventLog]::SourceExists($serviceName)) {
    [System.Diagnostics.EventLog]::DeleteEventSource($serviceName)
}

# 5. Files.
Remove-Item "C:\Program Files\WindowsControlService" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item "C:\ProgramData\WindowsControlService"   -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item "$env:TEMP\wcs-test-target"              -Recurse -Force -ErrorAction SilentlyContinue

# 6. Verification. Prints the real state; assumes nothing.
$policyPresent = $false
if (Test-Path $ciTool) {
    $remaining = & $ciTool --list-policies -json 2>$null | ConvertFrom-Json -ErrorAction SilentlyContinue
    $policyPresent = [bool]($remaining.Policies | Where-Object { $_.PolicyID -replace '[{}]', '' -eq $policyId })
}

[PSCustomObject]@{
    Service        = if (Get-Service $serviceName -ErrorAction SilentlyContinue) { "PRESENT" } else { "absent" }
    WdacPolicy     = if ($policyPresent) { "PRESENT" } else { "absent" }
    UsbStorStart   = (Get-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Services\USBSTOR").Start
    EventLogSource = if ([System.Diagnostics.EventLog]::SourceExists($serviceName)) { "PRESENT" } else { "absent" }
    ProgramFiles   = Test-Path "C:\Program Files\WindowsControlService"
    ProgramData    = Test-Path "C:\ProgramData\WindowsControlService"
} | Format-List
