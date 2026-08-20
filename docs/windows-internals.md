# Windows internals

Almost nothing in this file can be derived from the official documentation. It describes the
Windows behaviour the `Platform/` layer depends on, including the cases where the platform
does something other than what its documentation says. Literal code appears where the detail
is fragile.

---

## 1. Running external processes

Used for `CiTool.exe` and for PowerShell. There is one implementation,
`Platform/ProcessRunner.cs`, behind `IProcessRunner`.

```csharp
public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;
    public bool TimedOut => ExitCode == -1;
}
```

Four constraints:

**Both streams must be drained in parallel.** A Windows pipe holds 4 KB. Reading `stdout` to
the end and then `stderr` deadlocks as soon as the child fills the buffer nobody is reading.

**`stdin` must be closed.** `CiTool` without `-json` prints "Press Enter to Continue" and
waits. Closing stdin gives it EOF.

**The timeout kills the process tree.** `powershell.exe` spawns children; killing only the
parent leaves orphans. Reading `ExitCode` of a live process throws, so a sentinel value stands
for "did not finish" (`-1`).

**Arguments go through `ArgumentList`, never concatenation.** The runtime applies the Windows
quoting rules.

```csharp
var psi = new ProcessStartInfo
{
    FileName = fileName,
    UseShellExecute = false,
    RedirectStandardInput = true,
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    CreateNoWindow = true,          // without this a service opens console windows
};
foreach (var argument in arguments) psi.ArgumentList.Add(argument);

using var process = Process.Start(psi) ?? throw new InvalidOperationException(...);
process.StandardInput.Close();

using var timeoutSource = new CancellationTokenSource(timeout);

// Start BOTH reads before waiting.
var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
var stderrTask = process.StandardError.ReadToEndAsync(timeoutSource.Token);

try
{
    await process.WaitForExitAsync(timeoutSource.Token);
    return new ProcessResult(process.ExitCode, await stdoutTask, await stderrTask);
}
catch (OperationCanceledException)
{
    try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
    return new ProcessResult(-1, await Drain(stdoutTask), await Drain(stderrTask));
}
```

The external `CancellationToken` does not abort a WDAC operation half way; only the internal
timeout kills the process. Interrupting `CiTool` during a policy update is worse than waiting,
which is why `HostOptions.ShutdownTimeout` sits above the process timeout (section 2).

---

## 2. WDAC

WDAC is enforced in the kernel through `ci.dll` and **stays in force while the service is
stopped or uninstalled**. AppLocker depends on `AppIDSvc`, which an administrator can stop.
Everyone using the machine shares one administrator account, so that difference decides the
whole design.

The consequence: an uninstall that does not remove the policy leaves a machine with blocked
applications and nothing on it that explains why. `uninstall.ps1` removes the policy and
verifies the removal.

### The deployment chain

```
XML (generated)  --ConvertFrom-CIPolicy-->  .bin  --CiTool --update-policy-->  active
```

`ConvertFrom-CIPolicy` belongs to the `ConfigCI` PowerShell module shipped with Windows 11.
There is no native API, so `powershell.exe` has to be launched.

`CiTool.exe` lives at `%SystemRoot%\System32\CiTool.exe`. Its absence is reported as
`PlatformUnavailable`.

Both operations are slow. With a 30 s timeout each, `HostOptions.ShutdownTimeout` must exceed
their sum — 70 s — or a stop request can cut a policy update in half and leave the system and
the database disagreeing.

### The XML

Validated against `%windir%\schemas\CodeIntegrity\cipolicy.xsd`.

- Namespace `urn:schemas-microsoft-com:sipolicy`.
- `PolicyType` is an **attribute** of `SiPolicy` with value `Base Policy`, not a child element.
- `PolicyTypeID` must not be emitted: it is a legacy element incompatible with declaring
  `PolicyID` and `BasePolicyID`.
- Element order matters: `VersionEx`, `PolicyID`, `BasePolicyID`, `PlatformID`, `Rules`,
  `EKUs`, `FileRules`, `Signers`, `SigningScenarios`, `UpdatePolicySigners`, `CiSigners`,
  `HvciOptions`, `Settings`.
