# Web interface

Plain ES modules served from `wwwroot`, no framework and no build step. `DESIGN.md` at the
repository root holds the design system the stylesheet transcribes; this file holds the
behaviour.

**Criterion for any presentation change:** it may rewrite `index.html`, `app.css` and any
function that builds DOM. The `[check]` captures of `scripts/interface-dom.mjs` must keep
producing the same values. Their count grows as rules are added; the number that matters is
zero changed.

## Where things live

| File                                                         | What it is                                                               | A redesign touches it                     |
|--------------------------------------------------------------|--------------------------------------------------------------------------|-------------------------------------------|
| `api.js`, `events.js`, `format.js`                           | The boundary with the service. No DOM references                         | No                                        |
| `rules.js`                                                   | The decisions. Pure functions, no DOM, importing nothing but `format.js` | Only if what the interface *says* changes |
| `markup.js`                                                  | The contract with `index.html`: ids per section, classes, attributes     | Only when renaming ids or classes         |
| `pending.js`, `notices.js`, `session.js`, `dom.js`           | Cross-cutting                                                            | Should not                                |
| `applications.js`, `devices.js`, `history.js`, `settings.js` | Handlers and renderers                                                   | The renderers, yes                        |
| `shell.js`                                                   | Top bar, tab rail, and the indicators on them                            | Its markup only                           |
| `index.html`, `app.css`                                      | Presentation                                                             | Yes                                       |

`el()` and `replace()` in `dom.js` are the primitive: everything shown here comes from the
machine, and `textContent` cannot be talked into being markup.

Two module boundaries are enforced by tests: only `api.js` calls `fetch`, and only `events.js`
opens an `EventSource`. One door for every request is what makes "a lost session lands on the
gate, once" a code path instead of a habit.

## Behaviour rules

Each rule states the constraint, where it lives, and the scenario that proves it.

### 1. An optimistic switch reverts when the service refuses

Writing the registry or applying a policy takes seconds; waiting for the answer before moving
the switch reads as a dead click. A switch left moved after a refusal is a screen lying about
the machine.

`pending.js` → `optimistic(control, action)` reads the requested value, runs the action and puts
the control back if it throws. The error is rethrown: what to say about it belongs to the
caller.

While the request is in flight the switch shows it. The thumb moves on the click and the track
drops to `switch-pending`, which is neither of its two settled colours. No spinner: the thumb
already says what was asked.

*applications · a refused toggle snaps the switch back*, *devices · a refused write snaps the
switch back*.

### 2. A failed `DELETE` keeps the row

If the `DELETE` failed the policy did not change, so the application is still blocked and its
row is still true.

`rules.js` → `describeRemoval(name, failure)` returns `reload: true` in both branches. The list
is always re-read from the service and never edited in place.

*applications · a failed removal keeps the row*, and in `node --test`, *a removal reloads the
list whether it succeeded or not*.

### 3. `Unknown` is not "nothing is blocked"

Three states, not two. `Unknown` means the service could not ask Windows. Collapsing it into
"not enforced" would tell the administrator the machine is unprotected when the truth is that
nobody knows.

`rules.js` → `describePolicyState(state)` returns `{ tone, headline, detail, checked, icon }`.
A state this version does not know is shown verbatim rather than mapped onto one it does know,
and `null` — could not ask at all — is a fourth, distinct case.

Four capture scenarios plus *the policy state could not be read at all*; five cases in
`node --test`.

### 4. Nothing rebuilds the form

A stream event can arrive while a path is being typed. Repainting the form would erase it and
move the caret.

This is a negative rule: no handler in `applications.js` touches the form except on submit.

*applications · a pushed state leaves the half-typed form alone* types a path, puts the caret at
position 7, pushes a `policy-state` over the stream, and checks the text, the caret position and
the focus. The status line does change, which is the half that must.

### 5. A pushed event does not move a switch with a click in flight

The user asked for one thing; if the stream moves the switch meanwhile, the answer they are
waiting for lands on top of a value they did not set.

