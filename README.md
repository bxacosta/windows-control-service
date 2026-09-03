<p align="center">
  <img src="banner/banner.png" alt="Windows Control Service, showing the Applications section of the interface" width="900">
</p>

<h1 align="center">Windows Control Service</h1>

<p align="center">
  Blocks applications with WDAC, blocks USB storage, and records every sign-in, local or over RDP.<br>
  One machine, one password, a web interface on <code>localhost</code>.
</p>

## What it is

WDAC (Windows Defender Application Control) decides in the kernel whether an executable may run. This service generates
that policy, deploys it with `CiTool`, and puts an interface in front of it, so blocking an application is a switch
rather than hand-written XML.

Two things follow from where the blocking lives:

- **Stopping or uninstalling the service does not lift a block.** The kernel enforces a deployed policy on its own.
  AppLocker, the obvious alternative, depends on a service that can be stopped.
- **Renaming an executable does not lift it either.** Rules match a field of the PE version resource, not a path:
  `OriginalFilename`, falling back to `InternalName` or `ProductName`.

## What it does

| Feature                           | Mechanism                                                                              |
|-----------------------------------|----------------------------------------------------------------------------------------|
| Application blocking              | A generated WDAC policy, converted to binary and deployed with `CiTool`                |
| Enable / disable without deleting | The policy is rebuilt and redeployed                                                   |
| Reconciliation                    | A worker compares the deployed policy against the database every minute                |
| USB storage blocking              | The `Start` value of `USBSTOR` in the registry                                         |
| Process inventory                 | Running processes, less anything under `C:\Windows`, named from their version resource |
| Access history                    | The Terminal Services log, a 30-day window, ingested in the background                 |
| Authentication                    | One password, a session cookie, an attempt limit                                       |
| REST API                          | `http://localhost:5150`, loopback only                                                 |
| Web interface                     | Served from the same origin, no framework and no build step                            |

Blocking `USBSTOR` stops new drives from mounting; drives already mounted stay mounted.

## What it is not

**It is not a security boundary.** It runs under an administrator account, and an administrator can stop it, uninstall
it, or remove the policy with `CiTool --remove-policy`. It enforces a decision already made; it does not hold against
someone who wants it gone.

- **The policy is unsigned**, so removing it needs no key. The reconciliation worker puts it back within a minute, which
  shortens that window rather than closing it, and only while the service runs.
- **A rule matches a field inside the file.** Edit the version resource of a copy and the rule stops matching it.
- **An executable with no version resource cannot be blocked.** It is refused rather than given a rule that matches
  nothing.
- **There are no user accounts.** One password guards the interface, and anyone who can reinstall does not need it.

Out of scope, deliberately:

- **Failed sign-in attempts.** They need the Security log, which the target machine retained for only a few hours.
- **Blocking installers.** An installer cannot be told from an ordinary program at the executable level.
- **Signed policies.** They would remove the need for the reconciliation worker, but require a CA and custody of a key.

## Requirements

- Windows 11 (tested on Pro 26200). `CiTool.exe` ships with it.
- Elevated PowerShell to install and operate.
- .NET SDK 10.0.1xx to build. What is published is self-contained.

## Quick start

```powershell
.\scripts\build.ps1                       # publishes to .\publish
.\scripts\install.ps1 -From .\publish     # registers and starts the service

curl.exe http://localhost:5150/api/health
```

Then open `http://localhost:5150/` and set the password. **Before anything else:** until one exists, the endpoint that
sets it is public.

To remove everything:

```powershell
.\scripts\uninstall.ps1 -RemoveData
```

It stops the service, removes the policy, restores `USBSTOR`, and deletes the registration, the binaries and the data.
The policy goes first and is verified: if `CiTool` still lists it, the script fails and prints the command to remove it
by hand.

## Documentation

| Document                                                 | What it is                                                                                              |
|----------------------------------------------------------|---------------------------------------------------------------------------------------------------------|
| [`docs/architecture.md`](docs/architecture.md)           | Shape of the solution, the patterns that do not change, known limits                                    |
| [`docs/windows-internals.md`](docs/windows-internals.md) | **WDAC, registry, event log, PE files, processes.** Behaviour that is not in the official documentation |
| [`docs/api.md`](docs/api.md)                             | The HTTP surface and the event stream                                                                   |
| [`docs/web-interface.md`](docs/web-interface.md)         | Module map, the behaviour rules, and the DOM harness                                                    |
| [`docs/operations.md`](docs/operations.md)               | Install, update, uninstall, diagnose, recover                                                           |
| [`docs/development.md`](docs/development.md)             | Toolchain, tests, packages, publishing                                                                  |

## Development

```powershell
dotnet build                              # 0 warnings; TreatWarningsAsErrors is on
dotnet test                               # everything; the ones that touch the machine need elevation
dotnet test --filter "Requires!=Admin"

dotnet run --project src\WindowsControlService -- --data-dir .\.localdata --urls http://localhost:5151
```

## License

This project is licensed under the [MIT License](LICENSE).