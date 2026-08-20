# HTTP API

Base `http://localhost:5150`. JSON. The listener binds to loopback only.

An OpenAPI 3.1.1 document is served at `/openapi/v1.json` and `/openapi/v1.yaml`. This file
covers what the generated document cannot state: the meaning of the fields and the conditions
attached to them.

## Rules that apply everywhere

Every error is `application/problem+json` (RFC 9457). Validation errors are produced by the
framework and already use that shape:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": { "ExecutablePath": ["The ExecutablePath field is required."] }
}
```

Business errors carry the reason in `detail`:

```json
{ "title": "Conflict", "status": 409, "detail": "An entry for this executable already exists" }
```

| Status | Meaning                                                                    |
|--------|----------------------------------------------------------------------------|
| `400`  | Shape validation, or `ErrorCode.Invalid`                                   |
| `401`  | No session, or the session expired                                         |
| `403`  | `ErrorCode.AccessDenied` — administrator rights missing                    |
| `404`  | `ErrorCode.NotFound`                                                       |
| `409`  | `ErrorCode.Conflict`                                                       |
| `429`  | Login attempt limit exceeded                                               |
| `500`  | `ErrorCode.OperationFailed` — the operation failed and **nothing changed** |
| `503`  | `ErrorCode.PlatformUnavailable` — `CiTool` missing, registry key absent    |

Authentication is the `wcs_session` cookie: `HttpOnly`, `SameSite=Strict`, sliding expiry.
Public endpoints are `GET /api/health`, `GET /api/auth/session`, `POST /api/auth/password`
(only while no password exists) and `POST /api/auth/login`. Everything else requires a session.

All timestamps are UTC in ISO 8601.

---

## Health

### `GET /api/health` · public

```json
{ "status": "running", "version": "1.0.0+<commit>", "timestamp": "2026-08-17T10:30:00Z" }
```

`status` is a constant. The endpoint answering at all is the information; the value is not a
health assessment and must not be compared against.

---

## Authentication

### `GET /api/auth/session` · public

```json
{
  "initialized": true,
  "authenticated": false,
  "minimumPasswordLength": 6,
  "sessionTimeoutMinutes": 10,
  "requiresLettersAndDigits": true
}
```

`initialized` says whether a password has been configured; `authenticated`, whether the cookie
on this request is valid. Always `200`.

The last three are **service rules the interface has to obey while typing**: the counter
against the minimum and the letters-and-digits requirement in the new-password field, and the
expiry shown on the session card. They ride along here because this call already happens on
every load. A copy of them in the browser would be a second source of truth that stops agreeing
the day the rule changes, and nobody would notice until the service refused a password the
interface had accepted.

They are answered without a session on purpose: they reveal nothing that trying a short
password would not, and the alternative is learning the rule only after being refused.

### `POST /api/auth/password` · public only while no password exists

```json
{ "password": "at-least-6-mixing-letters-and-digits" }
```

`200` · `400` (fails the policy) · `409` (already configured).

The policy is enforced on the server, not only in the client: with a client-side check alone a
`curl` can leave an empty password permanently.

### `POST /api/auth/login` · public · 5 attempts per minute

```json
{ "password": "..." }
```

`200` with `Set-Cookie` · `401` · `429`.

An absent or null password is a **failed login**, not a `500`.

### `POST /api/auth/logout`

Invalidates the session and clears the cookie. `200`.

### `PUT /api/auth/password`

```json
{ "currentPassword": "...", "newPassword": "..." }
```

`200` · `400` · `401`.

Requires the current password **in addition to** a valid session: the machine is shared,
and a browser left signed in must not be enough to take the service over.

Changing it rotates the security stamp, which invalidates **every open session**, including the
one that made the change. This is the only way to end a session held by another browser.

---

## Application blocking

### `GET /api/applications`

```json
[
  {
    "id": 1,
    "name": "Brave",
    "executablePath": "C:\\Program Files\\...\\brave.exe",
    "matchAttribute": "FileName",
    "matchValue": "brave.exe",
    "productName": "Brave Browser",
    "isEnabled": true,
    "createdAt": "2026-08-17T10:00:00Z"
  }
]
```

`matchAttribute` is the WDAC attribute the rule compares against — `FileName`, `InternalName`
or `ProductName` — and `matchValue` is the value read from the binary's version resource. Both
are exposed because not everything is blocked by `FileName`: that attribute compares against
the embedded `OriginalFilename`, and some executables do not carry one.

### `POST /api/applications`

```json
{ "executablePath": "C:\\...\\brave.exe", "name": "Brave" }
```

`201` with `{ "id": 1 }` and a `Location` header · `400` (invalid path, missing file, **or a
binary with no version resource**) · `409` (duplicate) · `500` (the policy could not be
applied; **nothing was registered**) · `503` (`CiTool` missing).

The path is normalised with `Path.GetFullPath` before it is stored, so `C:\App\x.exe` and
`C:\App\..\App\x.exe` cannot become two entries generating two rules for one executable.
Duplicate comparison is case-insensitive.

### `GET /api/applications/{id}`

`200` · `404`.

### `PATCH /api/applications/{id}`

```json
{ "enabled": false }
```

`200` · `400` (`enabled` missing) · `404` · `500`.

Disabling does not delete the entry; it rebuilds the policy without that rule.

### `DELETE /api/applications/{id}`

`204` · `404` · `500`.

On `500` the entry **still exists and the application is still blocked**: the delete is
committed to the database only after the system accepts the new policy.

### `GET /api/applications/policy-state`

```json
{ "state": "Enforced", "enabledRuleCount": 3, "lastReconciledAt": "2026-08-17T10:00:00Z" }
```

`state` is `Unknown`, `NotEnforced` or `Enforced`, serialized as the name. `Unknown` means
`CiTool` could not be queried, usually for want of privileges, and a client has to be able to
tell it apart from "there is no policy". `lastReconciledAt` is `null` until the worker has
completed one cycle.

---

## Device control

### `GET /api/devices/usb`

```json
{ "blocked": false, "lastModified": "2026-08-17T08:00:00Z" }
```

`blocked` is read **from the registry**, which is the source of truth. `lastModified` is
database metadata and may be `null`.

### `PUT /api/devices/usb`

```json
{ "blocked": true }
```

`200` · `400` (`blocked` missing) · `403` (no privileges) · `503` (key absent).

The field is required: an empty body is rejected rather than read as `false`.

---

## Processes

### `GET /api/processes`

Running processes whose executable is reachable and outside `C:\Windows\`, grouped by path.

```json
[{ "name": "Brave Browser", "executablePath": "C:\\...\\brave.exe", "productName": "Brave Browser" }]
```

---

## Access history

### `GET /api/access-history`

| Parameter | Type   | Default | Notes                                             |
|-----------|--------|---------|---------------------------------------------------|
| `limit`   | int    | `10`    | Clamped to 1–500                                  |
| `offset`  | int    | `0`     | Negative is treated as `0`                        |
| `origin`  | string | (all)   | `local`, `remote` or `all`. Anything else → `400` |

```json
{
  "total": 113,
  "entries": [
    {
      "id": 412,
      "occurredAt": "2026-08-17T01:20:14Z",
      "kind": "Reconnect",
      "startsSession": true,
      "origin": "Remote",
      "address": "203.0.113.2",
      "userName": "PC\\owner",
      "sessionId": 2,
      "durationSeconds": null
    }
  ]
}
```

`kind` is `Logon`, `Reconnect`, `Disconnect` or `Logoff`. `origin` is `Local`, `Remote` or
`Unknown`.

`startsSession` says whether the entry **opens** a session or closes one. It travels in the
response instead of being derived from `kind` by the client: which event ids open a session is
a fact about Windows that the service already owns, and it is the same rule
(`LogonEvent.IsSessionStart`) it uses to pair each close with its start for `durationSeconds`.
A client that derives it again is a second copy of that rule.

All four kinds are four different things to a reader — signing out and losing an RDP connection
are not the same event. The **direction** is binary; the **label** is not.

`durationSeconds` appears only on entries that close a session, and is `null` when the start
fell outside the window or the interval is not plausible.

`total` is the number of entries matching the current filter, not the number returned. Page N
is requested with `offset = N * limit`.

Answered **from the database only**. The Windows log is read by a background worker, never
during a request.

---

## Event stream

### `GET /api/events`

One `text/event-stream` carrying everything the interface needs without asking. Requires a
session.

```
event: policy-state
data: {"state":"Enforced","enabledRuleCount":3,"lastReconciledAt":"2026-08-18T01:00:00Z"}