- `PlatformID` is the fixed Windows GUID `{2E07F7E4-194C-4D20-B7C9-6F44A6C5A234}`.
- `PolicyID` and `BasePolicyID` must be equal and **stable across deployments**, so that
  `CiTool --update-policy` updates the same policy instead of installing a new one every time.
- UTF-8 without BOM. `XmlWriter` over a `StringBuilder` writes `encoding="utf-16"` into the
  declaration and `ConvertFrom-CIPolicy` then fails to read the file. Write to a
  `MemoryStream` with `new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)`.

Required options in `Rules`:

| Option                                     | Effect                                                 |
|--------------------------------------------|--------------------------------------------------------|
| `Enabled:Unsigned System Integrity Policy` | The policy is not signed                               |
| `Enabled:Advanced Boot Options Menu`       | Leaves an emergency exit                               |
| `Enabled:UMCI`                             | Required. Without it user-mode blocks are not enforced |
| `Enabled:Update Policy No Reboot`          | Updates without a reboot                               |

### A deny-only policy blocks everything

A WDAC policy containing only `Deny` rules does not behave as a blocklist; it behaves as an
allowlist and blocks everything. One `Allow` rule with `FileName="*"` is required **per signing
scenario**:

```xml
<FileRules>
  <Allow ID="ID_ALLOW_A_1" FileName="*" />   <!-- scenario 131, kernel -->
  <Allow ID="ID_ALLOW_A_2" FileName="*" />   <!-- scenario 12, user -->
  <Deny ID="ID_DENY_D_1" FriendlyName="Brave" FileName="brave.exe" />
</FileRules>
```

| `Value` | Scenario                   | Contents                                                     |
|---------|----------------------------|--------------------------------------------------------------|
| `131`   | Kernel Mode Code Integrity | The allow rule only. No deny rules — drivers are not blocked |
| `12`    | User Mode Code Integrity   | The other allow rule and every deny rule                     |

Each `SigningScenario` carries `ProductSigners` > `FileRulesRef` > `FileRuleRef RuleID="..."`.

Rule ids follow an XSD pattern: `DenyType` requires `ID_DENY_[A-Z][_A-Z0-9]*`, so the first
character after `ID_DENY_` must be a letter. The format used is `ID_DENY_D_{id}`;
`ID_DENY_{id}` fails validation for numeric ids.

`Deny` carries no `MinimumFileVersion`. Omitting it applies the rule to every version of the
file, which is the intent, and matches what `New-CIPolicyRule -Deny` generates.

`Settings` > `PolicyInfo` > `Information` > `Name` supplies the name shown by
`CiTool --list-policies`.

A unit test validates the generated XML against the XSD.

### Deny matches `OriginalFilename`, not a path

`FileName=` compares against the `OriginalFilename` field embedded in the PE version resource,
not against the name of the file on disk. Renaming the executable therefore does not defeat the
block.

Not every binary carries the field. A rule built from the on-disk name matches nothing, and the
policy deploys without error while reporting protection that does not exist. The service reads
`OriginalFilename` → `InternalName` → `ProductName` and **refuses the block when none of the
three is present**. `Hash` would work but breaks on every application update; `FilePath` is
defeated by moving the file.

### Reading the version resource: the MUI trap

`FileVersionInfo.GetVersionInfo()` calls `GetFileVersionInfoW`, which follows MUI redirection
and returns `NOTEPAD.EXE.MUI` instead of `Notepad.exe` for system binaries. WDAC reads the
field without MUI, so a rule generated from `FileVersionInfo` does not match.

The value must be read through `GetFileVersionInfoSizeExW` / `GetFileVersionInfoExW` with
`FILE_VER_GET_NEUTRAL` (`0x02`):

