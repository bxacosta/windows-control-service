// The rules of the interface, tested where they live: plain ESM, no DOM, no browser, no service
// and no package to install. `node --test` runs this file as it is.
//
// The redesign that follows is meant to replace every renderer in wwwroot/js and change none of
// this. A failure here means a decision changed while the markup was being rewritten, which is
// the exact accident these tests exist to catch.

import test from 'node:test';
import assert from 'node:assert/strict';

import {
  acceptsPushedValue,
  describePolicyState,
  describeRemoval,
  describeToggle,
  describeUsbChange,
  describeUsbState,
  followsPushedEvents,
  offsetAfterEmptyPage,
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
  assert.match(described.text, /^Enforced · 3 rule\(s\) · checked .* ago$/);
});

test('an unknown state never claims that nothing is blocked', () => {
  const described = describePolicyState({ state: 'Unknown', enabledRuleCount: 0, lastReconciledAt: null });

  assert.equal(described.tone, 'unknown');
  assert.match(described.text, /could not ask Windows/);
  assert.match(described.text, /not the same as "nothing is blocked"/);
});

test('a policy that is not enforced distinguishes waiting rules from no rules at all', () => {
  const waiting = describePolicyState({ state: 'NotEnforced', enabledRuleCount: 2, lastReconciledAt: null });
  const nothing = describePolicyState({ state: 'NotEnforced', enabledRuleCount: 0, lastReconciledAt: null });

  assert.match(waiting.text, /2 rule\(s\) waiting/);
  assert.equal(nothing.text, 'No policy deployed. Nothing is blocked.');
  assert.equal(waiting.tone, 'notenforced');
  assert.equal(nothing.tone, 'notenforced');
});

test('a state that could not be read at all is not turned into one that could', () => {
  const described = describePolicyState(null);

  assert.equal(described.tone, 'unknown');
  assert.equal(described.text, 'Unavailable.');
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

// --- Rules 5 and 6: the registry is the source of truth ---------------------

test('the usb state is described from what the service reports', () => {
  const blocked = describeUsbState({ blocked: true, lastModified: '2026-08-19T10:00:00Z' });
  const allowed = describeUsbState({ blocked: false, lastModified: null });

  assert.equal(blocked.title, 'Blocked. New drives will not mount.');
  assert.match(blocked.lastChanged, /^Last changed through this service: /);

  assert.equal(allowed.title, 'Allowed. Drives mount normally.');
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

test('an empty history says so instead of counting pages', () => {
  const pager = pagerState(0, 0, 10);

  assert.equal(pager.summary, 'No events recorded yet.');
  assert.equal(pager.pages, 1);
  assert.equal(pager.canGoNewer, false);
  assert.equal(pager.canGoOlder, false);
});

test('the first page of three can only go older', () => {
  const pager = pagerState(0, 23, 10);

  assert.equal(pager.summary, '23 event(s) · page 1 of 3');
  assert.equal(pager.canGoNewer, false);
  assert.equal(pager.canGoOlder, true);
});

test('the last page can only go newer', () => {
  const pager = pagerState(20, 23, 10);

  assert.equal(pager.summary, '23 event(s) · page 3 of 3');
  assert.equal(pager.canGoNewer, true);
  assert.equal(pager.canGoOlder, false);
});

test('a total that is an exact multiple of the page size does not invent a page', () => {
  // The off-by-one that would show "page 3 of 3" with nothing on it.
  const full = pagerState(10, 20, 10);

  assert.equal(full.pages, 2);
  assert.equal(full.summary, '20 event(s) · page 2 of 2');
  assert.equal(full.canGoOlder, false);
});

test('a single page that is not full is still one page', () => {
  const pager = pagerState(0, 4, 10);

  assert.equal(pager.summary, '4 event(s) · page 1 of 1');
  assert.equal(pager.canGoNewer, false);
  assert.equal(pager.canGoOlder, false);
});
