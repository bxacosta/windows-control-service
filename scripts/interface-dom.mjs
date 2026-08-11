// Renders every section of the interface against fixed data and writes the resulting DOM to a
// file, so a change meant to be cosmetic can be proved to be exactly that: capture, change,
// capture again, compare. A difference is a regression.
//
// Nothing here touches the machine. The page is served by the running service, but fetch, the
// event stream and the clock are all replaced inside the browser before the modules load, so no
// policy is deployed, no registry value is written and no session is used. That is also what
// makes the output comparable: a live capture would embed "checked 3 s ago" and never match
// itself twice.
//
// Local development tooling. Node is not part of the build or the deployment of the service.
//
//   node scripts/interface-dom.mjs --out=before.txt
//   node scripts/interface-dom.mjs --out=after.txt
//   git diff --no-index before.txt after.txt

import { spawn } from 'node:child_process';
import { writeFileSync } from 'node:fs';
import { setTimeout as delay } from 'node:timers/promises';

const args = Object.fromEntries(
  process.argv.slice(2).map((argument) => {
    const [name, ...rest] = argument.replace(/^--/, '').split('=');
    return [name, rest.join('=')];
  }),
);

const origin = args.origin ?? 'http://localhost:5150';
const outputPath = args.out ?? 'interface-dom.txt';
const port = Number(args.port ?? 9334);
const browserPath = args.browser ?? 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';

// Frozen, so "checked 40 s ago" is a constant. Every timestamp below is relative to it.
const NOW = Date.parse('2026-08-19T12:00:00Z');
const ISO = (secondsAgo) => new Date(NOW - secondsAgo * 1000).toISOString();

// --- The data every scenario is built from ---------------------------------

const HEALTH = { status: 'healthy', version: '1.0.0+0123456789abcdef0123456789abcdef01234567' };

const APPLICATIONS = [
  {
    id: 1,
    name: 'Test Target A',
    executablePath: 'C:\\ProgramData\\WindowsControlService\\test\\target-a.exe',
    matchAttribute: 'FileName',
    matchValue: 'target-a.exe',
    productName: 'Harmless Test Target',
    isEnabled: true,
    createdAt: ISO(7200),
  },
  {
    id: 2,
    name: 'Test Target B',
    executablePath: 'C:\\ProgramData\\WindowsControlService\\test\\target-b.exe',
    matchAttribute: 'ProductName',
    matchValue: 'Harmless Test Target',
    productName: 'Harmless Test Target',
    isEnabled: false,
    createdAt: ISO(3600),
  },
];

const PROCESSES = [
  { name: 'Test Target A', executablePath: 'C:\\ProgramData\\WindowsControlService\\test\\target-a.exe' },
  { name: 'Another Target', executablePath: 'C:\\ProgramData\\WindowsControlService\\test\\other.exe' },
];

const HISTORY_ROWS = (count, from) =>
  Array.from({ length: count }, (unused, index) => ({
    id: from + index,
    occurredAt: ISO(3600 * (from + index)),
    kind: index % 2 === 0 ? 'Logon' : 'Logoff',
    origin: index % 3 === 0 ? 'Remote' : 'Local',
    address: index % 3 === 0 ? '203.0.113.44' : null,
    userName: 'MACHINE\\owner',
    sessionId: 2 + index,
    durationSeconds: index % 2 === 0 ? null : 5400,
  }));

const OK = (body) => ({ status: 200, body });
const NO_CONTENT = { status: 204 };
const PROBLEM = (status, title) => ({ status, body: { title } });

const BASE = {
  'GET /api/health': OK(HEALTH),
  'GET /api/auth/session': OK({ initialized: true, authenticated: true }),
  'GET /api/applications': OK([]),
  'GET /api/applications/policy-state': OK({ state: 'Unknown', enabledRuleCount: 0, lastReconciledAt: null }),
  'GET /api/processes': OK(PROCESSES),
  'GET /api/devices/usb': OK({ blocked: false, lastModified: null }),
  'GET /api/access-history?limit=10&offset=0': OK({ total: 0, entries: [] }),
};

const withResponses = (extra) => ({ ...BASE, ...extra });

// --- What each scenario shows ----------------------------------------------

const SECTION = (name) => `document.getElementById('section-${name}').outerHTML`;
const NOTICES = "document.getElementById('notices').outerHTML";
const GATE = "document.getElementById('app-nav').outerHTML + '\\n' + document.getElementById('gate').outerHTML";