```csharp
private const uint FileVerGetNeutral = 0x02;

[DllImport("version.dll", EntryPoint = "GetFileVersionInfoSizeExW",
    CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
private static extern uint GetFileVersionInfoSizeEx(uint flags, string filePath, out uint handle);

[DllImport("version.dll", EntryPoint = "GetFileVersionInfoExW",
    CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
private static extern bool GetFileVersionInfoEx(uint flags, string filePath, uint handle,
    uint bufferLength, IntPtr buffer);

[DllImport("version.dll", EntryPoint = "VerQueryValueW",
    CharSet = CharSet.Unicode, ExactSpelling = true)]
private static extern bool VerQueryValue(IntPtr block, string subBlock,
    out IntPtr valuePointer, out uint valueLength);
```

The value sits at `\StringFileInfo\{lang:X4}{codepage:X4}\OriginalFilename`. The translations
in `\VarFileInfo\Translation` (4 bytes per entry: 2 language, 2 code page) have to be walked
until one answers. When nothing answers, the fallback is
`Path.GetFileName(executablePath)`.

These stay as `DllImport` with an explicit `EntryPoint` and `ExactSpelling = true`.
`LibraryImport` would require `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` across the whole
project, and its benefit is trimming and AOT compatibility, neither of which is used here.

A unit test reads `%windir%\system32\notepad.exe` and asserts the result is `NOTEPAD.EXE` and
does not end in `.MUI`.

### Querying state: valid JSON is not success

```
CiTool.exe --list-policies -json
```

`-json` avoids the interactive prompt.

**`CiTool` reports failure as well-formed JSON.** On error it prints an object carrying only an
HRESULT:

```json
{"OperationResult":-2147024891}
```

(that one is access denied). Code that only checks for a `Policies` property reads every
`CiTool` failure as "no policy is installed", and the reconciliation worker then reinstalls the
policy in a loop forever.

The check order is:

1. Exit code other than 0 → failure.
2. `OperationResult` present and non-zero → failure.
3. `Policies` missing or not an array → failure.
4. Walk `Policies` looking for `PolicyID`, compared case-insensitively.

`PolicyID` values come back **without braces**, so `{}` has to be trimmed before comparing.
`IsEnforced` is a JSON boolean, not a string.

### Three states, and the third one matters

```csharp
public enum PolicyState
{
    Unknown,      // CiTool could not be queried — do not act on it
    NotEnforced,  // not installed, or installed and not enforced
    Enforced
}
```

Without `Unknown`, a permissions failure reads as "there is no policy" and the guard reapplies
it every 60 seconds indefinitely. On `Unknown` the reconciliation worker logs a warning and
does nothing.

### Removing the policy

`CiTool --remove-policy {GUID}` **fails when the policy is not installed**, which is not a
failure from the caller's point of view. The state is queried first, and "there was nothing to
remove" returns success.

From PowerShell the same call hangs the script: without `-json`, `CiTool` prints
"Press Enter to Continue" and waits on stdin, and invoked with `&` it inherits the console and
blocks indefinitely. Piping to `Out-Null` hides the prompt as well. In C# this is solved by
`process.StandardInput.Close()`; in PowerShell the reliable form is to supply EOF through
`cmd`:

```powershell
cmd.exe /c "`"$ciTool`" --remove-policy `"{$policyId}`" <nul" | Out-Null
```

### Write order

A failure must never leave the system and the database disagreeing.

| Operation            | Order                                                     | Constraint                                                                                                           |
|----------------------|-----------------------------------------------------------|----------------------------------------------------------------------------------------------------------------------|
| **Add**              | insert row → apply policy → on failure **delete the row** | The deny rule id derives from the row id, so the policy cannot be built before the insert. It is compensated instead |
| **Remove**           | apply the resulting policy → on success delete the row    | The reverse order leaves a phantom block with no row explaining it                                                   |
| **Enable / disable** | apply the projected policy → on success update the row    | As for remove                                                                                                        |

Removing a **disabled** application does not change the policy, so its row can be deleted
without calling `CiTool`.

When disabling or removing empties the list, the correct operation is to **remove the policy**,
not to apply an empty one:

```csharp
private Task<Result> ApplyPolicyForAsync(IReadOnlyList<BlockedApplication> enabled, CancellationToken ct) =>
    enabled.Count == 0
        ? codeIntegrity.RemovePolicyAsync(ct)
        : codeIntegrity.ApplyPolicyAsync(enabled, ct);
