// The rules of the interface, tested where they live: plain ESM, no DOM, no browser, no service
// and no package to install. `node --test` runs this file as it is.
//
// A redesign is meant to replace every renderer in wwwroot/js and change none of this. A failure
// here means a decision changed while the markup was being rewritten, which is the exact
// accident these tests exist to catch.

import test from 'node:test';
import assert from 'node:assert/strict';

import {
  acceptsPushedValue,
  describeEvent,
  describeMatch,
  describePasswordNote,
  describePasswordMatch,
  describePasswordRule,
  describePolicyState,
  describeProcessCount,
  describeProcessEmptiness,
  describeRemoval,
  describeServiceHealth,
  describeSessionExpiry,
  describeToggle,
  describeUsbChange,
  describeUsbState,
  filterProcesses,
  followsPushedEvents,
  offsetAfterEmptyPage,
  pageNumbers,
  pagerState,
} from '../../src/WindowsControlService/wwwroot/js/rules.js';
import { formatUptime } from '../../src/WindowsControlService/wwwroot/js/format.js';

// --- Rule 3: three policy states, and Unknown is not "nothing is blocked" ---

test('an enforced policy says how many rules and how long ago it was checked', () => {
  const described = describePolicyState({
    state: 'Enforced',
    enabledRuleCount: 3,
    lastReconciledAt: new Date().toISOString(),
  });

  assert.equal(described.tone, 'enforced');
  // Two halves, not one sentence: only the claim is set in bold, and finding the boundary again
  // in the renderer would mean splitting on a separator that belongs to rules.js.
  assert.equal(described.headline, 'Policy enforced');
  assert.equal(described.detail, '3 rules');
  assert.equal(described.icon, 'ok');
  assert.match(described.checked, /^checked .* ago$/);
  // The relative time answers "was that just now"; the recorded one has to survive alongside it,
  // because a value the service recorded must never be only paraphrased.
  assert.notEqual(described.checkedExactly, '');
  assert.notEqual(described.checkedExactly, described.checked);
});

test('one rule is one rule, not one rules', () => {
  const described = describePolicyState({ state: 'Enforced', enabledRuleCount: 1, lastReconciledAt: null });

  assert.equal(described.detail, '1 rule');
  // Nothing has been reconciled yet, so there is nothing to say about when.
  assert.equal(described.checked, '');
  assert.equal(described.checkedExactly, '');
});

test('an unknown state never claims that nothing is blocked', () => {
  const described = describePolicyState({ state: 'Unknown', enabledRuleCount: 0, lastReconciledAt: null });

  assert.equal(described.tone, 'unknown');
  assert.equal(described.headline, 'Policy state unknown');
  assert.equal(described.detail, '');
  assert.equal(described.icon, 'alert');
  assert.doesNotMatch(described.headline, /nothing/i);
});

test('a policy that is not enforced distinguishes waiting rules from no rules at all', () => {
  const waiting = describePolicyState({ state: 'NotEnforced', enabledRuleCount: 2, lastReconciledAt: null });
  const nothing = describePolicyState({ state: 'NotEnforced', enabledRuleCount: 0, lastReconciledAt: null });

  assert.equal(waiting.headline, 'Not enforced');
  assert.equal(waiting.detail, '2 rules waiting');
  assert.equal(nothing.headline, 'No policy deployed');
  assert.equal(nothing.detail, '');
  assert.equal(waiting.tone, 'notenforced');
  assert.equal(nothing.tone, 'notenforced');
});

test('a state that could not be read at all is not turned into one that could', () => {
  const described = describePolicyState(null);

  assert.equal(described.tone, 'unknown');
  assert.equal(described.headline, 'Policy state unavailable');
  assert.equal(described.checked, '');
});

test('a state this version does not know is shown verbatim rather than guessed at', () => {
  const described = describePolicyState({ state: 'Auditing', enabledRuleCount: 1, lastReconciledAt: null });

  assert.equal(described.headline, 'Auditing');
  assert.equal(described.tone, 'auditing');
});

// --- Rule 1: the toggle reports the value the service accepted -------------