const scenarios = [
  {
    name: 'gate · first run, no password configured',
    responses: withResponses({ 'GET /api/auth/session': OK({ initialized: false, authenticated: false }) }),
    capture: [GATE, NOTICES],
  },
  {
    name: 'gate · configured but signed out',
    responses: withResponses({ 'GET /api/auth/session': OK({ initialized: true, authenticated: false }) }),
    capture: [GATE, NOTICES],
  },
  {
    name: 'gate · a wrong password keeps what was typed',
    responses: withResponses({
      'GET /api/auth/session': OK({ initialized: true, authenticated: false }),
      'POST /api/auth/login': PROBLEM(401, 'Wrong password.'),
    }),
    steps: [
      "document.getElementById('login-password').value = 'not-the-password';",
      "document.getElementById('login-form').requestSubmit(); await window.__wcs.settle();",
    ],
    capture: [GATE, NOTICES, "'value: ' + document.getElementById('login-password').value"],
  },
  {
    name: 'shell · navigation and footer once signed in',
    hash: '#/applications',
    responses: withResponses({}),
    capture: [
      "document.getElementById('app-nav').outerHTML",
      "document.getElementById('service-status').outerHTML",
    ],
  },

  // --- Applications --------------------------------------------------------
  {
    name: 'applications · nothing blocked, policy state unknown',
    hash: '#/applications',
    responses: withResponses({}),
    capture: [SECTION('applications')],
  },
  {
    name: 'applications · nothing blocked, no policy deployed',
    hash: '#/applications',
    responses: withResponses({
      'GET /api/applications/policy-state': OK({ state: 'NotEnforced', enabledRuleCount: 0, lastReconciledAt: ISO(40) }),
    }),
    capture: [SECTION('applications')],
  },
  {
    name: 'applications · two rules, policy enforced',
    hash: '#/applications',
    responses: withResponses({
      'GET /api/applications': OK(APPLICATIONS),
      'GET /api/applications/policy-state': OK({ state: 'Enforced', enabledRuleCount: 1, lastReconciledAt: ISO(40) }),
    }),
    capture: [SECTION('applications')],
  },
  {
    name: 'applications · rules waiting for a policy that is not in force',
    hash: '#/applications',
    responses: withResponses({
      'GET /api/applications': OK(APPLICATIONS),
      'GET /api/applications/policy-state': OK({ state: 'NotEnforced', enabledRuleCount: 2, lastReconciledAt: ISO(200) }),
    }),
    capture: ["document.getElementById('policy-state-line').outerHTML"],
  },
  {
    name: 'applications · the policy state could not be read at all',
    hash: '#/applications',
    responses: withResponses({
      'GET /api/applications/policy-state': PROBLEM(500, 'CiTool did not answer.'),
    }),
    capture: ["document.getElementById('policy-state-line').outerHTML", NOTICES],
  },
  {
    name: 'applications · the running process list',
    hash: '#/applications',
    responses: withResponses({}),
    steps: ["document.getElementById('load-processes').click(); await window.__wcs.settle();"],
    capture: ["document.getElementById('process-list').outerHTML"],
  },
  {
    name: 'applications · picking a process fills the form',
    hash: '#/applications',
    responses: withResponses({}),
    steps: [
      "document.getElementById('load-processes').click(); await window.__wcs.settle();",
      "document.querySelectorAll('#process-list button')[1].click(); await window.__wcs.settle();",
    ],
    capture: [
      "'path: ' + document.getElementById('application-path').value",
      "'name: ' + document.getElementById('application-name').value",
      "'focused: ' + document.activeElement.id",
    ],
  },
  {
    name: 'applications · an empty process list',
    hash: '#/applications',
    responses: withResponses({ 'GET /api/processes': OK([]) }),
    steps: ["document.getElementById('load-processes').click(); await window.__wcs.settle();"],
    capture: ["document.getElementById('process-list').outerHTML"],
  },
  {
    name: 'applications · a refused executable explains itself in the form',
    hash: '#/applications',
    responses: withResponses({
      'POST /api/applications': PROBLEM(400, 'That executable carries no version information, so a rule has nothing to match against.'),
    }),
    steps: [
      "document.getElementById('application-path').value = 'C:\\\\bare.exe';",
      "document.getElementById('add-application-form').requestSubmit(); await window.__wcs.settle();",
    ],
    capture: [
      "document.getElementById('add-application-error').outerHTML",
      "'path kept: ' + document.getElementById('application-path').value",
      NOTICES,
    ],
  },
  {
    name: 'applications · a successful block clears the form',
    hash: '#/applications',
    responses: withResponses({ 'POST /api/applications': OK({ id: 3 }) }),
    steps: [
      "document.getElementById('application-path').value = 'C:\\\\target.exe';",
      "document.getElementById('application-name').value = 'Target';",
      "document.getElementById('add-application-form').requestSubmit(); await window.__wcs.settle();",
    ],
    capture: [
      "'path: [' + document.getElementById('application-path').value + ']'",
      "'name: [' + document.getElementById('application-name').value + ']'",
      NOTICES,
    ],
  },
  {
    // Rule 1. The switch shows what was asked for, and goes back when the service says no.
    name: 'applications · a refused toggle snaps the switch back',
    hash: '#/applications',
    responses: withResponses({
      'GET /api/applications': OK(APPLICATIONS),
      'PATCH /api/applications/1': PROBLEM(409, 'The policy is being rebuilt.'),
    }),
    steps: ["document.querySelector('#application-list input[type=checkbox]').click(); await window.__wcs.settle();"],
    capture: [
      "'switch: ' + document.querySelector('#application-list input[type=checkbox]').checked",
      NOTICES,
    ],
  },
  {
    name: 'applications · an accepted toggle keeps the value and reloads',
    hash: '#/applications',
    responses: withResponses({
      'GET /api/applications': OK(APPLICATIONS),
      'PATCH /api/applications/2': NO_CONTENT,
    }),
    steps: [
      "document.querySelectorAll('#application-list input[type=checkbox]')[1].click(); await window.__wcs.settle();",
    ],
    capture: [
      "'calls: ' + window.__wcs.calls.filter((c) => c.includes('applications')).join(' | ')",
      NOTICES,
    ],
  },
  {
    // Rule 2. A DELETE that fails means the application is still blocked, so the row stays.
    name: 'applications · a failed removal keeps the row',
    hash: '#/applications',
    responses: withResponses({
      'GET /api/applications': OK(APPLICATIONS),
      'DELETE /api/applications/1': PROBLEM(500, 'The policy could not be rebuilt.'),
    }),
    steps: ["document.querySelectorAll('#application-list button')[0].click(); await window.__wcs.settle();"],
    capture: [
      "'rows: ' + document.querySelectorAll('#application-list .row').length",
      "'reloaded: ' + window.__wcs.calls.filter((c) => c === 'GET /api/applications').length",
      NOTICES,
    ],
  },
  {
    name: 'applications · a successful removal reloads the list',
    hash: '#/applications',
    responses: withResponses({
      'GET /api/applications': OK(APPLICATIONS),
      'DELETE /api/applications/1': NO_CONTENT,
    }),
    steps: ["document.querySelectorAll('#application-list button')[0].click(); await window.__wcs.settle();"],
    capture: [
      "'reloaded: ' + window.__wcs.calls.filter((c) => c === 'GET /api/applications').length",
      NOTICES,
    ],
  },
  {
    // Rule 4. A pushed policy state must not rebuild the form under a half-typed path.
    name: 'applications · a pushed state leaves the half-typed form alone',
    hash: '#/applications',
    responses: withResponses({}),
    steps: [
      "document.getElementById('application-path').value = 'C:\\\\Half\\\\typed';",
      "document.getElementById('application-path').focus();",
      "document.getElementById('application-path').setSelectionRange(7, 7);",
      "window.__wcs.push('policy-state', { state: 'Enforced', enabledRuleCount: 3, lastReconciledAt: '" + ISO(5) + "' }); await window.__wcs.settle();",
    ],
    capture: [
      "'path: ' + document.getElementById('application-path').value",
      "'caret: ' + document.getElementById('application-path').selectionStart",
      "'focused: ' + (document.activeElement.id === 'application-path')",
      "document.getElementById('policy-state-line').outerHTML",
    ],
  },

  // --- Devices -------------------------------------------------------------
  {
    name: 'devices · allowed, never changed through the service',
    hash: '#/devices',
    responses: withResponses({}),
    capture: [SECTION('devices')],
  },
  {
    name: 'devices · blocked, with a last change',
    hash: '#/devices',
    responses: withResponses({ 'GET /api/devices/usb': OK({ blocked: true, lastModified: ISO(900) }) }),
    capture: [SECTION('devices')],
  },
  {
    // Rule 6. What the switch shows after a write is what the service reports, not what was asked.
    name: 'devices · the state after a write is re-read from the service',
    hash: '#/devices',
    responses: withResponses({ 'PUT /api/devices/usb': NO_CONTENT }),
    steps: [
      "window.__wcs.after('PUT /api/devices/usb', 'GET /api/devices/usb', " +
        "{ status: 200, body: { blocked: true, lastModified: '" + ISO(1) + "' } });",
      "document.getElementById('usb-switch').click(); await window.__wcs.settle();",
    ],
    capture: [SECTION('devices'), NOTICES, "'calls: ' + window.__wcs.calls.filter((c) => c.includes('usb')).join(' | ')"],
  },
  {
    name: 'devices · a refused write snaps the switch back',
    hash: '#/devices',
    responses: withResponses({
      'PUT /api/devices/usb': PROBLEM(500, 'The registry value could not be written.'),
    }),
    steps: ["document.getElementById('usb-switch').click(); await window.__wcs.settle();"],
    capture: ["'switch: ' + document.getElementById('usb-switch').checked", NOTICES],
  },
  {
    // Rule 5. A pushed update must not move the switch out from under a click still in flight.
    name: 'devices · a push does not move a switch with a click in flight',
    hash: '#/devices',
    responses: withResponses({}),
    steps: [
      "window.__wcs.hold = 'PUT /api/devices/usb';",
      "document.getElementById('usb-switch').click(); await window.__wcs.settle();",
      "window.__wcs.push('usb', { blocked: false, lastModified: null }); await window.__wcs.settle();",
    ],
    capture: [
      "'switch while busy: ' + document.getElementById('usb-switch').checked",
      "'busy: ' + document.getElementById('usb-switch').hasAttribute('data-busy')",
      "document.getElementById('usb-state-title').outerHTML",
    ],
  },
  {
    name: 'devices · a push moves the switch when nothing is in flight',
    hash: '#/devices',
    responses: withResponses({}),
    steps: ["window.__wcs.push('usb', { blocked: true, lastModified: '" + ISO(5) + "' }); await window.__wcs.settle();"],
    capture: [SECTION('devices')],
  },

  // --- History -------------------------------------------------------------
  {
    name: 'history · empty',
    hash: '#/history',
    responses: withResponses({}),
    capture: [SECTION('history')],
  },
  {
    name: 'history · first page of three',
    hash: '#/history',
    responses: withResponses({
      'GET /api/access-history?limit=10&offset=0': OK({ total: 23, entries: HISTORY_ROWS(10, 1) }),
    }),
    capture: [SECTION('history')],
  },
  {
    name: 'history · the last page',
    hash: '#/history',
    responses: withResponses({
      'GET /api/access-history?limit=10&offset=0': OK({ total: 23, entries: HISTORY_ROWS(10, 1) }),
      'GET /api/access-history?limit=10&offset=10': OK({ total: 23, entries: HISTORY_ROWS(10, 11) }),
      'GET /api/access-history?limit=10&offset=20': OK({ total: 23, entries: HISTORY_ROWS(3, 21) }),
    }),
    steps: [
      "document.getElementById('history-next').click(); await window.__wcs.settle();",
      "document.getElementById('history-next').click(); await window.__wcs.settle();",
    ],
    capture: [
      "document.getElementById('history-summary').outerHTML",
      "'rows: ' + document.querySelectorAll('#history-rows tr').length",
      "'newer disabled: ' + document.getElementById('history-previous').disabled",
      "'older disabled: ' + document.getElementById('history-next').disabled",
    ],
  },
  {
    name: 'history · filtering by origin resets to the first page',
    hash: '#/history',
    responses: withResponses({
      'GET /api/access-history?limit=10&offset=0': OK({ total: 23, entries: HISTORY_ROWS(10, 1) }),
      'GET /api/access-history?limit=10&offset=10': OK({ total: 23, entries: HISTORY_ROWS(10, 11) }),
      'GET /api/access-history?limit=10&offset=0&origin=remote': OK({ total: 4, entries: HISTORY_ROWS(4, 1) }),
    }),
    steps: [
      "document.getElementById('history-next').click(); await window.__wcs.settle();",
      "document.getElementById('history-origin').value = 'remote';",
      "document.getElementById('history-origin').dispatchEvent(new Event('change')); await window.__wcs.settle();",
    ],
    capture: [
      "document.getElementById('history-summary').outerHTML",
      "'rows: ' + document.querySelectorAll('#history-rows tr').length",
      "'calls: ' + window.__wcs.calls.filter((c) => c.includes('access-history')).join(' | ')",
    ],
  },
  {
    // Rule 8. An offset past the end steps back instead of showing an empty table.
    name: 'history · an offset past the end steps back',
    hash: '#/history',
    responses: withResponses({
      'GET /api/access-history?limit=10&offset=0': OK({ total: 23, entries: HISTORY_ROWS(10, 1) }),
      'GET /api/access-history?limit=10&offset=10': OK({ total: 6, entries: [] }),
    }),
    steps: ["document.getElementById('history-next').click(); await window.__wcs.settle();"],
    capture: [
      "document.getElementById('history-summary').outerHTML",
      "'rows: ' + document.querySelectorAll('#history-rows tr').length",
      "'empty shown: ' + !document.getElementById('history-empty').hidden",
      "'calls: ' + window.__wcs.calls.filter((c) => c.includes('access-history')).join(' | ')",
    ],
  },
  {
    // Rule 7, first half: the first page follows the stream while the section is on screen.
    name: 'history · the first page follows a pushed event',
    hash: '#/history',
    responses: withResponses({
      'GET /api/access-history?limit=10&offset=0': OK({ total: 23, entries: HISTORY_ROWS(10, 1) }),
    }),
    steps: [
      "window.__wcs.override('GET /api/access-history?limit=10&offset=0', { status: 200, body: { total: 24, entries: "
        + "[{ id: 99, occurredAt: '" + ISO(1) + "', kind: 'Logon', origin: 'Local', address: null, userName: 'MACHINE\\\\owner', sessionId: 9, durationSeconds: null }] } });",
      "window.__wcs.push('access-history', { total: 24 }); await window.__wcs.settle();",
    ],
    capture: [
      "document.getElementById('history-summary').outerHTML",
      "'rows: ' + document.querySelectorAll('#history-rows tr').length",
    ],
  },
  {
    // Rule 7, second half: a page other than the first does not move under the reader.
    name: 'history · a later page does not move under a pushed event',
    hash: '#/history',
    responses: withResponses({
      'GET /api/access-history?limit=10&offset=0': OK({ total: 23, entries: HISTORY_ROWS(10, 1) }),
      'GET /api/access-history?limit=10&offset=10': OK({ total: 23, entries: HISTORY_ROWS(10, 11) }),
    }),
    steps: [
      "document.getElementById('history-next').click(); await window.__wcs.settle();",
      "window.__wcs.calls.length = 0;",
      "window.__wcs.push('access-history', { total: 30 }); await window.__wcs.settle();",
    ],
    capture: [
      "document.getElementById('history-summary').outerHTML",
      "'first cell: ' + document.querySelector('#history-rows td').textContent",
      "'reloads: ' + window.__wcs.calls.filter((c) => c.includes('access-history')).length",
    ],
  },
  {
    // Rule 7, third half: nor does the first page while the section is not on screen.
    name: 'history · nothing reloads while the section is not on screen',
    hash: '#/devices',
    responses: withResponses({
      'GET /api/access-history?limit=10&offset=0': OK({ total: 23, entries: HISTORY_ROWS(10, 1) }),
    }),
    steps: [
      "window.__wcs.calls.length = 0;",
      "window.__wcs.push('access-history', { total: 30 }); await window.__wcs.settle();",
    ],
    capture: [
      "document.getElementById('history-summary').outerHTML",
      "'reloads: ' + window.__wcs.calls.filter((c) => c.includes('access-history')).length",
    ],
  },

  // --- Settings ------------------------------------------------------------
  {
    name: 'settings · at rest',
    hash: '#/settings',
    responses: withResponses({}),
    capture: [SECTION('settings')],
  },
  {
    name: 'settings · two new passwords that do not match',
    hash: '#/settings',
    responses: withResponses({}),
    steps: [
      "document.getElementById('current-password').value = 'old';",
      "document.getElementById('new-password').value = 'one';",
      "document.getElementById('confirm-password').value = 'other';",
      "document.getElementById('change-password-form').requestSubmit(); await window.__wcs.settle();",
    ],
    capture: [
      "document.getElementById('change-password-error').outerHTML",
      "'calls: ' + window.__wcs.calls.filter((c) => c.includes('auth/password')).join(' | ')",
    ],
  },
  {
    name: 'settings · a wrong current password',
    hash: '#/settings',
    responses: withResponses({ 'PUT /api/auth/password': PROBLEM(401, 'no') }),
    steps: [
      "document.getElementById('current-password').value = 'wrong';",
      "document.getElementById('new-password').value = 'same';",
      "document.getElementById('confirm-password').value = 'same';",
      "document.getElementById('change-password-form').requestSubmit(); await window.__wcs.settle();",
    ],
    capture: ["document.getElementById('change-password-error').outerHTML"],
  },
  {
    name: 'settings · a successful change returns to the gate',
    hash: '#/settings',
    responses: withResponses({ 'PUT /api/auth/password': NO_CONTENT }),
    steps: [
      "document.getElementById('current-password').value = 'old';",
      "document.getElementById('new-password').value = 'new';",
      "document.getElementById('confirm-password').value = 'new';",
      "document.getElementById('change-password-form').requestSubmit(); await window.__wcs.settle();",
    ],
    capture: [GATE, NOTICES, "'main hidden: ' + document.getElementById('main').hidden"],
  },
  {
    name: 'settings · signing out',
    hash: '#/settings',
    responses: withResponses({ 'POST /api/auth/logout': NO_CONTENT }),
    steps: ["document.getElementById('sign-out').click(); await window.__wcs.settle();"],
    capture: [GATE, NOTICES],
  },
  {
    // The single 401 door: two protected calls answer 401, and the gate is reached once.
    name: 'shell · a 401 anywhere lands on the gate exactly once',
    hash: '#/applications',
    responses: withResponses({
      'GET /api/applications': PROBLEM(401, 'no'),
      'GET /api/applications/policy-state': PROBLEM(401, 'no'),
    }),
    capture: [GATE, "'notices: ' + document.querySelectorAll('#notices .notice').length", NOTICES],
  },
];