`rules.js` → `acceptsPushedValue(isBusy)`, and the split in `devices.js` between
`renderUsb(status)` — text only, no decisions — and `showUsb(status)`, which decides whether the
switch moves.

*a push does not move a switch with a click in flight* holds the `PUT` forever and pushes a
contrary `usb`. Its twin, *a push moves the switch when nothing is in flight*, exists so the
rule cannot be "satisfied" by never listening.

### 6. Re-read from the server after writing

The registry is the source of truth. If someone edited it by hand a moment ago, this is where
the interface finds out.

`devices.js` → `handleToggle`: write, notify, re-read. The re-read happens **inside**
`withPending`, so by rule 5 it refreshes the text without moving the switch.

*devices · the state after a write is re-read from the service* changes the `GET` answer at the
instant the `PUT` is made, so the text can only come from the re-read.

### 7. Only page 0 follows the stream, and only while its section is on screen

Reloading page four under someone reading it moves their rows. Reloading a hidden section spends
a request on something nobody can see.

`rules.js` → `followsPushedEvents(offset, isSectionVisible)`. Visibility is asked of the router
(`currentRoute() === 'history'`), not read from a `hidden` attribute: how a section is taken off
screen is the navigation's business.

*the first page follows a pushed event*, *a later page does not move under a pushed event*,
*nothing reloads while the section is not on screen*.

### 8. An offset past the end steps back

Happens after a filter change or when entries disappear. An empty table under a pager announcing
three pages does not explain itself.

`rules.js` → `offsetAfterEmptyPage(offset, entryCount)` returns the offset to retry with, or
`null`. Returning `null` for an empty page 0 is what avoids a loop: "nothing recorded" is a
legitimate answer.

*history · an offset past the end steps back*; five cases in `node --test`.

### 9. Removal confirms in the row, never in a dialog

`window.confirm` blocks the thread, cannot be styled, and takes the name of what is about to
change away from where it was. The row that is about to disappear is exactly where the question
belongs. Opening one confirmation closes any other: two rows asking at once is an ambiguous
answer.

`applications.js` → `applicationRow`, in `ask(asking)` and the module variable `confirming`.

*a removal asks in the row it belongs to* (`asking: 1`, focus on `Remove`, `deleted: 0` — asking
does not delete), *cancelling a removal puts the row back*, *opening one confirmation closes the
other*.

### 10. The process picker is a dialog with a search box

The process list is long and the form it fills sits below it. A panel inside the card pushes that
form out of view exactly when it is about to be used.

`rules.js` → `filterProcesses`, `describeProcessCount`, `describeProcessEmptiness`; the dialog in
`applications.js`. The search filters on name **and** path, because the path is often the only
thing telling two copies apart.

*the filter narrows on name and on path*, *a search that finds nothing says what it looked for*,
*it opens with the caret in the search, and escape closes it*.

### 11. Passwords are validated while they are typed

The rules are the service's and arrive from it in `GET /api/auth/session`. The browser keeps no
copy: it would be a second source of truth that stops agreeing the day the rule changes.

`rules.js` → `describePasswordNote(value, rule)` and `describePasswordMatch`. Length is checked
first and the alphabet only after: complaining that six characters need a digit while there are
three of them answers a question nobody has reached.

Four `settings ·` scenarios, including *and says nothing before there is anything to say*.

### 12. The indicators in the bar cost no extra request

The Applications badge and the Devices dot have to be true on the first paint, without those
sections having been opened.

`shell.js`. The badge comes from the list Applications loads anyway as the default route; the
dot comes from the snapshot the stream sends on connect (`IServiceEventSnapshot`).

*the tab indicators come from the list and the stream*.

### 13. The direction of an event comes from the service

Which event ids open a session is a fact about Windows, not a presentation decision, and the
service already owns it (`LogonEvent.IsSessionStart`) because it is what pairs each close with
its start to compute the duration. Deriving it again in the browser is a second copy of that
rule.

It travels as `startsSession` on every entry of `GET /api/access-history`. `describeEvent` reads
it and deduces nothing.

