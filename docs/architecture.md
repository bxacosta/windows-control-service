# Architecture

## Shape

One `Microsoft.NET.Sdk.Web` process that is at once a Windows service, a REST API and the
server of its own web interface. It is not split into a privileged worker and an unprivileged
API over a named pipe: for one machine, one user and three features that multiplies the moving
parts without buying anything.

```
src/WindowsControlService/
├── Features/                 one vertical slice per feature
│   ├── AccessHistory/        ingestion, session pairing, timeline
│   ├── ApplicationBlocking/  WDAC: document, service, worker, endpoints
│   ├── Authentication/       password, cookie, security stamp
│   ├── DeviceControl/        USB storage
│   └── Health/
├── Platform/                 everything that talks to Windows, behind interfaces
│   ├── ICodeIntegrityTool.cs / CodeIntegrityTool.cs
│   ├── ILogonEventSource.cs / LogonEventSource.cs
│   ├── IPortableExecutableReader.cs / PortableExecutableReader.cs
│   ├── IProcessInventory.cs / ProcessInventory.cs
│   ├── IProcessRunner.cs / ProcessRunner.cs
│   └── IUsbStorageSwitch.cs / UsbStorageSwitch.cs
├── Infrastructure/
│   ├── Database/             SQLite, Dapper, embedded DbUp migrations
│   ├── Events/               the SSE broadcaster and its snapshots
│   ├── Hosting/              data directory, logging, sequential executor, clock
│   └── Results/              Result, Error, and the single mapping to HTTP
├── wwwroot/                  the interface (see web-interface.md)
└── Program.cs

tests/
├── WindowsControlService.UnitTests/
├── WindowsControlService.IntegrationTests/
└── interface/                the interface rules, run by node --test
scripts/                      build, install, update, uninstall, status, validation, the DOM harness
docs/
```

A file belongs to the folder of the feature it talks about, not to the folder of the kind of
thing it is. Each feature exposes exactly two extension methods, and `Program.cs` reads as a
list of what is switched on:

```csharp
builder.Services
    .AddAuthenticationFeature(builder.Configuration)
    .AddApplicationBlocking(builder.Configuration)
    .AddDeviceControl(builder.Configuration)
    .AddAccessHistory(builder.Configuration);

app.MapAuthenticationFeature();
app.MapApplicationBlocking();
app.MapDeviceControl();
app.MapAccessHistory();
```

Removing a feature is deleting a folder and two lines. `Program.cs` numbers its steps because
the order matters: logging before anything that can fail, migrations before serving,
`UseAuthentication` before `UseAuthorization`.

## Results, not a mixture of bool, enum and exceptions

`Result` / `Result<T>` carry **expected** failures. Anything unexpected travels as an exception
to `UseExceptionHandler` with its stack trace intact.

```csharp
public enum ErrorCode
{
    NotFound,
    Conflict,
    Invalid,
    AccessDenied,         // administrator rights missing
    PlatformUnavailable,  // CiTool absent, registry key missing, log unreadable
    OperationFailed       // the platform operation failed; nothing changed
}

public readonly record struct Error(ErrorCode Code, string Message);
```

One place translates to HTTP, `ErrorHttpExtensions`:

| `ErrorCode`           | Status |
|-----------------------|--------|
| `NotFound`            | 404    |
| `Conflict`            | 409    |
| `Invalid`             | 400    |
| `AccessDenied`        | 403    |
| `PlatformUnavailable` | 503    |
| `OperationFailed`     | 500    |

Its `switch` has **no `default` arm**. Adding an `ErrorCode` without mapping it breaks the
build with CS8509 instead of answering 500 in silence.

An `Error` message never carries internal detail or system paths. The message is what the user
reads; diagnosis goes to the log.

Every API error is `application/problem+json` (RFC 9457). There is no second error shape.

## Validation

.NET 10 runs DataAnnotations in Minimal APIs, so there are no hand-written required-field
checks and no validation endpoint filters:

```csharp
builder.Services.AddValidation();

public sealed record AddApplicationRequest(
    [Required, MaxLength(260)] string ExecutablePath,
    [Required, MaxLength(100)] string Name);
```