event: usb
data: {"blocked":true,"lastModified":"2026-08-18T01:00:00Z"}

event: access-history
data: {"total":118}
```

The bodies are the **same** ones returned by `GET /api/applications/policy-state`,
`GET /api/devices/usb` and the `total` of `GET /api/access-history`. A client does not learn
two representations of one thing.

On connect, the current state of all three is sent, so opening a section needs no separate GET.

**The stream closes itself** after `Events:StreamLifetime` (5 minutes by default). The session
cookie has a sliding expiry and an open stream sends no further requests, so a tab left open for
hours would find the session dead on the first click. When the stream closes the browser
reconnects on its own, and that reconnection **is** an authenticated request that renews the
cookie.

If the session has already expired, the reconnection receives 401 and the browser gives up:
`EventSource` moves to `CLOSED` and does not retry, silently. The interface treats that
`CLOSED` like any other 401.

A failure to read state is **not** published. The stream carries state, not errors: whoever
asks for something receives its `problem+json`, and an error event would give a client a second
way to learn the same thing.

---

## OpenAPI

`AddOpenApi()` + `MapOpenApi()`. With `<GenerateDocumentationFile>true</GenerateDocumentationFile>`
the XML comments reach the document.

In the generated output an `int` appears as `"type": ["integer","string"]` with a `pattern`,
because the default ASP.NET Core JSON options accept numbers written as strings. That is
faithful to what the endpoint accepts.
