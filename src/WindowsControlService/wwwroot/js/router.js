/**
 * Hash routing, deliberately. Path routing would need MapFallbackToFile on the server, and that
 * answers this page's HTML to a mistyped /api/... path -- turning a clean 404 into a response no
 * client can diagnose.
 */

import { attributes, sectionId } from './markup.js';

/** @type {Map<string, {name: string, element: HTMLElement, hooks: {enter?: Function}}>} */
const routes = new Map();
let active = null;
let started = false;

/**
 * @param {string} name Route name, also the id suffix of its <section>.
 * @param {{enter?: () => void | Promise<void>}} hooks
 */
export function register(name, hooks = {}) {
  const element = document.getElementById(sectionId(name));
  if (!element) {
    throw new Error(`No <section id="${sectionId(name)}"> to route to.`);
  }

  routes.set(name, { name, element, hooks });
}

function requestedName() {
  const raw = window.location.hash.replace(/^#\/?/, '').trim();
  return routes.has(raw) ? raw : routes.keys().next().value;
}

async function apply() {
  const name = requestedName();
  if (active?.name === name) {
    return;
  }

  if (active) {
    active.element.hidden = true;
  }

  const next = routes.get(name);
  next.element.hidden = false;
  active = next;

  for (const link of document.querySelectorAll(`[${attributes.navTarget}]`)) {
    // aria-current is the only nav state; the styling reserves its border so switching
    // sections does not reflow the row.
    if (link.getAttribute(attributes.navTarget) === name) {
      link.setAttribute(attributes.currentNav, 'page');
    } else {
      link.removeAttribute(attributes.currentNav);
    }
  }

  await next.hooks.enter?.();
}

export function start() {
  if (started) {
    // Signed in again: the section on screen was painted, or failed to paint, for the session that
    // ended, and the route has not changed, so nothing else would load it.
    void active?.hooks.enter?.();
    return;
  }

  started = true;
  window.addEventListener('hashchange', () => { void apply(); });
  void apply();
}

/** The route currently on screen, or null before start(). Used by background refresh: only the
 *  visible section is reloaded. */
export const currentRoute = () => active?.name ?? null;