The result is 400 with `problem+json` and an `errors` dictionary. The
`InterceptorsNamespaces` property that most third-party documentation prescribes is **not**
needed on SDK 10.0.111; that instruction belongs to earlier previews.

DataAnnotations cover shape, not business rules. "The executable exists on disk" and "that path
is already registered" belong to the service and return an `Error`.

## Configuration

One options class per feature, bound and validated at startup:

```csharp
builder.Services
    .AddOptions<AccessHistoryOptions>()
    .Bind(configuration.GetSection(AccessHistoryOptions.Section))
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

`ValidateOnStart()` turns "the service started but the interval is zero and the worker spins"
into "the service did not start, and the reason is written down".

| Option                                          | Default       |
|-------------------------------------------------|---------------|
| `ApplicationBlocking:ReconciliationInterval`    | `00:01:00`    |
| `CodeIntegrity:OperationTimeout`                | `00:00:30`    |
| `AccessHistory:IngestionInterval`               | `00:01:00`    |
| `AccessHistory:IngestionWindow`                 | `30.00:00:00` |
| `AccessHistory:MaxPlausibleSessionLength`       | `7.00:00:00`  |
| `AccessHistory:DefaultPageSize` / `MaxPageSize` | `10` / `500`  |
| `Authentication:SessionTimeout`                 | `00:10:00`    |
| `Authentication:MinimumPasswordLength`          | `6`           |
| `Authentication:Pbkdf2Iterations`               | `210000`      |
| `Authentication:LoginAttemptsPerMinute`         | `5`           |
| `Events:StreamLifetime`                         | `00:05:00`    |
| `Database:BusyTimeout`                          | `00:00:05`    |

`Events:StreamLifetime` must stay shorter than `Authentication:SessionTimeout`; `Program.cs`
validates the pair at startup, because an open stream outliving the cookie would keep pushing
to a session that no longer exists.

Service identity, registry paths and the policy GUID are genuine constants: `const` inside the
feature that uses them, not in a shared class.

## The clock is injected

`TimeProvider` is not registered in DI by the framework;
`Infrastructure/Hosting/HostingModule.AddServiceInfrastructure()` registers it. **No service
calls `DateTime.UtcNow`.** With `FakeTimeProvider` the session expiry, the 30-day window and
duration pairing are testable without waiting.

## One lock, not several

Every operation that mutates machine state — applying or removing a policy, writing the
registry — is serialized through `ISequentialExecutor`, registered as a singleton and used by
both endpoints and workers. What it serializes is the whole read-decide-apply-write block, not
just the call to `CiTool`.

```csharp
public interface ISequentialExecutor
{
    Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct);
}
```

Calling `RunAsync` from inside `RunAsync` is a deadlock; the executor detects it and throws
`InvalidOperationException`. The re-entrancy marker is **per instance** — a static one gives a
false positive when two different executors nest, and two executors are two semaphores that
cannot block each other.

## Data

- **SQLite** through `Microsoft.Data.Sqlite`, WAL enabled.
- **Dapper** for mapping. Columns are never read by index.
- **DbUp** (`dbup-sqlite`) for migrations: numbered `.sql` scripts embedded in the assembly
  with `WithScriptsEmbeddedInAssembly`, table `SchemaVersions`. Embedded rather than loose so
  `PublishSingleFile` still produces one file.
- `IDbConnectionFactory` is injected, never a hand-passed connection string. That is what lets
  tests point at a temporary database.
- The connection string is built with `SqliteConnectionStringBuilder`, never interpolated: a
  directory containing `;` or `"` would inject into the string. `DefaultTimeout = 5` covers the
  retry window on `SQLITE_BUSY`.

Repositories are `async` and take a `CancellationToken`.

## Logging

Serilog, with two sinks: a rolling file in the data directory, and the Windows Event Log from
`Warning` up.

