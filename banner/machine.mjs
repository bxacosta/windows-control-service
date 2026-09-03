/**
 * The machine the banner pretends to be.
 *
 * Nothing here is real and nothing is read from this computer: the banner has to show a machine
 * with a policy in force and a history behind it, and the one this is generated on has neither.
 * The addresses come from the ranges reserved for documentation (RFC 5737), so no screenshot of
 * this project ever publishes someone's address.
 */

/** Frozen, so every run produces the same picture and a redraw is not a diff. */
export const NOW = Date.parse('2026-09-02T14:20:00Z');

const ago = (seconds) => new Date(NOW - seconds * 1000).toISOString();

const HEALTH = {
  status: 'running',
  version: '1.0.0',
  machineName: 'DESKTOP-7K2M1',
  startedAt: ago(4 * 86400 + 6 * 3600 + 12 * 60),
};

/**
 * Applications someone would plausibly block on a machine they are keeping someone else off.
 * They match on both attributes a rule can match on, and two of them are switched off, because
 * those are the two things about a rule worth showing: what it matches, and that keeping a rule
 * is not the same as enforcing it.
 */
const APPLICATIONS = [
  {
    id: 1, name: 'Google Chrome', executablePath: 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe',
    matchAttribute: 'FileName', matchValue: 'chrome.exe',
    productName: 'Google Chrome', isEnabled: true, createdAt: ago(12 * 86400),
  },
  {
    id: 2, name: 'Discord', executablePath: 'C:\\Users\\owner\\AppData\\Local\\Discord\\Discord.exe',
    matchAttribute: 'ProductName', matchValue: 'Discord',
    productName: 'Discord', isEnabled: false, createdAt: ago(9 * 86400),
  },
  {
    id: 3, name: 'Spotify', executablePath: 'C:\\Users\\owner\\AppData\\Roaming\\Spotify\\Spotify.exe',
    matchAttribute: 'FileName', matchValue: 'Spotify.exe',
    productName: 'Spotify', isEnabled: true, createdAt: ago(6 * 86400),
  },
  {
    id: 4, name: 'Steam', executablePath: 'C:\\Program Files (x86)\\Steam\\steam.exe',
    matchAttribute: 'FileName', matchValue: 'steam.exe',
    productName: 'Steam Client', isEnabled: false, createdAt: ago(4 * 86400),
  },
  {
    id: 5, name: 'Notion', executablePath: 'C:\\Users\\owner\\AppData\\Local\\Programs\\Notion\\Notion.exe',
    matchAttribute: 'ProductName', matchValue: 'Notion',
    productName: 'Notion', isEnabled: true, createdAt: ago(2 * 86400),
  },
];

const PROCESSES = [
  { name: 'Google Chrome', executablePath: 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe' },
  { name: 'Microsoft Outlook', executablePath: 'C:\\Program Files\\Microsoft Office\\root\\Office16\\OUTLOOK.EXE' },
  { name: 'Spotify', executablePath: 'C:\\Users\\owner\\AppData\\Roaming\\Spotify\\Spotify.exe' },
];

/**
 * All four kinds, in the proportion a real machine shows them: reconnects and disconnects are
 * the traffic, a sign-in and a sign-out the exception.
 */
const HISTORY = [
  { kind: 'Reconnect', origin: 'Remote', address: '203.0.113.44', at: 1500, duration: null },
  { kind: 'Disconnect', origin: 'Remote', address: '203.0.113.44', at: 8400, duration: 6900 },
  { kind: 'Logon', origin: 'Local', address: null, at: 30600, duration: null },
  { kind: 'Logoff', origin: 'Unknown', address: null, at: 61200, duration: 30600 },
  { kind: 'Reconnect', origin: 'Remote', address: '198.51.100.7', at: 93600, duration: null },
  { kind: 'Disconnect', origin: 'Remote', address: '198.51.100.7', at: 104400, duration: 10800 },
  { kind: 'Logon', origin: 'Local', address: null, at: 176400, duration: null },
  { kind: 'Logoff', origin: 'Unknown', address: null, at: 205200, duration: 28800 },
].map((row, index) => ({
  id: 100 - index,
  occurredAt: ago(row.at),
  kind: row.kind,
  startsSession: row.kind === 'Logon' || row.kind === 'Reconnect',
  origin: row.origin,
  address: row.address,
  userName: 'DESKTOP-7K2M1\\owner',
  sessionId: 2 + (index % 3),
  durationSeconds: row.duration,
}));

const POLICY_STATE = {
  state: 'Enforced',
  enabledRuleCount: APPLICATIONS.filter((application) => application.isEnabled).length,
  lastReconciledAt: ago(95),
};

const USB = { blocked: true, lastModified: ago(3 * 86400) };

/** Keyed exactly as `api.js` asks for them: method, a space, then the path. */
export const RESPONSES = {
  'GET /api/health': HEALTH,
  'GET /api/auth/session': {
    initialized: true,
    authenticated: true,
    minimumPasswordLength: 6,
    requiresLettersAndDigits: true,
    sessionTimeoutMinutes: 10,
  },
  'GET /api/applications': APPLICATIONS,
  'GET /api/applications/policy-state': POLICY_STATE,
  'GET /api/processes': PROCESSES,
  'GET /api/devices/usb': USB,
  'GET /api/access-history?limit=10&offset=0': { total: 47, entries: HISTORY },
};

/**
 * What the service pushes down the event stream the moment a browser connects, keyed by event
 * name. It is not a duplicate of the table above: the tab indicators are painted from these and
 * from nothing else, so without them the Devices tab shows no dot on a machine whose USB storage
 * is blocked -- the section was never opened, and only the stream says so before it is.
 */
export const SNAPSHOT = {
  'policy-state': POLICY_STATE,
  usb: USB,
  'access-history': { total: 47 },
};