test('the toggle notice names the state that is now true', () => {
  assert.equal(describeToggle('Example', true), 'Example is blocked again.');
  assert.equal(describeToggle('Example', false), 'Example is no longer blocked.');
});

// --- Rule 2: a failed removal keeps the row ---------------------------------

test('a removal reloads the list whether it succeeded or not', () => {
  const removed = describeRemoval('Example', null);
  const refused = describeRemoval('Example', 'The policy could not be rebuilt.');

  // The row survives a failure precisely because the list is re-read instead of edited in
  // place: the application is still blocked, so its row is still true.
  assert.equal(removed.reload, true);
  assert.equal(refused.reload, true);

  assert.deepEqual(removed, { tone: 'ok', text: 'Example was removed.', reload: true });
  assert.deepEqual(refused, { tone: 'error', text: 'The policy could not be rebuilt.', reload: true });
});

test('the chip says which attribute is doing the blocking, and against what', () => {
  // Never a fixed label: a binary with no OriginalFilename is matched by something else, and
  // saying "FileName" anyway is the guess that made a block silently do nothing.
  assert.equal(describeMatch({ matchAttribute: 'ProductName', matchValue: 'Steam' }), 'ProductName · Steam');
});

// --- The process picker ----------------------------------------------------

test('the process filter matches on the name and on the path', () => {
  const processes = [
    { name: 'chrome', executablePath: 'C:\\Program Files\\Google\\chrome.exe' },
    { name: 'steam', executablePath: 'C:\\Games\\Steam\\steam.exe' },
  ];

  assert.equal(filterProcesses(processes, 'chr').length, 1);
  // The path is often the only thing that tells two copies of the same name apart.
  assert.equal(filterProcesses(processes, 'games').length, 1);
  assert.equal(filterProcesses(processes, 'CHROME').length, 1, 'the filter ignores case');
  assert.equal(filterProcesses(processes, '   ').length, 2, 'whitespace is not a filter');
  assert.equal(filterProcesses(processes, 'zzz').length, 0);
});

test('the count says how many of how many, and nothing at all before anything is loaded', () => {
  assert.equal(describeProcessCount(0, 0), '');
  assert.equal(describeProcessCount(14, 14), '14 processes');
  assert.equal(describeProcessCount(3, 14), '3 of 14 processes');
});

test('a search that found nothing says what it looked for', () => {
  assert.equal(describeProcessEmptiness('', 0), 'No candidate processes are running.');
  assert.equal(describeProcessEmptiness(' brave ', 14), 'No running process matches "brave".');
});

// --- Rules 5 and 6: the registry is the source of truth ---------------------

test('the usb state is described from what the service reports', () => {
  const blocked = describeUsbState({ blocked: true, lastModified: '2026-08-19T10:00:00Z' });
  const allowed = describeUsbState({ blocked: false, lastModified: null });

  // One line, and the time in it is relative like every other time in this interface.
  assert.deepEqual(blocked.pill, { tone: 'signal', text: 'Blocked' });
  assert.match(blocked.detail, /^New drives will not mount · changed .* ago$/);

  assert.notEqual(blocked.detailExactly, '');

  assert.deepEqual(allowed.pill, { tone: 'muted', text: 'Allowed' });
  assert.equal(allowed.detail, 'Drives mount normally · never changed through this service');
  // Never changed is not a time, so there is no exact one to show on hover either.
  assert.equal(allowed.detailExactly, '');
});

test('the usb notice names the state that is now true', () => {
  assert.equal(describeUsbChange(true), 'USB mass storage is blocked.');
  assert.equal(describeUsbChange(false), 'USB mass storage is allowed again.');
});

test('a pushed value is refused while a click is still in flight', () => {
  assert.equal(acceptsPushedValue(true), false);
  assert.equal(acceptsPushedValue(false), true);
});

// --- Rule 7: only the first page, and only while it is on screen ------------

test('only the first page of a visible section follows the stream', () => {
  assert.equal(followsPushedEvents(0, true), true);
  assert.equal(followsPushedEvents(0, false), false);
  assert.equal(followsPushedEvents(10, true), false);
  assert.equal(followsPushedEvents(10, false), false);
});

