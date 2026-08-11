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
  describePasswordLength,
  describePasswordMatch,
  describePolicyState,
  describeProcessCount,
  describeProcessEmptiness,
  describeRemoval,
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

// --- Rule 3: three policy states, and Unknown is not "nothing is blocked" ---

test('an enforced policy says how many rules and how long ago it was checked', () => {
  const described = describePolicyState({
    state: 'Enforced',
    enabledRuleCount: 3,
    lastReconciledAt: new Date().toISOString(),
  });

  assert.equal(described.tone, 'enforced');
  assert.equal(described.text, 'Policy enforced · 3 rules');
  assert.equal(described.icon, 'ok');
  assert.match(described.checked, /^checked .* ago$/);
});

test('one rule is one rule, not one rules', () => {
  const described = describePolicyState({ state: 'Enforced', enabledRuleCount: 1, lastReconciledAt: null });

  assert.equal(described.text, 'Policy enforced · 1 rule');
  // Nothing has been reconciled yet, so there is nothing to say about when.
  assert.equal(described.checked, '');
});

test('an unknown state never claims that nothing is blocked', () => {
  const described = describePolicyState({ state: 'Unknown', enabledRuleCount: 0, lastReconciledAt: null });

  assert.equal(described.tone, 'unknown');
  assert.equal(described.text, 'Policy state unknown');
  assert.equal(described.icon, 'alert');
  assert.doesNotMatch(described.text, /nothing/i);
});

test('a policy that is not enforced distinguishes waiting rules from no rules at all', () => {
  const waiting = describePolicyState({ state: 'NotEnforced', enabledRuleCount: 2, lastReconciledAt: null });
  const nothing = describePolicyState({ state: 'NotEnforced', enabledRuleCount: 0, lastReconciledAt: null });

  assert.equal(waiting.text, 'Not enforced · 2 rules waiting');
  assert.equal(nothing.text, 'No policy deployed');
  assert.equal(waiting.tone, 'notenforced');
  assert.equal(nothing.tone, 'notenforced');
});

test('a state that could not be read at all is not turned into one that could', () => {
  const described = describePolicyState(null);

  assert.equal(described.tone, 'unknown');
  assert.equal(described.text, 'Policy state unavailable');
  assert.equal(described.checked, '');
});

test('a state this version does not know is shown verbatim rather than guessed at', () => {
  const described = describePolicyState({ state: 'Auditing', enabledRuleCount: 1, lastReconciledAt: null });

  assert.equal(described.text, 'Auditing');
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

  assert.deepEqual(blocked.pill, { tone: 'signal', text: 'Blocked' });
  assert.equal(blocked.title, 'New drives will not mount.');
  assert.match(blocked.lastChanged, /^Last changed through this service: /);

  assert.deepEqual(allowed.pill, { tone: 'muted', text: 'Allowed' });
  assert.equal(allowed.title, 'Drives mount normally.');
  assert.equal(allowed.lastChanged, 'Never changed through this service.');
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

test('a logon is an inbound connection and a logoff is not', () => {
  const inbound = describeEvent({
    kind: 'Logon',
    origin: 'Remote',
    address: '203.0.113.44',
    userName: 'M\\owner',
    occurredAt: new Date().toISOString(),
    durationSeconds: null,
  });

  assert.equal(inbound.direction, 'in');
  assert.equal(inbound.label, 'Connected');
  assert.deepEqual(inbound.origin, { tone: 'remote', text: 'RDP' });
  assert.equal(inbound.detail, '203.0.113.44');
  // Only the events that close a session carry a duration, and null is not zero.
  assert.equal(inbound.duration, '');
});

test('a local session is identified by its user, since it has no address', () => {
  const outbound = describeEvent({
    kind: 'Logoff',
    origin: 'Local',
    address: null,
    userName: 'MACHINE\\owner',
    occurredAt: new Date().toISOString(),
    durationSeconds: 5400,
  });

  assert.equal(outbound.direction, 'out');
  assert.equal(outbound.label, 'Disconnected');
  assert.deepEqual(outbound.origin, { tone: 'muted', text: 'Local' });
  assert.equal(outbound.detail, 'MACHINE\\owner');
  assert.equal(outbound.duration, '1 h 30 min');
});

// --- Validation while typing ------------------------------------------------

test('the password counter counts against the minimum until it is met', () => {
  assert.deepEqual(describePasswordLength('', 10), { text: '', state: 'neutral' });
  assert.deepEqual(describePasswordLength('short', 10), { text: '5/10', state: 'bad' });
  assert.deepEqual(describePasswordLength('exactly-10', 10), { text: '10', state: 'ok' });
});

test('the repeated password says nothing before there is anything to say', () => {
  assert.deepEqual(describePasswordMatch('secret', ''), { text: '', state: 'neutral' });
  assert.deepEqual(describePasswordMatch('secret', 'secret'), { text: 'Match', state: 'ok' });
  assert.deepEqual(describePasswordMatch('secret', 'secrez'), { text: 'No match', state: 'bad' });
});

test('the session card says when the session ends, and nothing when it cannot', () => {
  assert.equal(describeSessionExpiry(30), 'Ends after 30 minutes of inactivity.');
  // Before the first answer arrives there is no number, and a guess would be worse than silence.
  assert.equal(describeSessionExpiry(0), '');
});