**Both destinations must be Serilog sinks.** Registering Serilog installs its own
`ILoggerFactory`, and that factory does not forward to the other `ILoggerProvider` instances in
the container. It makes no difference whether `UseWindowsService()` registers the Event Log
provider or whether `builder.Logging.AddEventLog(...)` adds it by hand: it is registered and
nobody consults it. The symptom is misleading, because the Event Viewer keeps showing
"Service started/stopped successfully" — written by `ServiceBase.AutoLog` through another path
— so the sink looks alive while not one application log arrives. The Event Viewer sink is
`Serilog.Sinks.EventLog` with `restrictedToMinimumLevel: LogEventLevel.Warning`. See
`Infrastructure/Hosting/LoggingExtensions.cs`.

This matters most when it hurts most: if the failure is creating the data directory or running
the migrations, the file sink may never get to write.

**The event source is not created at run time.** Registering it requires administrator rights
and is the installer's job. When it does not exist the service still starts, without the Event
Log sink, rather than refusing to start because of logging. `uninstall.ps1` deletes it.

File timestamps are UTC. In the Event Viewer `TimeCreated` is set by Windows in local time and
no application can change it, so the message also carries the UTC stamp, which is what allows
the two destinations to be correlated.

## Authentication

Framework cookie authentication.

```csharp
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "wcs_session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // HTTP on localhost
        options.ExpireTimeSpan = /* Authentication:SessionTimeout */;
        options.SlidingExpiration = true;
        options.Events.OnValidatePrincipal = ValidateSecurityStampAsync;
    });
```

In .NET 10 API endpoints answer `401`/`403` directly instead of redirecting to a login page,
detected through `IApiEndpointMetadata`. The 401 still carries a `Location` header; a browser
does not follow it.

There is no concept of a user and no user name anywhere. Sessions are not tracked
server-side: the cookie is signed and the service keeps no register of the cookies it issued,
so it cannot enumerate or revoke one session. What it can do is invalidate **all** of them at
once through a **security stamp** — a random value stored beside the password and issued as a
claim. `OnValidatePrincipal` compares the claim against the stored value and rejects the
principal when they differ. Changing the password rotates the stamp.

A side effect is that sessions survive a service restart.

Login is rate limited:

```csharp
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;  // the default is 503
    options.AddFixedWindowLimiter(LoginPolicy, limiter => { /* from Options */ });
});
```

Without `RejectionStatusCode` the rejection goes out as 503, which a client reads as "the
service is down" instead of "you are going too fast". `AddFixedWindowLimiter` lives in
`Microsoft.AspNetCore.RateLimiting`, not in `System.Threading.RateLimiting`; both `using`
directives are needed.

## Patterns that do not change

- Expected failures are values, never exceptions.
- No service reads the clock directly.
- No domain service talks to Windows directly; the registry, `CiTool`, the event log,
  processes and PE files live behind interfaces in `Platform/`.
- No operational value is hard-coded. Intervals, windows, timeouts, page sizes and hashing
  parameters live in `IOptions<T>` validated with `ValidateOnStart()`.
- One place translates errors to HTTP. Endpoints do not build `Results.Problem` by hand.
- The write order never leaves the system and the database disagreeing (see
  `windows-internals.md`).
- Derived fields are computed on read.
- Code, comments, names and log messages are in English.

## Known limits

- `ILogonEventRepository.GetAllAscendingAsync` loads the whole table and `GetTimelineAsync`
  pages in memory. Duration and origin derive from neighbouring events, because event 23
  carries neither. At the measured volume (~4 events a day, ~1,500 rows a year) this is
  irrelevant, and it stops being irrelevant at tens of thousands of rows. The limit is also
  noted in the code.
- Blocking `USBSTOR` stops new drives from mounting; it does not unmount drives already
  mounted. A drive already connected keeps working until it is unplugged or the machine
  restarts.
- The policy is unsigned, which is what the reconciliation worker exists for.
- `PublishTrimmed` is off: publishing with it on fails with IL2026 over
  `ValidateDataAnnotations`, `MaxLength` and `MinLength` — options validation, not
  `Microsoft.Data.Sqlite`.
- What is published is not literally one file: `e_sqlite3.dll` and
  `aspnetcorev2_inprocess.dll` sit beside the `.exe`. Without the first, the process does not
  start.