// --- Rule 8: an offset past the end steps back ------------------------------

test('an empty page past the beginning goes back to the first one', () => {
  assert.equal(offsetAfterEmptyPage(30, 0), 0);
  assert.equal(offsetAfterEmptyPage(10, 0), 0);
});

test('an empty first page is left alone, and so is any page with rows in it', () => {
  // Nothing recorded yet is a real answer, not a paging accident: correcting it would loop.
  assert.equal(offsetAfterEmptyPage(0, 0), null);
  assert.equal(offsetAfterEmptyPage(0, 10), null);
  assert.equal(offsetAfterEmptyPage(20, 3), null);
});

// --- The pager --------------------------------------------------------------

test('an empty history says so instead of counting rows it does not have', () => {
  const pager = pagerState(0, 0, 10, 0);

  assert.equal(pager.summary, 'Nothing recorded');
  assert.equal(pager.pages, 1);
  assert.equal(pager.canGoNewer, false);
  assert.equal(pager.canGoOlder, false);
});

test('the first page of three can only go older', () => {
  const pager = pagerState(0, 23, 10, 10);

  assert.equal(pager.summary, '1–10 of 23');
  assert.equal(pager.canGoNewer, false);
  assert.equal(pager.canGoOlder, true);
});

test('the last page can only go newer, and counts only the rows it actually has', () => {
  const pager = pagerState(20, 23, 10, 3);

  assert.equal(pager.summary, '21–23 of 23');
  assert.equal(pager.canGoNewer, true);
  assert.equal(pager.canGoOlder, false);
});

test('a total with no rows under it names no range', () => {
  // "1–0 of 30" was on screen: a range that ends before it begins, produced whenever a total
  // was known and the slice was not -- a pushed total before the first load, or a page the
  // service answered empty.
  const pager = pagerState(0, 30, 10, 0);

  assert.equal(pager.summary, 'Nothing on this page');
  assert.ok(!pager.summary.includes('1–0'), pager.summary);
  // The total is still known, so the pager still knows how many pages there are.
  assert.equal(pager.pages, 3);
});

test('a total that is an exact multiple of the page size does not invent a page', () => {
  // The off-by-one that would show a last page with nothing on it.
  const full = pagerState(10, 20, 10, 10);

  assert.equal(full.pages, 2);
  assert.equal(full.page, 2);
  assert.equal(full.canGoOlder, false);
});

test('a short pager shows every page and no gap', () => {
  assert.deepEqual(pageNumbers(1, 1), [1]);
  assert.deepEqual(pageNumbers(3, 7), [1, 2, 3, 4, 5, 6, 7]);
});

test('a long pager keeps the ends, the current page and its neighbours', () => {
  assert.deepEqual(pageNumbers(7, 13), [1, null, 6, 7, 8, null, 13]);
});

test('near either end the pager spends the freed neighbour on the other side', () => {
  // Otherwise the row of numbers changes width as you page through it.
  assert.deepEqual(pageNumbers(1, 13), [1, 2, 3, 4, null, 13]);
  assert.deepEqual(pageNumbers(13, 13), [1, null, 10, 11, 12, 13]);
});

test('the pager never offers a page that does not exist', () => {
  for (const pages of [1, 2, 5, 8, 13, 40]) {
    for (const page of [1, Math.ceil(pages / 2), pages]) {
      for (const number of pageNumbers(page, pages)) {
        if (number !== null) {
          assert.ok(number >= 1 && number <= pages, `page ${number} of ${pages}`);
        }
      }
    }
  }
});

// --- Access events ----------------------------------------------------------

/**
 * The four the TerminalServices channel emits, and the whole list on purpose: a test that only
 * ever passes Logon and Logoff is what let `kind === 'Logon'` look right while it mislabelled
 * every Reconnect. Dropping one from this table fails the test below rather than going unnoticed.
 */
const KINDS = ['Logon', 'Reconnect', 'Disconnect', 'Logoff'];

const event = (fields) => describeEvent({
  origin: 'Local',
  address: null,
  userName: 'MACHINE\\owner',
  occurredAt: new Date().toISOString(),
  durationSeconds: null,
  ...fields,
});

