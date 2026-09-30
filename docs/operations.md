# Operations

Every operation is a `.\wcs` command, run from the repository root. `.\wcs` alone lists them.

## Requirements

- Windows 11 with `CiTool.exe` in `%SystemRoot%\System32`, which ships with the system.
- PowerShell 7 or Windows PowerShell 5.1. `deploy`, `uninstall`, `restore-point` and `validate`
  need administrator rights; `.\wcs` asks for them when they are missing, through Windows' `sudo`
  when it is enabled and otherwise in a new elevated window.
- .NET SDK 10.0.1xx to build only. What is published is self-contained and needs no installed
  runtime, but it is more than one file.
- Node only for the tests, which run the interface rules through it. It takes no part in the
  build or the deployment, and the installed service does not need it.

If PowerShell answers `running scripts is disabled on this system`, the session has a
restricted policy. Bypass it per invocation: `powershell -ExecutionPolicy Bypass -File .\wcs.ps1 deploy`.

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
| Orphaned policy        | Applications blocked with nothing explaining it, surviving uninstall | `.\wcs uninstall` removes it first and verifies with `CiTool`; a failure exits with an error         |
| `USBSTOR` left at `4`  | No USB storage mounts again                                          | Restore with `Start = 3`                                                                             |
| Half-installed service | A registered name with no binary, or a binary in use                 | `.\wcs deploy` again, or `.\wcs uninstall -Force`                                                    |

Create a restore point first:

```powershell
.\wcs restore-point          # -Force to get one within 24 hours of the last
```

**The net is for WDAC, and only for WDAC.** A policy built wrong can leave this machine refusing
to run applications, including whatever you would reach for to undo it, and no script here can
promise to talk its way out of that. It is not the net for a registry change: the USB tests write
two DWORDs and put them back in a `finally`, and the recovery when that fails is one
`Set-ItemProperty`, not a rollback of the whole machine. Run this before anything that applies a
policy; skip it for the registry tests.

The point is created under one fixed description, `WindowsControlService checkpoint`, so
`.\wcs status` can tell one made for a validation apart from the ones Windows makes before its
own updates:

```
  Restore point 2026-09-29 23:24 (0 h ago)
```

Windows refuses to create a second point within 24 hours of the last, and **it refuses
silently**: `Checkpoint-Computer` reports success for a call that was thrown away. The command
reads the newest point of ours before and after and says which actually happened. `-Force` lifts
the throttle for that one call by setting `SystemRestorePointCreationFrequency` to 0 and putting
the original back in a `finally`, rather than leaving the machine with the throttle off forever.

If system protection is off, no point can exist and the command says so instead of failing
obscurely; turn it on with `Enable-ComputerRestore -Drive $env:SystemDrive`.

The policy deliberately enables `Enabled:Advanced Boot Options Menu`, so the advanced startup
menu remains reachable and leads to the restore point.

## Install and update

```powershell
.\wcs deploy
```

The same command installs and updates. It builds to `.\publish`, then registers the service if it
is not installed, or replaces the binaries of the one that is. It does not report success until
`GET /api/health` answers: the Service Control Manager reports Running as soon as the process is
up, before Kestrel listens and before the migrations have run.

To inspect a build before installing it, build and deploy separately:

```powershell
.\wcs build                    # publishes to .\publish, installs nothing
.\wcs deploy -From .\publish   # deploys that folder without building
```

`deploy` refuses a folder without the executable or without `wwwroot\index.html`, which would
install a service that answers the API and serves no interface.

**Copying only the `.exe` does not work.** `PublishSingleFile` leaves the native libraries out:
`e_sqlite3.dll` and `aspnetcorev2_inprocess.dll` sit beside the executable, and without the
first the process does not start (`DllNotFoundException` while initialising SQLite). `deploy`
copies the whole folder.

On a first install, set the password before anything else, in the interface or with:

```powershell
curl.exe -X POST http://localhost:5150/api/auth/password `
         -H "Content-Type: application/json" -d '{\"password\":\"<your password>\"}'
