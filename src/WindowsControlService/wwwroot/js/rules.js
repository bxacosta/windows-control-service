/**
 * The decisions the interface makes, away from the markup that shows them. Every function here
 * takes values and returns values: no element is read, none is written, and none is imported.
 *
 * That is the whole point. These rules were learned the hard way -- a switch that lies about the
 * registry, a row that disappears while the application it named is still blocked -- and they
 * used to live inside the functions that build the DOM, which is exactly what a redesign
 * replaces. Here they survive it, and can be read without reading any markup.
 */

import { formatAgo, formatDuration, formatTimestamp, formatUptime, formatWhen } from './format.js';

/**
 * Every time this interface shows is relative, because the question being asked of it is "was
 * that just now". The exact one is the value the service recorded, and a recorded value must
 * never be only paraphrased -- so it travels alongside, for the renderer to hang on the title.
 * Beside the relative one it would be two clocks on one line for one fact.
 */
const exactly = (iso) => (iso ? formatTimestamp(iso) : '');

// --- Applications ----------------------------------------------------------

/**
 * Three states, and the third one is the point: Unknown means the service could not ask, not
 * that nothing is blocked. Collapsing it into "not enforced" would tell the administrator the
 * machine is unprotected when the truth is that nobody knows.
 *
 * The answer comes in two halves rather than as one sentence: the state is the claim and the rest
 * is the detail behind it, and the strip sets only the claim in bold. One string would force the
 * renderer to find the boundary again, by splitting on a separator that lives in this file.
 *
 * @param {{state: string, enabledRuleCount: number, lastReconciledAt: string | null} | null} state
 *   null when the service could not be asked at all.
 * @returns {{tone: string, headline: string, detail: string, checked: string, icon: 'ok' | 'alert'}}
 *   `tone` is the styling hook, `headline` and `detail` read on the strip, `checked` sits at its
 *   trailing edge.
 */
export function describePolicyState(state) {
  if (!state) {
    return { tone: 'unknown', headline: 'Policy state unavailable', detail: '', checked: '', icon: 'alert' };
  }

  const rules = `${state.enabledRuleCount} ${state.enabledRuleCount === 1 ? 'rule' : 'rules'}`;

  const described = {
    Enforced: { headline: 'Policy enforced', detail: rules, icon: 'ok' },
    NotEnforced: state.enabledRuleCount > 0
      ? { headline: 'Not enforced', detail: `${rules} waiting`, icon: 'alert' }
      : { headline: 'No policy deployed', detail: '', icon: 'alert' },
    // Never "nothing is blocked": nobody knows, and saying otherwise is a claim about the
    // machine that this interface cannot make.
    Unknown: { headline: 'Policy state unknown', detail: '', icon: 'alert' },
  };

  // A state this version does not know is shown verbatim rather than mapped to one it does know.
  const fallback = { headline: state.state, detail: '', icon: 'alert' };
  const chosen = described[state.state] ?? fallback;

  return {
    tone: state.state.toLowerCase(),
    headline: chosen.headline,
    detail: chosen.detail,
    checked: state.lastReconciledAt === null ? '' : `checked ${formatAgo(state.lastReconciledAt)}`,
    checkedExactly: exactly(state.lastReconciledAt),
    icon: chosen.icon,
  };
}

/** @param {boolean} enabled The value the service accepted, not the one that was asked for. */
export const describeToggle = (name, enabled) =>
  (enabled ? `${name} is blocked again.` : `${name} is no longer blocked.`);

/**
 * A DELETE that fails means the policy was not changed, so the application is still blocked and
 * its row is still true. Hence `reload` in both branches: the list is always re-read from the
 * service and never edited in place, which is what keeps a failure from quietly dropping a row
 * that is still real.
 *
 * @param {string | null} failure The message from the service, or null when it succeeded.
 */
export const describeRemoval = (name, failure) =>
  (failure === null
    ? { tone: 'ok', text: `${name} was removed.`, reload: true }
    : { tone: 'error', text: failure, reload: true });

/** The chip that says which attribute a rule matches on, and against what. */
export const describeMatch = (application) =>
  `${application.matchAttribute} · ${application.matchValue}`;

/** Filters on name and on path: the path is often the only thing that tells two copies apart. */
export function filterProcesses(processes, query) {
  const needle = query.trim().toLowerCase();
  if (needle === '') {
    return processes;
  }

  return processes.filter((process) =>
    process.name.toLowerCase().includes(needle)
    || process.executablePath.toLowerCase().includes(needle));
}

/** @returns {string} Empty while nothing has been loaded, so the footer does not claim "0 of 0". */
export function describeProcessCount(shown, total) {
  if (total === 0) {
    return '';
  }

  return shown === total ? `${total} processes` : `${shown} of ${total} processes`;
}

/** A search that found nothing says what it looked for; an empty list says there was nothing. */
export const describeProcessEmptiness = (query, total) =>
  (total === 0
    ? 'No candidate processes are running.'
    : `No running process matches "${query.trim()}".`);

// --- Devices ---------------------------------------------------------------

