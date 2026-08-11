/**
 * The decisions the interface makes, away from the markup that shows them. Every function here
 * takes values and returns values: no element is read, none is written, and none is imported.
 *
 * That is the whole point. These rules were learned the hard way -- a switch that lies about the
 * registry, a row that disappears while the application it named is still blocked -- and they
 * used to live inside the functions that build the DOM, which is exactly what a redesign
 * replaces. Here they survive it, and can be read without reading any markup.
 */

import { formatAgo, formatTimestamp } from './format.js';

// --- Applications ----------------------------------------------------------

/**
 * Three states, and the third one is the point: Unknown means the service could not ask, not
 * that nothing is blocked. Collapsing it into "not enforced" would tell the administrator the
 * machine is unprotected when the truth is that nobody knows.
 *
 * @param {{state: string, enabledRuleCount: number, lastReconciledAt: string | null} | null} state
 *   null when the service could not be asked at all.
 * @returns {{tone: string, text: string}} `tone` is the styling hook, `text` is what is shown.
 */
export function describePolicyState(state) {
  if (!state) {
    return { tone: 'unknown', text: 'Unavailable.' };
  }

  const described = {
    Enforced: `Enforced · ${state.enabledRuleCount} rule(s) · checked ${formatAgo(state.lastReconciledAt)}`,
    NotEnforced: state.enabledRuleCount > 0
      ? `Not enforced, with ${state.enabledRuleCount} rule(s) waiting. The next check re-applies the policy.`
      : 'No policy deployed. Nothing is blocked.',
    Unknown: 'Unknown — the service could not ask Windows. This is not the same as "nothing is blocked".',
  };

  // A state this version does not know is shown verbatim rather than mapped to one it does know.
  return { tone: state.state.toLowerCase(), text: described[state.state] ?? state.state };
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

// --- Devices ---------------------------------------------------------------

/** @param {{blocked: boolean, lastModified: string | null}} status As the service reports it. */
export const describeUsbState = (status) => ({
  title: status.blocked
    ? 'Blocked. New drives will not mount.'
    : 'Allowed. Drives mount normally.',
  lastChanged: status.lastModified
    ? `Last changed through this service: ${formatTimestamp(status.lastModified)}`
    : 'Never changed through this service.',
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

/** @returns {{page: number, pages: number, summary: string, canGoNewer: boolean, canGoOlder: boolean}} */
export function pagerState(offset, total, pageSize) {
  const pages = Math.max(1, Math.ceil(total / pageSize));
  const page = Math.floor(offset / pageSize) + 1;

  return {
    page,
    pages,
    summary: total === 0 ? 'No events recorded yet.' : `${total} event(s) · page ${page} of ${pages}`,
    canGoNewer: offset > 0,
    canGoOlder: offset + pageSize < total,
  };
}
