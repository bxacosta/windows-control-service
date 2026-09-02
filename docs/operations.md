# Operations

## Requirements

- Windows 11 with `CiTool.exe` in `%SystemRoot%\System32`, which ships with the system.
- **Elevated** PowerShell. Every script checks for it and fails early without it.
- .NET SDK 10.0.1xx to build only. What is published is self-contained and needs no installed
  runtime, but it is more than one file.
- Node only for `dotnet test`, which runs the interface rules through it. It takes no part in
  the build or the deployment, and the installed service does not need it.

If PowerShell answers `running scripts is disabled on this system`, the session has a
restricted policy. Bypass it per invocation:
`powershell -ExecutionPolicy Bypass -File .\scripts\install.ps1 -From .\publish`.

## What the service touches on the machine

| Resource     | Value                                                                               |
|--------------|-------------------------------------------------------------------------------------|
| Service      | `WindowsControlService`, automatic start, runs as **LocalSystem**                   |
| Binaries     | `C:\Program Files\WindowsControlService`                                            |
| Data         | `C:\ProgramData\WindowsControlService` (database and logs)                          |
| Port         | `5150`, loopback only                                                               |
| WDAC policy  | `{9E9BB70B-2BD8-4EE9-9031-30476FCF1FF3}`                                            |
| Registry     | `HKLM\SYSTEM\CurrentControlSet\Services\USBSTOR`, value `Start`                     |
| Registry     | `HKLM\SYSTEM\CurrentControlSet\Control\StorageDevicePolicies`, value `WriteProtect` |
| Event Viewer | source `WindowsControlService` in the `Application` log                             |

LocalSystem is the highest privilege on the machine and is required: writing to HKLM and
driving `CiTool` need it.

## Before installing

WDAC is the one risk that is not undone in seconds. A malformed policy stops Windows from
running legitimate programs, and a policy left behind survives the uninstall.

| Risk                   | Consequence                                                          | Mitigation                                                                                           |
|------------------------|----------------------------------------------------------------------|------------------------------------------------------------------------------------------------------|
| Malformed WDAC policy  | A deny-only policy behaves as an allowlist and blocks everything     | The XML is validated against the XSD before deployment, and every policy carries the two allow rules |
| Orphaned policy        | Applications blocked with nothing explaining it, surviving uninstall | `uninstall.ps1` removes it first and verifies with `CiTool`; a failure exits with an error           |
| `USBSTOR` left at `4`  | No USB storage mounts again                                          | Restore with `Start = 3`                                                                             |
| Half-installed service | A registered name with no binary, or a binary in use                 | `uninstall.ps1 -Force`                                                                               |

Create a restore point first:

```powershell
.\scripts\restore-point.ps1          # -Force to get one within 24 hours of the last
```

**The net is for WDAC, and only for WDAC.** A policy built wrong can leave this machine refusing
to run applications, including whatever you would reach for to undo it, and no script here can
promise to talk its way out of that. It is not the net for a registry change: the USB tests write
two DWORDs and put them back in a `finally`, and the recovery when that fails is one
`Set-ItemProperty`, not a rollback of the whole machine. Run this before anything that applies a
policy; skip it for the registry tests.

The point is created under one fixed description, `WindowsControlService checkpoint`, so
`status.ps1` can tell one made for a validation apart from the ones Windows makes before its own
updates:

```
==> Restore point
    WindowsControlService checkpoint: 2026-09-02 02:36 (0 h ago)
```

Windows refuses to create a second point within 24 hours of the last, and **it refuses
silently** — `Checkpoint-Computer` reports success for a call that was thrown away. The script
reads the newest point of ours before and after and says which actually happened. `-Force` lifts
the throttle for that one call by setting `SystemRestorePointCreationFrequency` to 0 and putting
the original back in a `finally`, rather than leaving the machine with the throttle off forever.

If system protection is off, no point can exist and the script says so instead of failing
obscurely; turn it on with `Enable-ComputerRestore -Drive $env:SystemDrive`.

The policy deliberately enables `Enabled:Advanced Boot Options Menu`, so the advanced startup
menu remains reachable and leads to the restore point.

## Install

Building and deploying are separate steps, so what is about to be installed can be inspected
first.

```powershell
.\scripts\build.ps1                       # publishes to .\publish
.\scripts\install.ps1 -From .\publish     # deploys and registers the service
```

`install.ps1` does not compile. If `-From` does not exist or does not contain the executable it
stops and says so.

**Copying only the `.exe` does not work.** `PublishSingleFile` leaves the native libraries out:
`e_sqlite3.dll` and `aspnetcorev2_inprocess.dll` sit beside the executable, and without the
first the process does not start (`DllNotFoundException` while initialising SQLite).
`install.ps1` copies the whole folder.

Set the password before anything else:

```powershell
curl.exe -X POST http://localhost:5150/api/auth/password `
         -H "Content-Type: application/json" -d '{\"password\":\"<your password>\"}'
```

The endpoint is public while no password exists and answers `409` once one does.

## The interface

With the service running, `http://localhost:5150/` serves it: applications, devices, history and
settings. It is the same API underneath, so anything doable with `curl` is doable there and the
other way round. Loopback only.

