# WindowsControlService

A Windows service that controls which applications may run, whether USB storage mounts, and
keeps a record of when the machine was signed into.

## The constraint that shapes it

One Windows 11 PC is used by more than one person through **a single administrator account**.
That rules out per-user policy, NTFS permissions, and anything that depends on the other user
having fewer privileges. Two decisions follow:

- **WDAC rather than AppLocker.** WDAC is enforced in the kernel and **stays in force while the
  service is stopped or uninstalled**. AppLocker depends on a service an administrator can stop.
- **A reconciliation worker.** An administrator can remove the policy with
  `CiTool --remove-policy`. That cannot be prevented, but it can be detected and undone every
  minute.

## Requirements

- Windows 11 (tested on Pro 26200) with `CiTool.exe`, which ships with the system.
- Elevated PowerShell to install and operate.
- .NET SDK 10.0.1xx **to build only**. What is published is self-contained.

## Quick start

```powershell
.\scripts\build.ps1                       # publishes to .\publish
.\scripts\install.ps1 -From .\publish     # registers and starts the service

curl.exe http://localhost:5150/api/health
```

Then open `http://localhost:5150/` and set the password. Until one exists the endpoint that
sets it is public, which is also why it is the first thing to do. From the command line:

```powershell
curl.exe -X POST http://localhost:5150/api/auth/password `
         -H "Content-Type: application/json" -d '{\"password\":\"<your password>\"}'
```

To remove everything:

```powershell
.\scripts\uninstall.ps1 -RemoveData
```

**A WDAC policy survives an uninstall.** `uninstall.ps1` removes it and verifies the removal; if
it cannot, it says so and exits with an error. That message must not be ignored — a machine with
blocked applications and no service to explain them is the worst outcome available.

## What it does

| Feature | Mechanism |
|---|---|
| Application blocking | A generated WDAC policy, converted to binary and deployed with `CiTool` |
| Enable / disable without deleting | The policy is rebuilt and redeployed |
| Reconciliation | A worker compares the deployed policy against the database every minute |
| USB storage blocking | The `Start` value of `USBSTOR` in the registry |
| Process inventory | To pick what to block without typing a path |
| Access history | The Terminal Services log, a 30-day window, ingested in the background |
| Authentication | One password, a session cookie, an attempt limit |
| REST API | `http://localhost:5150`, loopback only |
| Web interface | Served from the same origin, no framework and no build step |

Blocking matches on the `OriginalFilename` field of the PE header, **not on a path**: renaming
the executable does not defeat it. A binary carrying no version resource is refused rather than
blocked by a rule that would match nothing.

**Out of scope:** failed sign-in attempts (they need the Security log, which on the target
machine retained only a few hours), blocking installers (an installer cannot be told from an
ordinary program at the executable level), signed WDAC policies (they would make the
reconciliation worker unnecessary but require a CA and private key custody), and multiple users
(there are no users, there is a password).

Blocking `USBSTOR` stops new drives from mounting; it does not unmount drives already mounted.

## Layout

```
src/WindowsControlService/
├── Features/         one vertical slice per feature
├── Platform/         everything that talks to Windows, behind interfaces
├── Infrastructure/   Result, database, hosting, logging, events
├── wwwroot/          the web interface
└── Program.cs
tests/
├── WindowsControlService.UnitTests/
├── WindowsControlService.IntegrationTests/
└── interface/        the interface rules, run by node --test
scripts/              build, install, update, uninstall, status, validation, the DOM harness
docs/
```

## Documentation

| Document | What it is |
|---|---|
| [`docs/architecture.md`](docs/architecture.md) | Shape of the solution, the patterns that do not change, known limits |
| [`docs/windows-internals.md`](docs/windows-internals.md) | **WDAC, registry, event log, PE files, processes.** Behaviour that is not in the official documentation |
| [`docs/api.md`](docs/api.md) | The HTTP surface and the event stream |
| [`docs/web-interface.md`](docs/web-interface.md) | Module map, the behaviour rules, and the DOM harness |
| [`docs/operations.md`](docs/operations.md) | Install, update, uninstall, diagnose, recover |
| [`docs/development.md`](docs/development.md) | Toolchain, tests, packages, publishing |
| [`DESIGN.md`](DESIGN.md) | The design system the stylesheet transcribes |

The API reference itself is generated, not maintained by hand:
`http://localhost:5150/openapi/v1.json`.

**Before touching any Windows API, read `docs/windows-internals.md`.**

## Development

```powershell
dotnet build                              # 0 warnings; TreatWarningsAsErrors is on
dotnet test                               # everything; the ones that touch the machine need elevation
dotnet test --filter "Requires!=Admin"

dotnet run --project src\WindowsControlService -- --data-dir .\.localdata --urls http://localhost:5151
```

Run outside the service, the application logs a `Warning` that privileged operations will use
the current user's permissions rather than LocalSystem's.
