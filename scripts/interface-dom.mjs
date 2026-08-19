// Renders every section of the interface against fixed data and writes the resulting DOM to a
// file, so a change meant to be cosmetic can be proved to be exactly that: capture, change,
// capture again, compare. A difference is a regression.
//
// Nothing here touches the machine, and the service does not even have to be running: fetch,
// the event stream and the clock are all replaced inside the browser before the modules load,
// and wwwroot is served straight off disk. So no policy is deployed, no registry value is
// written and no session is used. That is also what makes the output comparable -- a live
// capture would embed "checked 3 s ago" and never match itself twice.
//
// Local development tooling. Node is not part of the build or the deployment of the service.
//
//   node scripts/interface-dom.mjs --out=before.txt
//   node scripts/interface-dom.mjs --out=after.txt
//   git diff --no-index before.txt after.txt

import { spawn } from 'node:child_process';
import { readFile } from 'node:fs/promises';
import { createServer } from 'node:http';
import { extname, join, normalize, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { writeFileSync } from 'node:fs';
import { setTimeout as delay } from 'node:timers/promises';

const args = Object.fromEntries(
  process.argv.slice(2).map((argument) => {
    const [name, ...rest] = argument.replace(/^--/, '').split('=');
    return [name, rest.join('=')];
  }),
);

// The whole service is simulated inside the browser, so nothing here needs the service to be
// running: the page only has to be served over http for ES modules to load. Serving wwwroot
// directly is also what makes this usable while editing -- no publish, no install, no restart.
// Pass --origin to point at a running instance instead.
// Normalised so the traversal guard below compares separators of the same kind: a path given
// on the command line arrives with forward slashes even on Windows.
const webRoot = normalize(args.serve ?? fileURLToPath(new URL('../src/WindowsControlService/wwwroot', import.meta.url)));
const outputPath = args.out ?? 'interface-dom.txt';
const port = Number(args.port ?? 9334);

const CONTENT_TYPES = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.mjs': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.svg': 'image/svg+xml',
  '.json': 'application/json; charset=utf-8',
};

let origin = args.origin;
let files = null;

if (!origin) {
  files = createServer(async (request, response) => {
    const path = new URL(request.url, 'http://x').pathname;
    // Normalised and re-checked against the root: a served directory is a served directory.
    const target = normalize(join(webRoot, path === '/' ? 'index.html' : path));

    if (!target.startsWith(webRoot + sep)) {
      response.writeHead(403).end();
      return;
    }

    try {
      const body = await readFile(target);
      response.writeHead(200, { 'content-type': CONTENT_TYPES[extname(target)] ?? 'application/octet-stream' });
      response.end(body);
    } catch {
      response.writeHead(404).end();
    }
  });

  await new Promise((resolve) => files.listen(0, '127.0.0.1', resolve));
  origin = `http://127.0.0.1:${files.address().port}`;
}
const browserPath = args.browser ?? 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';

// Frozen, so "checked 40 s ago" is a constant. Every timestamp below is relative to it.
const NOW = Date.parse('2026-08-19T12:00:00Z');
const ISO = (secondsAgo) => new Date(NOW - secondsAgo * 1000).toISOString();

// --- The data every scenario is built from ---------------------------------

// "running" because that is the only value the endpoint can return -- it is a literal in
// HealthEndpoints. The mock used to say "healthy", a value the service cannot produce, and that
// is exactly why 56 scenarios passed with the indicator broken.
const HEALTH = { status: 'running', version: '1.0.0+0123456789abcdef0123456789abcdef01234567' };

/** Not an error status: a request that never arrives. */
const OFFLINE = { offline: true };

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

// All four kinds, in the proportion the real machine shows them: Reconnect and Disconnect are
// the traffic, and Logon and Logoff the exception. Rows built only from Logon and Logoff are
// what let a direction derived from `kind === 'Logon'` look correct for a whole release.
const HISTORY_KINDS = ['Logon', 'Disconnect', 'Reconnect', 'Disconnect', 'Reconnect', 'Logoff'];

const HISTORY_ROWS = (count, from) =>
  Array.from({ length: count }, (unused, index) => {
    const kind = HISTORY_KINDS[(from + index) % HISTORY_KINDS.length];
    const startsSession = kind === 'Logon' || kind === 'Reconnect';

    return {
      id: from + index,
      occurredAt: ISO(3600 * (from + index)),
      kind,
      startsSession,
      origin: index % 3 === 0 ? 'Remote' : 'Local',
      address: index % 3 === 0 ? '203.0.113.44' : null,
      userName: 'MACHINE\\owner',
      sessionId: 2 + index,
      durationSeconds: startsSession ? null : 5400,
    };
  });