// --- The page, with the service replaced -----------------------------------

const bootstrap = (responses) => `
(() => {
  const table = ${JSON.stringify(responses)};
  const NOW = ${NOW};
  Date.now = () => NOW;
  window.confirm = () => true;

  const listeners = new Map();
  class StubEventSource {
    constructor(url) { this.url = url; this.readyState = 1; }
    addEventListener(name, handler) { listeners.set(name, handler); }
    close() { this.readyState = 2; }
  }
  StubEventSource.CONNECTING = 0;
  StubEventSource.OPEN = 1;
  StubEventSource.CLOSED = 2;
  window.EventSource = StubEventSource;

  let inFlight = 0;
  const followUps = new Map();

  window.__wcs = {
    calls: [],
    hold: null,
    push: (name, payload) => listeners.get(name) && listeners.get(name)({ data: JSON.stringify(payload) }),
    override: (key, entry) => { table[key] = entry; },
    // Swaps one answer for another the moment a given call is made. That is how "read back after
    // writing" is shown: the answer changes because the write happened, not because it was asked.
    after: (trigger, key, entry) => followUps.set(trigger, [key, entry]),
    settle: async () => {
      const pause = () => new Promise((resolve) => setTimeout(resolve, 20));

      // The page boots through a dynamic import, so on the first settle of a scenario the
      // modules may not have run yet and nothing is in flight because nothing has started.
      // Waiting for idle without this reports "settled" before the page has done anything.
      for (let attempt = 0; attempt < 150 && window.__wcs.calls.length === 0; attempt++) {
        await pause();
      }

      // Three consecutive idle polls, not one: two awaited calls in a row are separated by a
      // microtask, and a single poll can land in that gap and call it finished.
      let idle = 0;
      for (let attempt = 0; attempt < 200 && idle < 3; attempt++) {
        await pause();
        idle = inFlight === 0 ? idle + 1 : 0;
      }
    },
  };

  window.fetch = async (path, init) => {
    const method = (init && init.method) || 'GET';
    const key = method + ' ' + path;
    window.__wcs.calls.push(key);

    const followUp = followUps.get(key);
    if (followUp) { table[followUp[0]] = followUp[1]; followUps.delete(key); }

    // Never resolves: the control stays busy for the rest of the scenario.
    if (window.__wcs.hold === key) { return new Promise(() => {}); }

    inFlight++;
    try {
      await new Promise((resolve) => setTimeout(resolve, 0));
      const entry = table[key];
      if (!entry) {
        return new Response(JSON.stringify({ title: 'No canned answer for ' + key }), { status: 599 });
      }

      const status = entry.status || 200;
      const body = status === 204 || entry.body === undefined ? null : JSON.stringify(entry.body);
      return new Response(body, { status, headers: { 'content-type': 'application/json' } });
    } finally {
      inFlight--;
    }
  };
})();
`;