## PowerShell becomes restricted while anything is blocked

This is the first thing that surprises, and it is not a fault. With a WDAC policy in enforcement
the interactive PowerShell console drops to `ConstrainedLanguage`, in 5.1 and 7 alike:

```
                          no policy        policy in force
interactive console       FullLanguage     ConstrainedLanguage
.ps1 file                 FullLanguage     FullLanguage
[System.IO.File] typed    allowed          blocked
[System.IO.File] in .ps1  allowed          allowed
```

The scripts in this repository keep working in full: a `.ps1` on disk runs in `FullLanguage`,
including the `[Security.Principal.WindowsPrincipal]::new()` in the shared module and the
`Add-Type` in the validation script. What gets restricted is what is **typed or passed with
`-Command`**.

To get the full mode back, remove the blocks and **open a new console**. The one already open
stays restricted: the mode is fixed when the process starts.

## Verify that a block blocks

```powershell
.\scripts\validate-blocking.ps1
```

Starts a temporary instance on another port — it does not touch the installed service or its
password — builds two variants of a harmless test executable, one with `OriginalFilename` and
one with no version resource, and tries to block both. The first must end up blocked by Windows;
the second must be **refused** by the service. Everything it applies, it removes.

## Update

```powershell
.\scripts\build.ps1
.\scripts\update.ps1 -From .\publish
```

Keeps `C:\ProgramData\WindowsControlService`: the password, the blocked applications and the
history. The script waits for the service to actually stop before overwriting the executable.
With `ShutdownTimeout` at 70 seconds, sleeping two is not enough and the symptom is `Copy-Item`
failing on a file in use.

It refuses a folder that is not a build of this service -- one without `wwwroot\index.html`
included, which would install a service that answers the API and serves no interface -- and it
does not report success until `GET /api/health` answers. Running is not serving: the Service
Control Manager reports it as soon as the process is up, before Kestrel listens and before the
migrations have run.

**Re-running it is the recovery for an update that failed part way through.** It empties the
install directory before copying, so a copy that dies half way leaves a registered service with
no binary and `Start-Service` failing — and the fix is to run the same command again, not to
uninstall. Verified by emptying `C:\Program Files\WindowsControlService` completely and running
it: it stops nothing, replaces everything, starts, and answers. The data directory is never in
the blast radius.

## Uninstall

```powershell
.\scripts\uninstall.ps1              # asks before deleting data
.\scripts\uninstall.ps1 -RemoveData  # deletes the password and the history too
```

The order is not negotiable: stop the service, **remove the WDAC policy**, restore the registry,
delete the service, delete the binaries, and only then the data.

If removing the policy fails the script says so, prints the manual command and exits with an
error code. A machine with blocked applications and no service to explain them is the worst
possible outcome.

## Diagnose

```powershell
.\scripts\status.ps1            # service, port, health, policy, USB, database, logs
.\scripts\status.ps1 -LogLines 50
```

Works the same with the service installed and without it.

| Policy state it reports          | Meaning                                                           |
|----------------------------------|-------------------------------------------------------------------|
| `not installed`                  | No policy of ours                                                 |
| `installed, enforced=True`       | Applied and in force                                              |
| `could not be queried (Unknown)` | `CiTool` could not be asked. Not the same as "there is no policy" |

Where to look when something fails:

1. `.\scripts\status.ps1`.
2. `C:\ProgramData\WindowsControlService\logs\wcs-*.log` — everything, stamped UTC.
3. Event Viewer, `Application` log, source `WindowsControlService` — `Warning` and above only.

Both destinations carry UTC in the text. The Event Viewer's `TimeCreated` is set by Windows in
local time and no application can change it, which is why the message repeats the UTC stamp:
that is what allows the two to be correlated.

## Emergency

```powershell
.\scripts\uninstall.ps1 -Force
```

Total cleanup, idempotent, no questions and nothing kept: service, policy, registry, event
source, binaries, data, and whatever a validation run left in `TEMP`. It ends by printing the
real state rather than assuming it — service absent, policy absent, `USBSTOR Start` at `3`, both
paths gone. On a machine that never had the service it does nothing.

This is the mode for a machine where something stopped half way: an install that failed, a
validation that crashed, a service deleted by hand with its policy still in force.

If a program stops running and an orphaned policy is suspected:

```powershell
& "$env:SystemRoot\System32\CiTool.exe" --list-policies -json | ConvertFrom-Json |
  Select-Object -ExpandProperty Policies | Where-Object { -not $_.IsSystemPolicy }

& "$env:SystemRoot\System32\CiTool.exe" --remove-policy "{GUID}"
```

From PowerShell, `--remove-policy` without `-json` waits on a "Press Enter to Continue" nobody
sees and the script hangs. The scripts here avoid it by supplying EOF through `cmd`; by hand,
always pass `-json`.

If USB drives do not mount:

```powershell
Set-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Services\USBSTOR" -Name Start -Value 3
```

`3` is Manual, the normal state. `4` is Disabled.

Last resort: restore the system restore point.

## API reference

Not maintained by hand. With the service running:

- `http://localhost:5150/openapi/v1.json`
- `http://localhost:5150/openapi/v1.yaml`

The contract and the meaning of the fields are in `api.md`.