```

The endpoint is public while no password exists and answers `409` once one does. `deploy` warns
while none is set.

### What an update keeps

`C:\ProgramData\WindowsControlService`: the password, the blocked applications and the history.
`deploy` waits for the service to actually stop before overwriting the executable. With
`ShutdownTimeout` at 70 seconds, sleeping two is not enough and the symptom is `Copy-Item`
failing on a file in use.

**Re-running it is the recovery for a deploy that failed part way through.** It empties the
install directory before copying, so a copy that dies half way leaves a registered service with
no binary and `Start-Service` failing, and the fix is to run the same command again, not to
uninstall. The data directory is never in the blast radius.

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

The `.\wcs` commands keep working in full: a `.ps1` on disk runs in `FullLanguage`, including
the `[Security.Principal.WindowsPrincipal]::new()` in the shared module and the `Add-Type` in the
validation. What gets restricted is what is **typed or passed with `-Command`**.

To get the full mode back, remove the blocks and **open a new console**. The one already open
stays restricted: the mode is fixed when the process starts.

## Verify that a block blocks

```powershell
.\wcs restore-point
.\wcs validate
```

It refuses to run without a restore point of ours from the last 24 hours. It starts a temporary
instance on another port, so the installed service and its password are not touched, builds two
variants of a harmless test executable, one with `OriginalFilename` and one with no version
resource, and tries to block both. The first must end up blocked by Windows; the second must be
**refused** by the service. Everything it applies, it removes, and it prints the final state.

## Uninstall

```powershell
.\wcs uninstall                # keeps the data
.\wcs uninstall -RemoveData    # deletes the password and the history too
```

The order is not negotiable: stop the service, **remove the WDAC policy**, restore the registry,
delete the service, delete the binaries, and only then the data. It ends by reading the final
state back rather than assuming it.

If removing the policy fails the command says so, prints the manual command and exits with an
error code. A machine with blocked applications and no service to explain them is the worst
possible outcome.

## Diagnose

```powershell
.\wcs status              # service, health, version, policy, USB, restore point, database
.\wcs status -Logs 50     # plus the last 50 log lines and the Event Viewer entries
```

Works the same with the service installed and without it. When the installed version was built
from an older commit than the working tree's HEAD, it says so.

| Policy state it reports                 | Meaning                                                           |
|-----------------------------------------|-------------------------------------------------------------------|
| `not installed`                         | No policy of ours                                                 |
| `installed, enforced`                   | Applied and in force                                              |
| `unknown (CiTool could not be queried)` | `CiTool` could not be asked. Not the same as "there is no policy" |

Where to look when something fails:

1. `.\wcs status -Logs 50`.
2. `C:\ProgramData\WindowsControlService\logs\wcs-*.log`: everything, stamped UTC.
3. Event Viewer, `Application` log, source `WindowsControlService`: `Warning` and above only.

Both destinations carry UTC in the text. The Event Viewer's `TimeCreated` is set by Windows in
local time and no application can change it, which is why the message repeats the UTC stamp:
that is what allows the two to be correlated.

## Emergency

```powershell
.\wcs uninstall -Force
```

Total cleanup, idempotent, nothing kept: service, policy, registry, event source, binaries, data,
and whatever a validation run left in `TEMP`. It ends by printing the real state rather than
assuming it. On a machine that never had the service it does nothing.

This is the mode for a machine where something stopped half way: an install that failed, a
validation that crashed, a service deleted by hand with its policy still in force.

If a program stops running and an orphaned policy is suspected:

```powershell
& "$env:SystemRoot\System32\CiTool.exe" --list-policies -json | ConvertFrom-Json |
  Select-Object -ExpandProperty Policies | Where-Object { -not $_.IsSystemPolicy }

& "$env:SystemRoot\System32\CiTool.exe" --remove-policy "{GUID}" -json
```

From PowerShell, `--remove-policy` without `-json` waits on a "Press Enter to Continue" nobody
sees and hangs. The commands here avoid it by supplying EOF through `cmd`; by hand, always pass
`-json`.

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