/**
 * One line under the title, not two. What the switch does and when it last moved are the same
 * fact seen twice, and splitting them made the row three lines tall for one control.
 *
 * The time is relative, like every other time in this interface: an absolute timestamp forces a
 * subtraction to answer "was that just now", which is the only question being asked here.
 *
 * @param {{blocked: boolean, lastModified: string | null}} status As the service reports it.
 */
export const describeUsbState = (status) => ({
  pill: status.blocked
    ? { tone: 'signal', text: 'Blocked' }
    : { tone: 'muted', text: 'Allowed' },
  detail: [
    status.blocked ? 'New drives will not mount' : 'Drives mount normally',
    status.lastModified ? `changed ${formatAgo(status.lastModified)}` : 'never changed through this service',
  ].join(' · '),
  detailExactly: exactly(status.lastModified),
});

export const describeUsbChange = (blocked) =>
  (blocked ? 'USB mass storage is blocked.' : 'USB mass storage is allowed again.');

/**
 * A value pushed by the service must not yank a control out from under a click that is still in
 * flight: the user asked for one thing, the screen would answer with another, and the request
 * they are waiting for would then land on top of that.
 */
export const acceptsPushedValue = (isBusy) => !isBusy;

// --- History ---------------------------------------------------------------

/**
 * Only the first page follows the stream, and only while its section is on screen. Reloading
 * page four under someone reading it would move the rows they are looking at, and reloading a
 * hidden section spends a request on something nobody can see.
 */
export const followsPushedEvents = (offset, isSectionVisible) => offset === 0 && isSectionVisible;

/**
 * An offset past the end happens after a filter change, or after entries are removed elsewhere.
 * Stepping back is better than an empty table under a pager that says there are three pages.
 *
 * @returns {number | null} The offset to retry with, or null when the page is fine as it is.
 */
export const offsetAfterEmptyPage = (offset, entryCount) =>
  (entryCount === 0 && offset > 0 ? 0 : null);

/**
 * A range needs both of its ends, and `shown` is the one the service has not answered yet. With
 * a total but nothing under it -- a pushed total that arrived before any page was loaded, or a
 * page the service answered empty -- `${offset + 1}–${offset + shown}` reads "1–0 of 30": a
 * range that ends before it begins. Saying how many exist without naming a slice is the honest
 * answer when the slice is not known.
 */
function summarise(offset, total, shown) {
  if (total === 0) {
    return 'Nothing recorded';
  }

  return shown === 0 ? 'Nothing on this page' : `${offset + 1}–${offset + shown} of ${total}`;
}

/** @returns {{page: number, pages: number, summary: string, canGoNewer: boolean, canGoOlder: boolean}} */
export function pagerState(offset, total, pageSize, shown = 0) {
  const pages = Math.max(1, Math.ceil(total / pageSize));
  const page = Math.floor(offset / pageSize) + 1;

  return {
    page,
    pages,
    summary: summarise(offset, total, shown),
    canGoNewer: offset > 0,
    canGoOlder: offset + pageSize < total,
  };
}

/**
 * The page numbers a pager shows: the ends, the current page and its neighbours, and a gap where
 * the rest were left out. `null` is the gap.
 *
 * @returns {Array<number | null>}
 */
export function pageNumbers(page, pages) {
  if (pages <= 7) {
    return Array.from({ length: pages }, (unused, index) => index + 1);
  }

  const shown = new Set([1, pages, page, page - 1, page + 1]);

  // Keep the row the same width wherever the current page is: near an end, spend the freed
  // neighbour on the other side rather than letting the pager shrink.
  if (page <= 3) {
    shown.add(2).add(3).add(4);
  }

  if (page >= pages - 2) {
    shown.add(pages - 1).add(pages - 2).add(pages - 3);
  }

  const numbers = [...shown].filter((number) => number >= 1 && number <= pages).sort((a, b) => a - b);

  return numbers.flatMap((number, index) =>
    (index > 0 && number - numbers[index - 1] > 1 ? [null, number] : [number]));
}

/**
 * The four transitions this machine records, in this interface's words. Flattening them into two
 * would throw away the only distinction the log actually makes here: on a box reached over RDP,
 * "I closed the session" and "the connection dropped" are different events, and Disconnect and
 * Reconnect are the overwhelming majority of what gets recorded.
 */
const eventLabels = Object.freeze({
  Logon: 'Signed in',
  Reconnect: 'Reconnected',
  Disconnect: 'Disconnected',
  Logoff: 'Signed out',
});

/**
 * Three origins, not two. An event whose record carried no address at all is `Unknown`, and
 * showing it as "Local" turns "nobody knows where this came from" into a claim about the
 * machine -- the same mistake `describePolicyState` exists to avoid. Rare and real: one of the
 * 129 entries stored on a real machine is Unknown.
 */
const eventOrigins = Object.freeze({
  Remote: { tone: 'remote', text: 'RDP' },
  Local: { tone: 'muted', text: 'Local' },
  Unknown: { tone: 'muted', text: 'Unknown' },
});