const browser = spawn(browserPath, [
  '--headless=old',
  '--disable-gpu',
  '--no-sandbox',
  '--no-first-run',
  `--remote-debugging-port=${port}`,
  `--user-data-dir=${args.profile ?? 'C:\\Windows\\Temp\\wcs-dom-profile'}`,
  '--window-size=1200,1400',
  'about:blank',
], { stdio: 'ignore' });

let socketUrl = null;
for (let attempt = 0; attempt < 60 && !socketUrl; attempt++) {
  await delay(500);
  try {
    socketUrl = (await (await fetch(`http://127.0.0.1:${port}/json/version`)).json()).webSocketDebuggerUrl;
  } catch {
    // Not listening yet.
  }
}

if (!socketUrl) {
  browser.kill();
  throw new Error('The browser never opened its debugging port.');
}

const socket = new WebSocket(socketUrl);
await new Promise((resolve, reject) => {
  socket.addEventListener('open', resolve, { once: true });
  socket.addEventListener('error', reject, { once: true });
});

let nextId = 0;
const waiting = new Map();
let onLoad = null;

socket.addEventListener('message', (message) => {
  const payload = JSON.parse(message.data);
  if (payload.method === 'Page.loadEventFired') {
    onLoad?.();
    return;
  }

  const pending = waiting.get(payload.id);
  if (pending) {
    waiting.delete(payload.id);
    pending(payload);
  }
});