The **labels** do belong to the view, and there are four of them: signing out and losing an RDP
connection are different events. The direction is binary; the label is not.

*the four transitions read as four different things*,
`EveryEntrySaysWhetherItOpensASessionOrClosesOne` in integration, and a table in
`rules.test.mjs` where removing one kind fails the test.

### 14. The pager never names a range it does not know

`1–0 of 30` is a range that ends before it begins. It appears when there is a total with no page
under it: an event pushed before the first load, or a first load that failed. Both sides are
covered: `pagerState` does not build a range with `shown` at 0, and the stream handler does not
repaint a pager that has never had rows.

`rules.js` → `summarise`; `history.js` → the `access-history` handler.

*a page answered empty says so instead of naming a range*, *a total pushed over a failed load
names no range*.

### 15. `aria-modal` is a claim, and it has to be kept

The dialog declares `aria-modal="true"`, so Tab must not walk out to the page behind the scrim,
which is still operable by keyboard under it. Every exit restores focus to the control that
opened the dialog.

`applications.js` → `keepFocusInside` and `pickerOpener`. Focus is restored in `closePicker`, one
place, rather than each exit remembering to. Picking a process is the one exit with a better
destination — the path field — and it says so by pointing `pickerOpener` there.

Two constraints inside that:

- The focusable selector in `markup.js` uses `a[href]`, not a bare `[href]`. Every icon here is
  an `<svg><use href="#id">`, a bare attribute selector matches those, and they are not
  focusable, so the last stop in the dialog came out as a `<use>` element and the trap silently
  let Tab through.
- The opener can be disabled when the dialog closes — it is busy applying what was asked — and
  focusing a disabled element drops the caret to `<body>`. `closePicker` falls back to the path
  field.

*tab does not walk out from under the scrim*, *the scrim gives the caret back like escape does*,
*closing while the opener is still busy does not drop the caret*.

### 16. The dot in the bar says whether the call arrived

`GET /api/health` returns a constant, so comparing its value against a word says nothing. What a
browser can actually answer is whether the last call arrived, and that is what the dot reports.

`api.js` reports it — it is the only module that calls `fetch`, so it is the only one that sees
every attempt — and `shell.showReachable` paints it. A response is a response whatever its
status: a 500 means the service is there and unhappy. Only a rejected `fetch` means nothing
arrived.

The words beside the dot follow the same signal. Coming back asks the service again rather than
restoring what was on screen, because a restarted service can answer with a different version.
`whenReachabilityChanges` fires on transitions only; the starting state is "unknown", so the
first call of the page is a transition like any other and one path paints the indicator at boot
and keeps it right afterwards.

*the health dot answers whether the call arrived*, *a service that does not answer turns the dot
red*, *a later call that never arrives turns the dot red too*.

**The words are a duration, and the service does not send one.** `GET /api/health` answers with
`startedAt`, the instant it started; `describeServiceHealth` in `rules.js` does the subtraction.
So the line goes stale on its own and `app.js` repaints it once a minute from the value already
in hand — not by asking again, which would be a round trip a minute for a number the browser can
work out. The ticker stops when the service stops answering: one left running would keep counting
up beside a red dot, which is the lie the version used to tell from that corner.

The same two values are shown on the sign-in screen, by `session.js`, because that screen has no
bar to put them on. Both call the one rule, so the two cannot drift apart. What may be said there
is bounded by the endpoint being public: the machine's name and the service's uptime, never what
the machine is configured to block.

### 17. Origin has three values, and the third one is not "Local"

`LogonOrigin` is `Local`, `Remote` and `Unknown`. An event whose record carried no address is
`Unknown`, and showing it as "Local" turns "nobody knows where this came from" into a claim
about the machine.

`rules.js` → `eventOrigins`.

*an origin the service could not determine is not called local*, and a table in `rules.test.mjs`
with the three origins where two reading the same way is a failure.

### 18. A card has a header or a strip, never both

