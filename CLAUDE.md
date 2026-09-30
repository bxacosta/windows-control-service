# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## The project

A .NET 10 Windows service on one Windows 11 machine: blocks applications with WDAC, blocks USB storage, records
sign-ins, serves a password-protected API and interface on `http://localhost:5150`.
`README.md` has what it is and is not; this file is the rules.

**Read `docs/windows-internals.md` before touching any Windows API**: the behaviour it records is mostly absent from
Microsoft's.

Rules below about the installed service apply only when `.\wcs status` shows one.

## Safety rules: this agent runs elevated

Administrator rights on a real machine; a bad WDAC policy stops real programs from running.

1. **Restore point before anything that applies a WDAC policy:** `.\wcs restore-point`. Not created, not
   applied. WDAC only: the registry tests put two DWORDs back in a `finally`.
2. **Never deploy a policy that denies a real application.** Real-WDAC tests use the executable
   `.\wcs validate` compiles, never a browser, a game or a system tool.
3. **Every destructive validation is torn down in the same turn.** No policy left applied, no registry value left
   changed, no service left installed "for the next phase". Verify the final state.
4. **Read a registry value before writing it**, and restore it in a `finally`.
5. **Leave the machine as found:** owner's policy intact, `USBSTOR Start` and `WriteProtect` at their original values, a
   service that was running still running, at HEAD's version.
6. **Never `--force`, `-Force` or `--no-verify`** on a destructive operation unless the step asks for it.

Anything else that can affect the machine: ask first.

## Build, test, run

`.\wcs` is the entry point; `.\wcs` alone lists the commands. Each is one script in `scripts\`.

```powershell
.\wcs dev                               # from source, hot reload, :5151, data in src\WindowsControlService\.localdata
.\wcs test                              # everything, node --test suites included
.\wcs test -Fast                        # skips the tests that touch the machine
.\wcs test -Filter PasswordServiceTests # one class, or one test
.\wcs build                             # publishes to .\publish (Properties\PublishProfiles\win-x64.pubxml)
.\wcs deploy                            # builds, backs up the database, then installs or updates
.\wcs status                            # service, health, version vs HEAD, policy, USB, restore point, database
.\wcs uninstall                         # policy first, then registry, service, binaries; keeps the data

dotnet build                                    # TreatWarningsAsErrors is on; never turned off
node --test "tests/interface/*.test.mjs"        # the interface rules alone
node scripts/interface-dom.mjs --out=after.txt  # the DOM harness
node banner/generate.mjs                        # redraws the README image
```

## How the work is done

- **Demonstrate, do not assert.** "Tests pass" carries the `dotnet test` output; "returns 400"
  carries the `curl`.
- **Interface changes are measured with the harness.** The `[check]` captures must come out identical; `[markup]`
  changes with the appearance. See `docs/web-interface.md`.
- **Deploy when finished, if this machine runs the service**, so it reports HEAD's version.
- **If an instruction turns out to be wrong, say so and propose the fix**, rather than following it blindly or deviating
  in silence.

## Conventions that do not bend

- Predictable failures return `Result` / `Result<T>`. Exceptions mean a bug.
- No service calls `DateTime.UtcNow`; `TimeProvider` is injected. No exceptions.
- No domain service touches Windows. Registry, `CiTool`, event log, processes and PE files live behind interfaces in
  `Platform/`; a service instantiating `Registry.LocalMachine` is wrong.
- No hardcoded operational values. Intervals, windows, timeouts, page sizes and hash parameters go in `IOptions<T>` with
  `ValidateOnStart()`.
- One place maps errors to HTTP. Endpoints never build `Results.Problem`.
- Everything committed is in English: code, comments, names, log and commit messages, `docs/`.
  `docs/internal/` is local, unpublished, and stays as it is.
- Comments say why, not what. Paraphrasing the next line is noise; a Windows trap or a non-obvious decision is required.
- `Features/` one vertical slice per capability · `Platform/` the only code that talks to Windows ·
  `Infrastructure/` `Result`, database, hosting, logging, events · `wwwroot/` the interface, no framework, no build
  step.

## Documentation

| Document                    | When to read it                                                   |
|-----------------------------|-------------------------------------------------------------------|
| `README.md`                 | What the project is and is not                                    |
| `DESIGN.md`                 | **Before touching `app.css` or `index.html`.** The design system  |
| `docs/windows-internals.md` | **Before touching any Windows API**                               |
| `docs/architecture.md`      | Structure, the patterns that do not change, known limits          |
| `docs/api.md`               | The HTTP surface and the event stream                             |
| `docs/web-interface.md`     | **Before touching `wwwroot`.** The rules a redesign must not lose |
| `docs/operations.md`        | Install, update, uninstall, diagnose, recover                     |
| `docs/development.md`       | **Before writing C#.** Toolchain, tests, packages                 |
| `docs/internal/`            | Local, unpublished: phase plan, verification log, decisions       |