```

### Reconciliation

Every minute, inside the sequential executor:

1. `Unknown` → warn and exit.
2. No enabled applications in the database and a policy applied → **remove** the policy. The
   database is the source of truth for configuration.
3. Enabled applications and no policy applied → **reapply**.
4. Otherwise do nothing.

The first pass runs at startup, not one interval later: `do/while` with `PeriodicTimer`, not
`while`.

### Side effect: PowerShell drops to ConstrainedLanguage

One active block is enough. Measured with the service policy in enforcement:

```
                          no policy        policy in force
interactive console 5.1   FullLanguage     ConstrainedLanguage
interactive console 7     FullLanguage     ConstrainedLanguage
.ps1 file (5.1 and 7)     FullLanguage     FullLanguage
[System.IO.File] typed    allowed          blocked
[System.IO.File] in .ps1  allowed          allowed
```

The distinction is not PowerShell but where the code comes from: anything typed or passed with
`-Command` has no provenance and is restricted, while a `.ps1` on disk that the policy allows
runs in full. This is why the repository scripts keep working despite using
`[Security.Principal.WindowsPrincipal]::new()` and `Add-Type`.

Two practical consequences:

- The mode is fixed when the process starts. A console opened **before** the policy was applied
  does not change, and one opened after it was removed stays restricted until it is closed.
- Automation that needs .NET types must be written as a `.ps1` file, not as a `-Command`
  string.

---

## 3. Blocking USB storage

Key `HKLM\SYSTEM\CurrentControlSet\Services\USBSTOR`, value `Start` (`REG_DWORD`).

| Value | Meaning                                              |
|-------|------------------------------------------------------|
| `3`   | Manual — normal operation, drives mount              |
| `4`   | Disabled — the driver does not start, nothing mounts |

Blocking writes `4`, unblocking writes `3`. Administrator rights are required; without them the
write throws `UnauthorizedAccessException`, which maps to `AccessDenied` rather than a generic
error.

**State is always read from the registry**, never from the database. The database stores only
the timestamp of the last change made through the service, as metadata.

A secondary key, `HKLM\SYSTEM\CurrentControlSet\Control\StorageDevicePolicies`, value
`WriteProtect`, forces read-only. It is defence in depth and **may not exist**: it is created
when needed and a failure there is a warning, not an error.

Blocking `USBSTOR` prevents new drives from mounting; it does not unmount drives that are
already mounted. Ejecting them is not implemented: the obvious route — launching
`powershell.exe` to instantiate `Shell.Application` and invoke the `Eject` verb — does not work
in this context, because `Shell.Application` is interactive shell COM and the service runs as
LocalSystem in session 0, where there is no shell.

Tests that touch these values read the original value first and restore it in a `finally`, even
when the test fails.

---

## 4. Access history

### The channel

```
Microsoft-Windows-TerminalServices-LocalSessionManager/Operational
```

Not the `Security` log. On a real machine `Security` was saturated — 20/20 MB, overwriting
continuously — and retained a few hours. The Terminal Services channel retained **160 days in
1 MB**, because it only receives session transitions.

| Event ID | Meaning    |
|----------|------------|
| 21       | Logon      |
| 23       | Logoff     |
| 24       | Disconnect |
| 25       | Reconnect  |

21 and 25 open a session; 23 and 24 close one.

`System.Diagnostics.Eventing.Reader` is part of the framework for `net10.0-windows`. The
`System.Diagnostics.EventLog` package must not be added: it produces NU1510 as a redundant
reference.

### Filter on the server with XPath

```csharp
var xpath =
    $"*[System[(EventID=21 or EventID=23 or EventID=24 or EventID=25) and " +
    $"TimeCreated[timediff(@SystemTime) <= {(long)window.TotalMilliseconds}]]]";