test('all four transitions get their own label, and no two share one', () => {
  const labels = KINDS.map((kind) => event({ kind, startsSession: false }).label);

  for (const [index, kind] of KINDS.entries()) {
    // The fallback shows an unknown kind verbatim, so a label equal to the kind means this one
    // was never given words of its own.
    assert.notEqual(labels[index], kind, `${kind} has no label of its own`);
  }

  assert.equal(new Set(labels).size, KINDS.length, `two kinds share a label: ${labels.join(', ')}`);
});

test('the direction is the service\'s answer, not something derived from the kind', () => {
  // Every kind, both answers. If the direction were derived here, half of these would disagree
  // with what was asked for -- which is exactly the bug this replaces.
  for (const kind of KINDS) {
    assert.equal(event({ kind, startsSession: true }).direction, 'in', `${kind} starting`);
    assert.equal(event({ kind, startsSession: false }).direction, 'out', `${kind} ending`);
  }
});

test('a kind this version does not know keeps its direction and shows itself', () => {
  const unknown = event({ kind: 'ShadowConnect', startsSession: true });

  assert.equal(unknown.label, 'ShadowConnect');
  assert.equal(unknown.direction, 'in');
});

test('a reconnection reads as a reconnection and opens a session', () => {
  const reconnect = event({
    kind: 'Reconnect',
    startsSession: true,
    origin: 'Remote',
    address: '203.0.113.44',
  });

  assert.equal(reconnect.direction, 'in');
  assert.equal(reconnect.label, 'Reconnected');
  assert.deepEqual(reconnect.origin, { tone: 'remote', text: 'RDP' });
  assert.equal(reconnect.detail, '203.0.113.44');
  // Only the events that close a session carry a duration, and null is not zero.
  assert.equal(reconnect.duration, '');
});

test('a local session is identified by its user, since it has no address', () => {
  const outbound = event({ kind: 'Logoff', startsSession: false, durationSeconds: 5400 });

  assert.equal(outbound.direction, 'out');
  assert.equal(outbound.label, 'Signed out');
  assert.deepEqual(outbound.origin, { tone: 'muted', text: 'Local' });
  assert.equal(outbound.detail, 'MACHINE\\owner');
  assert.equal(outbound.duration, '1 h 30 min');
});

/** Three, not two. The third is what an event whose record carried no address at all gets. */
const ORIGINS = ['Local', 'Remote', 'Unknown'];

test('an origin the service could not determine is not reported as local', () => {
  const texts = ORIGINS.map((origin) => event({ kind: 'Logon', startsSession: true, origin }).origin.text);

  assert.equal(new Set(texts).size, ORIGINS.length, `two origins read the same: ${texts.join(', ')}`);
  // The one that bit: Unknown fell through to the Local branch and turned "nobody knows where
  // this came from" into a claim about the machine.
  assert.notEqual(texts[ORIGINS.indexOf('Unknown')], texts[ORIGINS.indexOf('Local')]);
});

test('a disconnection is not a sign-out, because here they are different events', () => {
  assert.notEqual(
    event({ kind: 'Disconnect', startsSession: false }).label,
    event({ kind: 'Logoff', startsSession: false }).label);
});

// --- Validation while typing ------------------------------------------------

test('the password counter counts against the minimum until it is met', () => {
  const rule = { minimum: 6, requiresLettersAndDigits: true };

  assert.deepEqual(describePasswordNote('', rule), { text: '', state: 'neutral', icon: null });
  assert.deepEqual(describePasswordNote('sh1', rule), { text: '3/6', state: 'bad', icon: 'alert' });
  // Long enough, and only then is the alphabet the thing standing in the way.
  assert.deepEqual(describePasswordNote('letters', rule), { text: 'letters and digits', state: 'bad', icon: 'alert' });
  assert.deepEqual(describePasswordNote('1234567', rule), { text: 'letters and digits', state: 'bad', icon: 'alert' });
  assert.deepEqual(describePasswordNote('letter5', rule), { text: '7', state: 'ok', icon: null });
});

