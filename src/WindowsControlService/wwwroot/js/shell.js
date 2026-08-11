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

export function showHealth(health) {
  // The informational version carries the commit after a plus sign. The top bar is not the
  // place for a forty character hash.
  ui.serviceStatus.textContent = `${health.status} · ${String(health.version).split('+')[0]}`;
  ui.healthDot.setAttribute(attributes.health, health.status === 'healthy' ? 'healthy' : 'unknown');
}

export function showUnreachable() {
  ui.serviceStatus.textContent = 'unreachable';
  ui.healthDot.setAttribute(attributes.health, 'unreachable');
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
