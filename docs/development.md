# Development

## Toolchain

|                  |                                                                          |
|------------------|--------------------------------------------------------------------------|
| SDK              | .NET 10.0.1xx, pinned in `global.json` with `rollForward: latestFeature` |
| Target framework | `net10.0-windows`                                                        |
| Node             | for `dotnet test` only; it runs the interface rules                      |

The `-windows` suffix is required. Without it `Microsoft.Win32` (registry) and
`System.Diagnostics.Eventing.Reader` (event log) do not resolve, and plain `net10.0` fails to
compile this project.

The SDK band matters because source generators travel with it. Windows Update only patches
inside the installed band and never changes it; crossing bands is a manual action that changes
MSBuild and the generators without changing the runtime. `global.json` is what stops a
`winget upgrade` from changing the build chain silently.

Repository-wide files, all of them load-bearing:

| File                       | Purpose                                                                                   |
|----------------------------|-------------------------------------------------------------------------------------------|
| `global.json`              | Pins the SDK                                                                              |
| `Directory.Build.props`    | `TargetFramework`, `Nullable`, `TreatWarningsAsErrors`, analyzers — once, not per project |
| `Directory.Packages.props` | Central Package Management: versions in one place                                         |
| `.editorconfig`            | Style and analyzer severity; makes `dotnet format` useful                                 |
| `.gitignore`               | `**/bin/`, `**/obj/` with the folder prefix, or the test project's slip through           |

`TreatWarningsAsErrors` is on and is not turned off to make progress.

## Build, test, run

```powershell
dotnet build
dotnet test                                  # everything, including node --test
dotnet test --filter "Requires!=Admin"       # without the tests that touch the machine
node --test "tests/interface/*.test.mjs"     # the interface rules alone
node scripts/interface-dom.mjs --out=after.txt
```

Running the service from the working tree, without installing it:

```powershell
dotnet run --project src/WindowsControlService -- --data-dir .\.localdata --urls http://localhost:5151
```

Both switches matter. `--data-dir` keeps the local database away from
`C:\ProgramData\WindowsControlService`, and `--urls` keeps the port away from an installed
instance. The address is read once, in `Program.cs`, from `builder.Configuration["urls"]`.

## Tests

| Level            | Project                                                    | Covers                                                                                                  |
|------------------|------------------------------------------------------------|---------------------------------------------------------------------------------------------------------|
| Unit             | `UnitTests`                                                | Policy XML generation, session pairing, error mapping, passwords, the executor                          |
| HTTP integration | `IntegrationTests`                                         | The whole API through `WebApplicationFactory`, with a temporary SQLite database and `Platform/` doubles |
| Real platform    | `IntegrationTests`, `[Trait("Requires","Admin")]`          | `CiTool`, registry, event log, PE reader                                                                |
| Interface        | `tests/interface/*.test.mjs` + `scripts/interface-dom.mjs` | The rules in `rules.js`, and the rendered DOM                                                           |

`WebApplicationFactory` needs `Program` to be reachable, which is what the
`public partial class Program;` at the end of `Program.cs` is for.

**No mocking framework.** Doubles are hand-written `Fake*` classes in the test project: there
are few of them, they are explicit, and their failures read better.

Tests that write to the registry capture the original value first and restore it in a `finally`,
even when the test fails. Tests that touch WDAC use a purpose-built harmless executable, never a
real application.

`InterfaceRuleTests` shells out to `node --test` so that `dotnet test` stays the single answer to
"is the tree green". It fails rather than passing quietly when Node is missing.

## Packages

Microsoft packages follow the runtime version number.

| Package                                        | Version |
|------------------------------------------------|---------|
| `Microsoft.Data.Sqlite`                        | 10.0.11 |
| `Microsoft.Extensions.Hosting.WindowsServices` | 10.0.11 |
| `Microsoft.AspNetCore.OpenApi`                 | 10.0.11 |
| `Microsoft.AspNetCore.Mvc.Testing`             | 10.0.11 |
| `Microsoft.Extensions.TimeProvider.Testing`    | 10.0.x  |
| `Dapper`                                       | 2.1.79  |
| `dbup-sqlite`                                  | 6.0.4   |
| `Serilog.Extensions.Hosting`                   | 10.0.0  |
| `Serilog.Sinks.File`                           | 7.0.0   |
| `xunit`                                        | 2.9.3   |
| `xunit.runner.visualstudio`                    | 4.0.0   |
| `Microsoft.NET.Test.Sdk`                       | 18.9.0  |