const send = (method, params = {}, sessionId) =>
  new Promise((resolve) => {
    const id = ++nextId;
    waiting.set(id, resolve);
    socket.send(JSON.stringify({ id, method, params, sessionId }));
  });

const { result: target } = await send('Target.createTarget', { url: 'about:blank' });
const { result: attached } = await send('Target.attachToTarget', { targetId: target.targetId, flatten: true });
const session = attached.sessionId;

await send('Page.enable', {}, session);
await send('Runtime.enable', {}, session);

const evaluate = async (expression) => {
  const answer = await send('Runtime.evaluate', {
    expression: `(async () => { ${expression} })()`,
    awaitPromise: true,
    returnByValue: true,
  }, session);

  const details = answer.result.exceptionDetails;
  if (details) {
    throw new Error(`${expression}\n  -> ${details.exception?.description ?? details.text}`);
  }

  return answer.result.result.value;
};

const blocks = [];

for (const scenario of scenarios) {
  const injected = await send(
    'Page.addScriptToEvaluateOnNewDocument',
    { source: bootstrap(scenario.responses) },
    session);

  const navigated = new Promise((resolve) => { onLoad = resolve; });
  await send('Page.navigate', { url: `${origin}/${scenario.hash ?? ''}` }, session);
  await navigated;

  // The first paint goes through the same stub, so waiting on it is enough.
  await evaluate('await window.__wcs.settle();');

  for (const step of scenario.steps ?? []) {
    await evaluate(step);
  }

  const captured = [];
  for (const expression of scenario.capture) {
    captured.push(String(await evaluate(`return ${expression};`)));
  }

  blocks.push(`### ${scenario.name}\n${captured.join('\n')}`);
  process.stdout.write(`${scenario.name}\n`);

  await send('Page.removeScriptToEvaluateOnNewDocument', { identifier: injected.result.identifier }, session);
  await send('Page.navigate', { url: 'about:blank' }, session);
}

writeFileSync(outputPath, `${blocks.join('\n\n')}\n`, 'utf8');
socket.close();
browser.kill();

process.stdout.write(`\n${scenarios.length} scenarios -> ${outputPath}\n`);