/**
 * One access event as it reads on screen.
 *
 * The direction is the service's answer, carried in the response as `startsSession`, and is not
 * worked out from the kind here. Which event ids open a session is a fact about Windows that the
 * service already owns and already uses to pair each session end with its start. Deriving it
 * again in the browser is a second copy of that rule, and the copy that got written -- `kind ===
 * 'Logon'` -- called every Reconnect a disconnection and pointed the caret the wrong way.
 */
export function describeEvent(entry) {
  return {
    direction: entry.startsSession ? 'in' : 'out',
    // A kind this version does not know is shown verbatim rather than mapped onto one it does
    // know. The direction survives it, because that answer did not come from the kind.
    label: eventLabels[entry.kind] ?? entry.kind,
    // An origin this version does not know is shown verbatim, like the kind above.
    origin: eventOrigins[entry.origin] ?? { tone: 'muted', text: entry.origin },
    // The address is what identifies a remote session; the user name is all a local one has.
    detail: entry.address ?? entry.userName ?? '',
    // Not always relative: an event from this morning reads as a time and one from last week as
    // a date. "19 h ago" makes the reader do the subtraction instead of the interface.
    ago: formatWhen(entry.occurredAt),
    agoExactly: exactly(entry.occurredAt),
    // Only the events that close a session carry a duration, and null is not zero. An empty
    // string rather than a dash: an absent value does not need a placeholder holding its place.
    duration: entry.durationSeconds === null || entry.durationSeconds === undefined
      ? ''
      : formatDuration(entry.durationSeconds),
  };
}

// --- Settings --------------------------------------------------------------

/**
 * What a password field says about itself while it is being typed. Both halves of the rule are
 * the service's and both arrive from it: a copy here would be a hint that goes on being given
 * after the rule behind it changed.
 *
 * Length first, and only then the alphabet: complaining that six characters need a digit while
 * there are three of them is answering a question nobody has reached yet.
 *
 * @param {{minimum: number, requiresLettersAndDigits: boolean}} rule As the service states it.
 */
export function describePasswordNote(value, rule) {
  if (value.length === 0) {
    return { text: '', state: 'neutral', icon: null };
  }

  if (value.length < rule.minimum) {
    return { text: `${value.length}/${rule.minimum}`, state: 'bad', icon: 'alert' };
  }

  if (rule.requiresLettersAndDigits && !(/\p{L}/u.test(value) && /\p{Nd}/u.test(value))) {
    return { text: 'letters and digits', state: 'bad', icon: 'alert' };
  }

  return { text: `${value.length}`, state: 'ok', icon: null };
}

/** Says nothing until there is something to say: an empty field has not failed to match yet. */
export function describePasswordMatch(replacement, confirmation) {
  if (confirmation.length === 0) {
    return { text: '', state: 'neutral', icon: null };
  }

  // Two failures, two icons: falling short of the minimum is a warning about something still
  // being typed, and two passwords that differ is a refusal.
  return replacement === confirmation
    ? { text: 'Match', state: 'ok', icon: null }
    : { text: 'No match', state: 'bad', icon: 'no' };
}

/** The pill beside it already says "Signed in", so this says the one thing it does not. */
export const describeSessionExpiry = (minutes) =>
  (minutes > 0 ? `Ends after ${minutes} minutes of inactivity.` : '');

/**
 * What the card asks of a new password, at the head of the card rather than only inside the
 * field while it is being typed. Says nothing before the service has answered: a rule invented
 * to fill the space would be wrong the moment the real one differed.
 *
 * @param {{minimum: number, requiresLettersAndDigits: boolean}} rule
 */
export function describePasswordRule(rule) {
  if (rule.minimum <= 0) {
    return '';
  }

  return rule.requiresLettersAndDigits
    ? `at least ${rule.minimum} characters, letters and digits`
    : `at least ${rule.minimum} characters`;
}

// --- Service health --------------------------------------------------------

/**
 * What the indicator beside the health dot says, in the bar and on the sign-in screen alike.
 *
 * It reads as a duration and not as a version because the question it answers is "has this been
 * restarted": a version string is the same after a restart and after a week of running, and the
 * one thing an operator looks at this corner for is whether the service is the one they left.
 * The version has not been dropped -- it moves into the title with the exact instant, which is
 * the rule every other time on this interface already follows.
 *
 * The service's own word is printed rather than interpreted. `status` is a constant it cannot
 * vary, and comparing it against one this file chose is how the dot spent months never going
 * green.
 *
 * @param {{status: string, version: string, startedAt: string} | null} health
 *   null before the first answer, or after one that did not arrive.
 * @returns {{text: string, title: string}} `title` is empty when there is nothing exact to hang
 *   on it, so a renderer can remove the attribute rather than set it to a lie.
 */
export function describeServiceHealth(health, now = Date.now()) {
  if (!health) {
    return { text: '', title: '' };
  }

  // The informational version carries the commit after a plus sign. A tooltip is not the place
  // for a forty character hash either.
  const version = String(health.version).split('+')[0];
  const started = exactly(health.startedAt);

  return {
    text: `${health.status} ${formatUptime(health.startedAt, now)}`,
    title: started ? `Started ${started} · version ${version}` : `Version ${version}`,
  };
}