### SQLitePCLRaw

`Microsoft.Data.Sqlite` does not carry the native provider itself, so the version it drags in
matters:

| `Microsoft.Data.Sqlite` | Resolves `SQLitePCLRaw` | `dotnet list package --vulnerable`                |
|-------------------------|-------------------------|---------------------------------------------------|
| 10.0.0                  | 2.1.11                  | **High** — GHSA-2m69-gcr7-jv3q in `lib.e_sqlite3` |
| 10.0.11                 | 2.1.12                  | clean                                             |

At 10.0.11 no explicit reference is needed. Dropping to 10.0.0 would require adding
`SQLitePCLRaw.bundle_e_sqlite3` >= 2.1.12 by hand.

**Do not move to the 3.0.x line.** `Microsoft.Data.Sqlite.Core 10.0.11` declares
`SQLitePCLRaw.core [2.1.12, )`; the open range would accept 3.0.5, which is ahead of what
Microsoft builds and tests against.

`dbup-sqlite` depends on `Microsoft.Data.Sqlite.Core`, **without** the native provider. It
composes correctly only while the project references the full `Microsoft.Data.Sqlite`;
otherwise it fails at run time with `You need to call SQLitePCL.raw.SetProvider()`.

Before each deployment:

```powershell
dotnet list package --vulnerable --include-transitive
dotnet list package --outdated
```

## Publishing

```powershell
dotnet publish -c Release -r win-x64 --self-contained -o .\publish
```

`PublishSingleFile` + `SelfContained` + `win-x64`, plus
`<InvariantGlobalization>true</InvariantGlobalization>` — safe because all formatting uses
`CultureInfo.InvariantCulture`, and it removes ICU.

`PublishTrimmed` is off: publishing with it on fails with IL2026 over `ValidateDataAnnotations`,
`MaxLength` and `MinLength`. Native AOT is out of scope; the Windows service hosting model and
the platform APIs used here are not comfortable candidates.

`--self-contained` without `-r` does not error on SDK 10.0.111 — it infers the current RID —
but `-r` stays for reproducibility.

## Things .NET 10 already does, so this project does not

- **Validation.** `AddValidation()` runs DataAnnotations in Minimal APIs. No hand-written
  required-field checks, no validation endpoint filters. The `InterceptorsNamespaces` property
  that most third-party articles prescribe is not needed on SDK 10.0.111.
- **OpenAPI.** `Microsoft.AspNetCore.OpenApi` covers it. No Swashbuckle, no NSwag.
- **Sessions.** Framework cookie auth answers 401 on API endpoints instead of redirecting, which
  removes the reason to write a session middleware.
- **`System.Threading.Lock`** instead of `lock` on a bare `object`.
- **`TimeProvider`** is *not* registered in DI by the framework, contrary to what is often
  written; `AddServiceInfrastructure()` registers it. Nothing calls `DateTime.UtcNow`.
- **The rate limiter rejects with 503 by default**, not 429. `RejectionStatusCode` is set
  explicitly.

`JsonSerializerOptions.Strict` is available and groups five hardening options —
`AllowDuplicateProperties: false`, `RespectNullableAnnotations`, `UnmappedMemberHandling:
Disallow`, `PropertyNameCaseInsensitive: false`, `RespectRequiredConstructorParameters` — which
turn duplicated or invented fields into explicit errors.

Minimal APIs deserialize over `PipeReader`. A custom `JsonConverter`, if one is ever added, has
to handle `Utf8JsonReader.HasValueSequence`:

```csharp
var span = reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan;
```

## Scripts

| Script                       | Purpose                                                        |
|------------------------------|----------------------------------------------------------------|
| `build.ps1`                  | Publishes to `.\publish`. Does not install                     |
| `install.ps1`                | Deploys and registers the service. Does not compile            |
| `update.ps1`                 | Replaces the binaries, keeps the data                          |
| `uninstall.ps1`              | Removes the policy, restores the registry, deletes the service |
| `uninstall.ps1 -Force`       | The same with no questions and nothing kept. Idempotent        |
| `status.ps1`                 | Service, port, health, policy, USB, database, logs             |
| `validate-blocking.ps1`      | Proves that a block blocks, against a harmless test executable |
| `interface-dom.mjs`          | The DOM harness (see `web-interface.md`)                       |
| `WindowsControlService.psm1` | Shared module: elevation check, paths, service waits           |

All PowerShell scripts require elevation and check for it.
