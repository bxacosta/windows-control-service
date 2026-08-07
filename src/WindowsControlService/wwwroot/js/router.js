/**
 * Hash routing, deliberately. Path routing would need MapFallbackToFile on the server, and that
 * answers this page's HTML to a mistyped /api/... path -- turning a clean 404 into a response no
 * client can diagnose.
 */

/** @type {Map<string, {name: string, element: HTMLElement, hooks: {enter?: Function, leave?: Function}}>} */
const routes = new Map();
let active = null;
let started = false;

/**
 * @param {string} name Route name, also the id suffix of its <section>.
 * @param {{enter?: () => void | Promise<void>, leave?: () => void}} hooks
 */
export function register(name, hooks = {}) {
  const element = document.getElementById(`section-${name}`);
  if (!element) {
    throw new Error(`No <section id="section-${name}"> to route to.`);
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
    active.hooks.leave?.();
    active.element.hidden = true;
  }

  const next = routes.get(name);
  next.element.hidden = false;
  active = next;

  for (const link of document.querySelectorAll('[data-nav]')) {
    // aria-current is the only nav state; the styling reserves its border so switching
    // sections does not reflow the row.
    if (link.dataset.nav === name) {
      link.setAttribute('aria-current', 'page');
    } else {
      link.removeAttribute('aria-current');
    }
  }

  await next.hooks.enter?.();
}

export function start() {
  if (started) {
    return;
  }

  started = true;
  window.addEventListener('hashchange', () => { void apply(); });
  void apply();
}

/** The route currently on screen, or null before start(). Used by background refresh: only the
 *  visible section is reloaded. */
export const currentRoute = () => active?.name ?? null;