var query = new EventLogQuery(Channel, PathType.LogName, xpath) { TolerateQueryErrors = true };
using var reader = new EventLogReader(query);
while (reader.ReadEvent() is { } record)
{
    using (record) { /* parse */ }
}
```

`timediff(@SystemTime)` yields milliseconds from the event to now.

### Parse by element name, not by position

**Event 23 does not carry the `Address` field.** Reading the data by index shifts every
following field. The event XML is walked by element name (`User`, `SessionID`, `Address`) and
missing elements are accepted.

Origin is classified from `Address`:

| `Address`             | Origin    |
|-----------------------|-----------|
| absent or empty       | `Unknown` |
| `LOCAL`               | `Local`   |
| anything else (an IP) | `Remote`  |

The reader never throws. A missing channel, missing permissions or unexpected XML is logged and
yields an empty list.

### Persistence: the unique key carries the date

`(Channel, RecordId, OccurredAt)`.

`RecordId` alone is not enough: **clearing the event log resets the counter to 1**, and without
the date in the key new events would collide with old ones and be discarded silently as
duplicates.

### Ingestion re-reads the whole window

Every minute the full 30-day window is re-read and inserted with `INSERT OR IGNORE`. The
measured volume is around 3 events a day (~90 rows in 30 days). This removes all recovery code:
if the service was off for a month, if the database was deleted, or if someone cleared the
Windows log, the next cycle leaves the history correct.

Ingestion runs on a timer, never during an HTTP request. Depending on someone opening the
interface would leave permanent gaps as soon as Windows rotated the log.

### Derived fields are computed on read

A session's duration and the origin a closing event inherits are relations **between** events,
and that relation changes as new events arrive. Storing them would store a conclusion with an
expiry date.

The algorithm walks events in ascending order, keeping per session the instant of the last
opening event and the last known origin and direction. On a closing event:

1. With a recorded start, the duration is the difference — **unless it exceeds the plausible
   maximum** of 7 days, in which case there is no duration. An absurd interval usually means
   the real start fell outside the window.
2. With no recorded start there is no duration.
3. Origin and direction are inherited from that session's start, because event 23 does not
   carry them.

Sessions are grouped by `SessionId`, with `?? -1` standing in when it is missing. Concurrent
sessions must not be mixed: each pairs with its own start.

The origin filter is applied **after** deriving, not before: a logoff with no `Address` of its
own has to be able to match the `remote` filter through the origin it inherited.

Paging is by `offset`, ordered newest first.

---

## 5. Process inventory

`Process.GetProcesses()`, filtered:

- Paths ending in `.exe` only.
- The service's own executable excluded (`Environment.ProcessPath`).
- `C:\Windows\` excluded.
- Grouped by path, with no duplicates when several instances run.

The Windows prefix must carry a trailing separator. Comparing against `"C:\Windows"` also
excludes sibling directories that start the same way — `C:\Windows.old`, left by an in-place
upgrade, is the realistic case. Use
`Path.TrimEndingDirectorySeparator(...) + Path.DirectorySeparatorChar`.

`process.MainModule` throws for protected processes and processes in other sessions. Each
iteration is wrapped in `try/catch` and continues, and each `Process` is disposed in a
`finally`.

The display name comes from `FileVersionInfo.FileDescription` — usable here because it is only
shown, never used to generate rules — falling back to `Path.GetFileNameWithoutExtension`.

---

## 6. Hosting as a Windows service

- `builder.Host.UseWindowsService(options => options.ServiceName = ...)`.
- `UseUrls` must not receive a bare constant. Fixing the port at compile time prevents starting
  a second instance for tests:
  ```csharp
  builder.WebHost.UseUrls(builder.Configuration["urls"] ?? DefaultUrl);
  ```
  `UseUrls` takes precedence over `ASPNETCORE_URLS`, `--urls` and `launchSettings`, so this is
  the only place the address is decided.
- `HostOptions.ShutdownTimeout` above the worst possible WDAC operation: 70 s.
- The data directory is created on **every** branch, including when it arrives as an argument.
  A `--data-dir` pointing at a directory that does not exist fails later with an opaque SQLite
  error.
- Event Log logging has a startup trap; see `architecture.md`.
