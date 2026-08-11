/**
 * The access timeline. Paged on the server, filtered on the server, and derived on the server:
 * a session's duration and the origin an event inherits come from its neighbours, so nothing
 * here recomputes them.
 */

import * as api from './api.js';
import * as events from './events.js';
import { currentRoute } from './router.js';
import { el, replace } from './dom.js';
import { elementsOf } from './markup.js';
import { followsPushedEvents, offsetAfterEmptyPage, pagerState } from './rules.js';
import { formatDuration, formatTimestamp } from './format.js';
import { withPending } from './pending.js';
import { notifyError } from './notices.js';

const PAGE_SIZE = 10;

const ui = elementsOf('history');

/** The view lives here, not in the DOM, so a pushed update cannot reset the page you are on. */
const view = { offset: 0, origin: 'all', total: 0 };

/**
 * Asked of the router rather than read off a section's `hidden` attribute: how a section is
 * taken off screen is the navigation's business, and this rule must not have an opinion about it.
 */
const isOnScreen = () => currentRoute() === 'history';

// --- Renderers -------------------------------------------------------------

function entryRow(entry) {
  return el('tr', {}, [
    el('td', { text: formatTimestamp(entry.occurredAt) }),
    el('td', { text: entry.kind }),
    el('td', { text: entry.origin }),
    el('td', { text: entry.address ?? '—' }),
    el('td', { text: entry.userName ?? '—' }),
    el('td', { text: String(entry.sessionId) }),
    // Only the events that close a session carry one, and null is not zero.
    el('td', { text: formatDuration(entry.durationSeconds) }),
  ]);
}

function renderPager() {
  const pager = pagerState(view.offset, view.total, PAGE_SIZE);

  ui.summary.textContent = pager.summary;
  ui.previous.disabled = !pager.canGoNewer;
  ui.next.disabled = !pager.canGoOlder;
}

// --- Loading ---------------------------------------------------------------

async function load() {
  let page;
  try {
    page = await api.getAccessHistory({ limit: PAGE_SIZE, offset: view.offset, origin: view.origin });
  } catch (error) {
    notifyError(error.message);
    return;
  }

  view.total = page.total;

  const corrected = offsetAfterEmptyPage(view.offset, page.entries.length);
  if (corrected !== null) {
    view.offset = corrected;
    await load();
    return;
  }

  replace(ui.rows, page.entries.map(entryRow));
  ui.empty.hidden = page.entries.length > 0;
  renderPager();
}

async function move(control, delta) {
  await withPending(control, async () => {
    view.offset = Math.max(0, view.offset + delta * PAGE_SIZE);
    await load();
  });
}

export async function enter() {
  await load();
}

export function connect() {
  ui.origin.addEventListener('change', (changeEvent) => {
    view.origin = changeEvent.currentTarget.value;
    view.offset = 0;
    void load();
  });

  ui.previous.addEventListener('click', (clickEvent) => {
    void move(clickEvent.currentTarget, -1);
  });

  ui.next.addEventListener('click', (clickEvent) => {
    void move(clickEvent.currentTarget, 1);
  });

  events.on('access-history', (payload) => {
    view.total = payload.total;
    renderPager();

    if (followsPushedEvents(view.offset, isOnScreen())) {
      void load();
    }
  });
}