const OK = (body) => ({ status: 200, body });

// The minimum password length and the session timeout are the service's rules, and the interface
// counts against them while typing rather than keeping a copy of the numbers.
const SESSION = (initialized, authenticated) => ({
  initialized,
  authenticated,
  minimumPasswordLength: 6,
  requiresLettersAndDigits: true,
  sessionTimeoutMinutes: 10,
});
const NO_CONTENT = { status: 204 };
const PROBLEM = (status, title) => ({ status, body: { title } });

const BASE = {
  'GET /api/health': OK(HEALTH),
  'GET /api/auth/session': OK(SESSION(true, true)),
  'GET /api/applications': OK([]),
  'GET /api/applications/policy-state': OK({ state: 'Unknown', enabledRuleCount: 0, lastReconciledAt: null }),
  'GET /api/processes': OK(PROCESSES),
  'GET /api/devices/usb': OK({ blocked: false, lastModified: null }),
  'GET /api/access-history?limit=10&offset=0': OK({ total: 0, entries: [] }),
};

const withResponses = (extra) => ({ ...BASE, ...extra });

// A stopped service does not answer one call badly, it answers none at all. Faking only the
// health call offline showed the dot green, and rightly: the other calls of the boot did arrive,
// so the service was there.
const NOTHING_ANSWERS = Object.fromEntries(Object.keys(BASE).map((key) => [key, OFFLINE]));

// --- What each scenario shows ----------------------------------------------

const SECTION = (name) => `document.getElementById('section-${name}').outerHTML`;
const NOTICES = "document.getElementById('notices').outerHTML";
const GATE = "document.getElementById('app-nav').outerHTML + '\\n' + document.getElementById('gate').outerHTML";

