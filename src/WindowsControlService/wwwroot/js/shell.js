/**
 * The frame around the sections: the top bar, the tab rail, and the two indicators on it. Those
 * indicators are why this is a module rather than a few lines in app.js -- they belong to the
 * shell but their values come from two different sections, and neither section should be
 * reaching into the other's markup to set them.
 */

import { attributes, elementsOf } from './markup.js';

const ui = elementsOf('shell');

/** Signed in or not decides whether there is anything to navigate. */
export function showApplication(visible) {
  ui.topBar.hidden = !visible;
  ui.nav.hidden = !visible;
  ui.main.hidden = !visible;
}

/** The service's own words, printed rather than interpreted. */
export function showHealth(health) {
  // The informational version carries the commit after a plus sign. The top bar is not the
  // place for a forty character hash.
  ui.serviceStatus.textContent = `${health.status} · ${String(health.version).split('+')[0]}`;
}

/**
 * The words beside the dot, when there is no service to quote. Kept in step with the dot rather
 * than only written at boot: leaving "running · 1.0.0" up next to a red dot states a version of
 * something that stopped answering.
 */
export function showUnreachable() {
  ui.serviceStatus.textContent = 'unreachable';
}

/**
 * The dot answers the one question a browser can actually answer about a service: did the last
 * call arrive. It used to compare `status` against a word, and `status` is a constant the
 * service cannot vary -- so the comparison was reading nothing, and it was reading it wrong:
 * the constant is "running", the comparison wanted "healthy", and the dot never went green on
 * a machine where everything worked.
 *
 * Nothing here decides what "healthy" means. Whether the blocking policy is in force and
 * whether USB storage is blocked are the health of this service, and both already have a place
 * on screen that says so in more detail than a dot could.
 */
export function showReachable(reachable) {
  ui.healthDot.setAttribute(attributes.health, reachable ? 'healthy' : 'unreachable');
}

/** The badge counts blocked applications, and disappears rather than showing a zero. */
export function showApplicationCount(count) {
  ui.applicationCount.textContent = String(count);
  ui.applicationCount.hidden = count === 0;
}

/** A dot with a halo, shown only while USB storage is actually blocked. */
export function showDeviceSignal(blocked) {
  ui.deviceSignal.hidden = !blocked;
}

export function connect(onSignOut) {
  ui.signOut.addEventListener('click', (clickEvent) => onSignOut(clickEvent.currentTarget));
}