test('the repeated password says nothing before there is anything to say', () => {
  assert.deepEqual(describePasswordMatch('secret', ''), { text: '', state: 'neutral', icon: null });
  assert.deepEqual(describePasswordMatch('secret', 'secret'), { text: 'Match', state: 'ok', icon: null });
  // Falling short of the minimum is a warning; two passwords that differ is a refusal.
  assert.deepEqual(describePasswordMatch('secret', 'secrez'), { text: 'No match', state: 'bad', icon: 'no' });
});

test('the password card asks for the service minimum, and says nothing before it knows it', () => {
  assert.equal(
    describePasswordRule({ minimum: 6, requiresLettersAndDigits: true }),
    'at least 6 characters, letters and digits');
  assert.equal(describePasswordRule({ minimum: 6, requiresLettersAndDigits: false }), 'at least 6 characters');
  // Before the first answer arrives there is no number, and a guess would be worse than silence.
  assert.equal(describePasswordRule({ minimum: 0, requiresLettersAndDigits: true }), '');
});

test('the session card says when the session ends, and nothing when it cannot', () => {
  assert.equal(describeSessionExpiry(30), 'Ends after 30 minutes of inactivity.');
  // Before the first answer arrives there is no number, and a guess would be worse than silence.
  assert.equal(describeSessionExpiry(0), '');
});

// --- The health indicator: a duration, computed here from the instant the service sent --------

// The clock every case below is read against. Fixed, because "how long ago" measured from
// Date.now() is a test that passes for a different reason every time it runs.
const NOW = Date.parse('2026-09-02T12:00:00Z');
const STARTED = (seconds) => new Date(NOW - seconds * 1000).toISOString();

test('the uptime pads every unit after the first, and only the first is left bare', () => {
  // The shape the format exists for: one measurement, not three adjacent numbers.
  assert.equal(formatUptime(STARTED(4 * 86400 + 6 * 3600 + 12 * 60), NOW), '4d 06h 12m');
  assert.equal(formatUptime(STARTED(6 * 3600 + 2 * 60), NOW), '6h 02m');
  assert.equal(formatUptime(STARTED(12 * 60), NOW), '12m');
});

test('a unit is dropped only while nothing larger has been shown', () => {
  // Zero hours between days and minutes is information: dropping it would turn four days and
  // twelve minutes into "4d 12m", which is a different and much shorter duration.
  assert.equal(formatUptime(STARTED(4 * 86400 + 12 * 60), NOW), '4d 00h 12m');
  assert.equal(formatUptime(STARTED(4 * 86400), NOW), '4d 00h 00m');
  // Nothing larger than minutes has been shown, so there is nothing to keep the place of.
  assert.equal(formatUptime(STARTED(3 * 60), NOW), '3m');
});

test('an uptime under a minute says so instead of rounding to zero', () => {
  assert.equal(formatUptime(STARTED(41), NOW), '<1m');
  // Clamped rather than allowed to go negative: the only way to get here is a clock that moved
  // under both the service and this page, and "-1m" would be a reading nobody can act on.
  assert.equal(formatUptime(STARTED(-90), NOW), '<1m');
});

test('the health line reads as a duration and keeps the version a hover away', () => {
  const described = describeServiceHealth({
    status: 'running',
    version: '1.0.0+0123456789abcdef0123456789abcdef01234567',
    machineName: 'DESKTOP-7K2M1',
    startedAt: STARTED(4 * 86400 + 6 * 3600 + 12 * 60),
  }, NOW);

  // The service's own word, printed and not interpreted: comparing `status` against a word this
  // file chose is how the dot spent months never going green.
  assert.equal(described.text, 'running 4d 06h 12m');
  // The version is not lost, and the commit after the plus sign does not come with it.
  assert.match(described.title, /^Started .+ · version 1\.0\.0$/);
});

test('the health line says nothing before the service has answered', () => {
  // Not "running 0m": before the first answer there is no reading, and inventing one would put
  // a claim about the service next to a dot that does not know either.
  assert.deepEqual(describeServiceHealth(null, NOW), { text: '', title: '' });
});