const scenarios = [
  {
    name: 'gate · first run, no password configured',
    responses: withResponses({ 'GET /api/auth/session': OK(SESSION(false, false)) }),
    capture: [GATE, NOTICES],
  },
  {
    name: 'gate · configured but signed out',
    responses: withResponses({ 'GET /api/auth/session': OK(SESSION(true, false)) }),
    capture: [GATE, NOTICES],
  },
  {
    name: 'gate · a wrong password keeps what was typed',
    responses: withResponses({
      'GET /api/auth/session': OK(SESSION(true, false)),
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
  {
    // The dot reports whether the call arrived, not a word inside it. It compared `status`
    // against "healthy" while the service can only ever say "running", so on a working machine
    // the dot sat grey forever and only the failure state worked.
    name: 'shell · the health dot answers whether the call arrived',
    hash: '#/applications',
    responses: withResponses({}),
    capture: [
      "'dot: ' + document.getElementById('health-dot').getAttribute('data-health')",
      "'status: ' + document.getElementById('service-status').textContent",
    ],
  },
  {
    name: 'shell · a service that does not answer turns the dot red',
    hash: '#/applications',
    responses: NOTHING_ANSWERS,
    capture: [
      "'dot: ' + document.getElementById('health-dot').getAttribute('data-health')",
      "'status: ' + document.getElementById('service-status').textContent",
    ],
  },
  {
    // Every call, not only the one at boot: a tab left open after the service stops must not go
    // on showing the green it earned when the page loaded. The words follow the same signal --
    // "running · 1.0.0" beside a red dot is a version of a service that stopped answering -- and
    // coming back asks again instead of restoring what was on screen before the gap.
    name: 'shell · a later call that never arrives turns the dot red too',
    hash: '#/applications',
    responses: withResponses({}),
    steps: [
      "window.__wcs.beforeClick = document.getElementById('health-dot').getAttribute('data-health');",
      "window.__wcs.wordsAtBoot = document.getElementById('service-status').textContent;",
      "window.__wcs.override('GET /api/processes', { offline: true });",
      "document.getElementById('load-processes').click(); await window.__wcs.settle();",
      "window.__wcs.dotWhileGone = document.getElementById('health-dot').getAttribute('data-health');",
      "window.__wcs.whileGone = document.getElementById('service-status').textContent;",
      "window.__wcs.override('GET /api/processes', { status: 200, body: " + JSON.stringify(PROCESSES) + " });",
      "document.getElementById('load-processes').click(); await window.__wcs.settle();",
    ],
    capture: [
      "'dot at boot: ' + window.__wcs.beforeClick",
      "'status at boot: ' + window.__wcs.wordsAtBoot",
      "'dot after a call that failed: ' + window.__wcs.dotWhileGone",
      "'status after a call that failed: ' + window.__wcs.whileGone",
      "'dot once it answers again: ' + document.getElementById('health-dot').getAttribute('data-health')",
      "'status once it answers again: ' + document.getElementById('service-status').textContent",
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
    // The three attributes a WDAC rule can match on. InternalName was never rendered until this
    // scenario existed, which is the gap that let the other two mistakes live: nothing notices a
    // value the simulated data never produces.
    name: 'applications · the three attributes a rule can match on',
    hash: '#/applications',
    responses: withResponses({
      'GET /api/applications': OK([
        { id: 1, name: 'Test Target A', executablePath: 'C:\\ProgramData\\WindowsControlService\\test\\target-a.exe', matchAttribute: 'FileName', matchValue: 'target-a.exe', productName: 'Harmless Test Target', isEnabled: true, createdAt: ISO(7200) },
        { id: 2, name: 'Test Target B', executablePath: 'C:\\ProgramData\\WindowsControlService\\test\\target-b.exe', matchAttribute: 'InternalName', matchValue: 'target-b', productName: 'Harmless Test Target', isEnabled: true, createdAt: ISO(3600) },
        { id: 3, name: 'Test Target C', executablePath: 'C:\\ProgramData\\WindowsControlService\\test\\target-c.exe', matchAttribute: 'ProductName', matchValue: 'Harmless Test Target', productName: 'Harmless Test Target', isEnabled: true, createdAt: ISO(1800) },
      ]),
    }),
    capture: [
      "'attributes: ' + [...document.querySelectorAll('#application-list .chip')].map((c) => c.textContent).join(' / ')",
    ],
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
    steps: [
      "document.querySelectorAll('#application-list button')[0].click();",
      "document.querySelector('#application-list .button-danger').click(); await window.__wcs.settle();",
    ],
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
    steps: [
      "document.querySelectorAll('#application-list button')[0].click();",
      "document.querySelector('#application-list .button-danger').click(); await window.__wcs.settle();",
    ],
    capture: [
      "'reloaded: ' + window.__wcs.calls.filter((c) => c === 'GET /api/applications').length",
      NOTICES,
    ],
  },
  {
    // The dialog replaced window.confirm. The row itself asks, so the name of what is about to
    // change stays exactly where it already was.
    name: 'applications · a removal asks in the row it belongs to',
    hash: '#/applications',
    responses: withResponses({ 'GET /api/applications': OK(APPLICATIONS) }),
    steps: ["document.querySelectorAll('#application-list button')[0].click(); await window.__wcs.settle();"],
    capture: [
      "document.querySelectorAll('#application-list .row')[0].outerHTML",
      "'asking: ' + document.querySelectorAll('#application-list [data-confirming]').length",
      "'focused: ' + document.activeElement.textContent.trim()",
      "'deleted: ' + window.__wcs.calls.filter((c) => c.startsWith('DELETE')).length",
    ],
  },
  {
    name: 'applications · cancelling a removal puts the row back',
    hash: '#/applications',
    responses: withResponses({ 'GET /api/applications': OK(APPLICATIONS) }),
    steps: [
      "document.querySelectorAll('#application-list button')[0].click();",
      "document.querySelector('#application-list .row-actions:not([hidden]) .button-ghost').click(); await window.__wcs.settle();",
    ],
    capture: [
      "'asking: ' + document.querySelectorAll('#application-list [data-confirming]').length",
      "'rows: ' + document.querySelectorAll('#application-list .row').length",
      "'deleted: ' + window.__wcs.calls.filter((c) => c.startsWith('DELETE')).length",
    ],
  },
  {
    // Two rows asking at once is two questions, and the answer to one of them is ambiguous.
    name: 'applications · opening one confirmation closes the other',
    hash: '#/applications',
    responses: withResponses({ 'GET /api/applications': OK(APPLICATIONS) }),
    steps: [
      "document.querySelectorAll('#application-list button')[0].click();",
      "document.querySelectorAll('#application-list .row')[1].querySelector('button').click(); await window.__wcs.settle();",
    ],
    capture: [
      "'asking: ' + document.querySelectorAll('#application-list [data-confirming]').length",
      "'asking row: ' + [...document.querySelectorAll('#application-list .row')].findIndex((r) => r.hasAttribute('data-confirming'))",
    ],
  },
  {
    name: 'processes · the filter narrows on name and on path',
    hash: '#/applications',
    responses: withResponses({}),
    steps: [
      "document.getElementById('load-processes').click(); await window.__wcs.settle();",
      "document.getElementById('process-search').value = 'other.exe';",
      "document.getElementById('process-search').dispatchEvent(new Event('input')); await window.__wcs.settle();",
    ],
    capture: [
      "'rows: ' + document.querySelectorAll('#process-list .row').length",
      "document.getElementById('process-count').outerHTML",
    ],
  },
  {
    name: 'processes · a search that finds nothing says what it looked for',
    hash: '#/applications',
    responses: withResponses({}),
    steps: [
      "document.getElementById('load-processes').click(); await window.__wcs.settle();",
      "document.getElementById('process-search').value = 'nothing-matches-this';",
      "document.getElementById('process-search').dispatchEvent(new Event('input')); await window.__wcs.settle();",
    ],
    capture: [
      "document.getElementById('process-list').outerHTML",
      "document.getElementById('process-count').outerHTML",
    ],
  },
  {
    name: 'processes · it opens with the caret in the search, and escape closes it',
    hash: '#/applications',
    responses: withResponses({}),
    steps: [
      "document.getElementById('load-processes').click(); await window.__wcs.settle();",
      "window.__wcs.focusedOnOpen = document.activeElement.id;",
      "document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' })); await window.__wcs.settle();",
    ],
    capture: [
      "'focused on open: ' + window.__wcs.focusedOnOpen",
      "'open: ' + !document.getElementById('process-modal').hidden",
      "'focused after escape: ' + document.activeElement.id",
    ],
  },
  {
    // Three ways out, and the scrim used to be the one that dropped the caret on the body.
    name: 'processes · the scrim gives the caret back like escape does',
    hash: '#/applications',
    responses: withResponses({}),
    steps: [
      "document.getElementById('load-processes').click(); await window.__wcs.settle();",
      "document.getElementById('process-modal').click(); await window.__wcs.settle();",
    ],
    capture: [
      "'open: ' + !document.getElementById('process-modal').hidden",
      "'focused after the scrim: ' + document.activeElement.id",
    ],
  },
  {
    // .focus() on a disabled control does nothing at all, and the caret lands on <body>, which
    // is the one place a keyboard user cannot navigate onward from. The opener is still held
    // disabled by withPending while the process list is loading.
    name: 'processes · closing while the opener is still busy does not drop the caret',
    hash: '#/applications',
    responses: withResponses({}),
    steps: [
      "window.__wcs.hold = 'GET /api/processes';",
      "document.getElementById('load-processes').click(); await window.__wcs.settle();",
      "window.__wcs.openerDisabled = document.getElementById('load-processes').disabled;",
      "document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' })); await window.__wcs.settle();",
    ],
    capture: [
      "'opener still disabled: ' + window.__wcs.openerDisabled",
      "'focused: ' + (document.activeElement.id || document.activeElement.tagName)",
    ],
  },
  {
    // aria-modal="true" says the page behind is inert. A synthetic Tab moves nothing on its own,
    // so a focus that lands on the far edge is the trap working and not the browser helping.
    name: 'processes · tab does not walk out from under the scrim',
    hash: '#/applications',
    responses: withResponses({}),
    steps: [
      "document.getElementById('load-processes').click(); await window.__wcs.settle();",
      "window.__wcs.stops = [...document.getElementById('process-modal')"
        + ".querySelectorAll('button:not([disabled]), input:not([disabled])')];",
      "window.__wcs.stops.at(-1).focus(); window.__wcs.lastStop = document.activeElement.id;",
      "document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab' }));",
      "window.__wcs.afterTab = document.activeElement.id;",
      "window.__wcs.stops[0].focus(); window.__wcs.firstStop = document.activeElement.id;",
      "document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', shiftKey: true }));",
    ],
    capture: [
      "'tab from the last stop (' + window.__wcs.lastStop + '): ' + window.__wcs.afterTab",
      "'shift-tab from the first (' + window.__wcs.firstStop + '): ' + document.activeElement.id",
      "'still inside: ' + document.getElementById('process-modal').contains(document.activeElement)",
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
      "'rows: ' + document.querySelectorAll('#history-rows .row').length",
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
      "document.querySelector('#history-origin [data-origin=remote]').click(); await window.__wcs.settle();",
    ],
    capture: [
      "document.getElementById('history-summary').outerHTML",
      "'rows: ' + document.querySelectorAll('#history-rows .row').length",
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
      "'rows: ' + document.querySelectorAll('#history-rows .row').length",
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
        + "[{ id: 99, occurredAt: '" + ISO(1) + "', kind: 'Reconnect', startsSession: true, origin: 'Local', address: null, userName: 'MACHINE\\\\owner', sessionId: 9, durationSeconds: null }] } });",
      "window.__wcs.push('access-history', { total: 24 }); await window.__wcs.settle();",
    ],
    capture: [
      "document.getElementById('history-summary').outerHTML",
      "'rows: ' + document.querySelectorAll('#history-rows .row').length",
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
      "'first row when: ' + document.querySelector('#history-rows .event-ago').textContent",
      "'reloads: ' + window.__wcs.calls.filter((c) => c.includes('access-history')).length",
    ],
  },
  {
    // Four kinds, four labels. The direction is the service's answer, carried as startsSession:
    // derived from the kind it read every Reconnect as a disconnection, and Reconnect is half of
    // what this machine records.
    name: 'history · the four transitions read as four different things',
    hash: '#/history',
    responses: withResponses({
      'GET /api/access-history?limit=10&offset=0': OK({
        total: 4,
        entries: [
          { id: 4, occurredAt: ISO(1), kind: 'Reconnect', startsSession: true, origin: 'Remote', address: '203.0.113.44', userName: 'MACHINE\\owner', sessionId: 2, durationSeconds: null },
          { id: 3, occurredAt: ISO(2), kind: 'Disconnect', startsSession: false, origin: 'Remote', address: '203.0.113.44', userName: 'MACHINE\\owner', sessionId: 2, durationSeconds: 5400 },
          { id: 2, occurredAt: ISO(3), kind: 'Logoff', startsSession: false, origin: 'Local', address: null, userName: 'MACHINE\\owner', sessionId: 1, durationSeconds: 900 },
          { id: 1, occurredAt: ISO(4), kind: 'Logon', startsSession: true, origin: 'Local', address: null, userName: 'MACHINE\\owner', sessionId: 1, durationSeconds: null },
        ],
      }),
    }),
    capture: [
      "'labels: ' + [...document.querySelectorAll('#history-rows .row-title span:first-child')].map((s) => s.textContent).join(' / ')",
      "'directions: ' + [...document.querySelectorAll('#history-rows .event-mark')].map((m) => m.getAttribute('data-direction')).join(' / ')",
    ],
  },
  {
    // Origin has three values, not two, and the third is real: one of the 129 entries stored on
    // a real machine is Unknown. Showing it as "Local" turns "nobody knows" into a claim.
    name: 'history · an origin the service could not determine is not called local',
    hash: '#/history',
    responses: withResponses({
      'GET /api/access-history?limit=10&offset=0': OK({
        total: 2,
        entries: [
          { id: 2, occurredAt: ISO(1), kind: 'Logoff', startsSession: false, origin: 'Unknown', address: null, userName: 'MACHINE\\owner', sessionId: 4, durationSeconds: 300 },
          { id: 1, occurredAt: ISO(2), kind: 'Logon', startsSession: true, origin: 'Local', address: null, userName: 'MACHINE\\owner', sessionId: 4, durationSeconds: null },
        ],
      }),
    }),
    capture: [
      "'origins: ' + [...document.querySelectorAll('#history-rows .pill')].map((p) => p.textContent).join(' / ')",
    ],
  },
  {
    // The service can answer a page empty while reporting a total. A range needs both ends.
    name: 'history · a page answered empty says so instead of naming a range',
    hash: '#/history',
    responses: withResponses({
      'GET /api/access-history?limit=10&offset=0': OK({ total: 30, entries: [] }),
    }),
    capture: [
      "document.getElementById('history-summary').outerHTML",
      "'empty shown: ' + !document.getElementById('history-empty').hidden",
    ],
  },
  {
    // The other half: a pushed total arriving after a first load that failed. The pager has a
    // total and has never had a row, and it must not invent the slice.
    name: 'history · a total pushed over a failed load names no range',
    hash: '#/history',
    responses: withResponses({
      'GET /api/access-history?limit=10&offset=0': PROBLEM(500, 'The log could not be read.'),
    }),
    steps: [
      "window.__wcs.push('access-history', { total: 30 }); await window.__wcs.settle();",
    ],
    capture: [
      "document.getElementById('history-summary').outerHTML",
      "'rows: ' + document.querySelectorAll('#history-rows .row').length",
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
    // The minimum is the service's rule and arrives with the session. The interface only counts.
    name: 'settings · the counter counts against the service minimum',
    hash: '#/settings',
    responses: withResponses({}),
    steps: [
      "document.getElementById('new-password').value = 'short';",
      "document.getElementById('new-password').dispatchEvent(new Event('input')); await window.__wcs.settle();",
    ],
    capture: ["document.getElementById('new-password-count').outerHTML"],
  },
  {
    name: 'settings · the counter stops counting once the minimum is met',
    hash: '#/settings',
    responses: withResponses({}),
    steps: [
      "document.getElementById('new-password').value = 'long-enough-password';",
      "document.getElementById('new-password').dispatchEvent(new Event('input')); await window.__wcs.settle();",
    ],
    capture: ["document.getElementById('new-password-count').outerHTML"],
  },
  {
    name: 'settings · the repeated password says whether it matches',
    hash: '#/settings',
    responses: withResponses({}),
    steps: [
      "document.getElementById('new-password').value = 'long-enough-password';",
      "document.getElementById('confirm-password').value = 'long-enough-passwor';",
      "document.getElementById('confirm-password').dispatchEvent(new Event('input')); await window.__wcs.settle();",
    ],
    capture: ["document.getElementById('confirm-password-match').outerHTML"],
  },
  {
    name: 'settings · and says nothing before there is anything to say',
    hash: '#/settings',
    responses: withResponses({}),
    steps: [
      "document.getElementById('new-password').value = 'long-enough-password';",
      "document.getElementById('new-password').dispatchEvent(new Event('input')); await window.__wcs.settle();",
    ],
    capture: ["document.getElementById('confirm-password-match').outerHTML"],
  },
  {
    name: 'settings · the session card says when the session ends',
    hash: '#/settings',
    responses: withResponses({}),
    capture: ["document.getElementById('session-expiry').outerHTML"],
  },
  {
    name: 'shell · the tab indicators come from the list and the stream',
    hash: '#/applications',
    responses: withResponses({ 'GET /api/applications': OK(APPLICATIONS) }),
    steps: ["window.__wcs.push('usb', { blocked: true, lastModified: null }); await window.__wcs.settle();"],
    capture: [
      "document.getElementById('tab-count-applications').outerHTML",
      "'device signal: ' + !document.getElementById('tab-signal-devices').hidden",
    ],
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
    capture: [GATE, "'notices: ' + document.querySelectorAll('#notices .toast').length", NOTICES],
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

      // A service that is not there does not answer with a status: fetch rejects. That is the
      // one thing a browser can really tell apart from "answered, badly", and it is what the
      // dot in the top bar reports.
      if (entry.offline) {
        throw new TypeError('Failed to fetch');
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
const counts = { markup: 0, check: 0 };

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
    // Two kinds of capture, and they are read differently. A markup capture is expected to
    // change when the presentation changes; a check is a statement about behaviour, and a
    // difference in one is a regression until proved otherwise.
    const kind = /outerHTML|innerHTML/.test(expression) ? 'markup' : 'check';
    counts[kind]++;

    captured.push(`[${kind}] ${String(await evaluate(`return ${expression};`))}`);
  }

  blocks.push(`### ${scenario.name}\n${captured.join('\n')}`);
  process.stdout.write(`${scenario.name}\n`);

  await send('Page.removeScriptToEvaluateOnNewDocument', { identifier: injected.result.identifier }, session);
  await send('Page.navigate', { url: 'about:blank' }, session);
}

writeFileSync(outputPath, `${blocks.join('\n\n')}\n`, 'utf8');
socket.close();
browser.kill();
files?.close();

process.stdout.write(
  `\n${scenarios.length} scenarios, ${counts.check} checks, ${counts.markup} markup -> ${outputPath}\n`);