They are different things. The header carries the card's **title**, its tint and whatever
control belongs to the whole card; it has no icon. The strip carries a **state of the machine**,
with an icon coloured by that state, and appears once — above the blocked applications, because
the policy state is the only thing here that changes without anyone asking.

`index.html` and `app.css` → `.card-header[data-tint]` and `.strip`.

The section dumps of `section-devices`, `section-history` and `section-settings`: one header
each, with `data-tint` and no `strip`.

### 19. What the interface says lives in `rules.js`

A redesign that only changes appearance does not touch `rules.js`. One that changes **what is
said** does, and the change belongs there rather than in the renderer.

The rule is not "do not touch the file", it is that there must be no second copy. The renderer
does not split the policy string on its separator to find the bold half:
`describePolicyState` returns `headline` and `detail` already separated, because the separator
lives in `rules.js`.

`rules.test.mjs` changes in the same commit as the text. Text that changes without its test
changing is text that changed silently.

### Three more, held by structure

| Rule                                    | Where        | What it prevents                                                      |
|-----------------------------------------|--------------|-----------------------------------------------------------------------|
| A repeated click is ignored, not queued | `pending.js` | Queueing would apply the policy twice and the second would answer 409 |
| "Session lost" is shown once            | `session.js` | Two 401s in the same instant stacking two notices                     |
| A 401 anywhere goes through one door    | `api.js`     | Except at login, where 401 is an answer and not an expired session    |

## The DOM harness

`scripts/interface-dom.mjs` renders every section against fixed data and writes the resulting
DOM to a file. It replaces `fetch`, `EventSource` and `Date.now` inside the browser before the
modules load, and serves `wwwroot` from disk itself, so **nothing has to be published or
installed between captures** and no policy is deployed, no registry value written and no session
used. Freezing the clock is also what makes the output comparable: a live capture would carry
"checked 3 s ago" inside it and never match itself twice.

Each capture is marked `[check]` or `[markup]`:

- **`[check]`** are statements about behaviour and do not look at HTML. A change in one is a
  regression until proved otherwise.
- **`[markup]`** are `outerHTML` dumps. They change when the presentation changes and are read
  by eye.

```powershell
dotnet test                                  # runs node --test behind InterfaceRuleTests
node --test "tests/interface/*.test.mjs"     # the rules alone, no browser, no service
node scripts/interface-dom.mjs --out=after.txt
git diff --no-index before.txt after.txt
```

`--origin http://localhost:5150` points the harness at the installed service instead of the
working tree.

Two traps when comparing captures:

- A capture of `outerHTML` **spans several lines**, so a script that reads only the marker lines
  undercounts.
- Captures must be compared **by value, not by position**. Inserting a capture in the middle of
  a scenario shifts the ones after it, and a script that aligns by index reports them as
  changed.

## Keeping the simulated data honest

The harness data is a second copy of the API's vocabulary and can drift from the first with
nothing to notice. `SimulatedResponseTests` closes the two doors that matter:

| Test                                                  | What it holds                                                        |
|-------------------------------------------------------|----------------------------------------------------------------------|
| `EveryValueAnEnumCanTakeAppearsInTheSimulatedAnswers` | Every value of an enum crossing the API is rendered at least once    |
| `NoSimulatedAnswerUsesAValueTheServiceCannotProduce`  | No canned answer uses a value that does not exist                    |
| `TheSimulatedHealthAnswerIsTheOneTheServiceGives`     | `status` is asked of the running service, not copied from the source |

Enums are found by **reflection**, not from a list, so a new one in a new response is covered
the day it is written — provided it crosses the boundary **typed as the enum**. A closed set
that goes out as a `string` is a closed set nobody checks: `JsonStringEnumConverter` is
registered without a naming policy, so typing the property costs nothing on the wire and both
exits — the REST answer and the event stream — have their own assertion.

Whole payloads are deliberately not compared. The shapes already agree, real and canned data
differ by design, and a check that has to be taught which differences are legitimate is a check
nobody keeps. What matters is the closed sets: the values a browser can be tempted to compare
against.
