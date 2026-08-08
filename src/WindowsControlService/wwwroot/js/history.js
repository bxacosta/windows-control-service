/**
 * The access timeline. Paged on the server, filtered on the server, and derived on the server:
 * a session's duration and the origin an event inherits come from its neighbours, so nothing
 * here recomputes them.
 */

import * as api from './api.js';
import * as events from './events.js';
import { el, replace } from './dom.js';
import { formatDuration, formatTimestamp } from './format.js';
import { withPending } from './pending.js';
import { notifyError } from './notices.js';

const PAGE_SIZE = 10;

const element = (id) => document.getElementById(id);

/** The view lives here, not in the DOM, so a pushed update cannot reset the page you are on. */
const view = { offset: 0, origin: 'all', total: 0 };

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
  const pages = Math.max(1, Math.ceil(view.total / PAGE_SIZE));
  const page = Math.floor(view.offset / PAGE_SIZE) + 1;

  element('history-summary').textContent = view.total === 0
    ? 'No events recorded yet.'
    : `${view.total} event(s) · page ${page} of ${pages}`;

  element('history-previous').disabled = view.offset === 0;
  element('history-next').disabled = view.offset + PAGE_SIZE >= view.total;
}

async function load() {
  let page;
  try {
    page = await api.getAccessHistory({ limit: PAGE_SIZE, offset: view.offset, origin: view.origin });
  } catch (error) {
    notifyError(error.message);
    return;
  }

  view.total = page.total;

  // An offset past the end can happen after a filter change; step back rather than show nothing.
  if (page.entries.length === 0 && view.offset > 0) {
    view.offset = 0;
    await load();
    return;
  }

  replace(element('history-rows'), page.entries.map(entryRow));
  element('history-empty').hidden = page.entries.length > 0;
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
  element('history-origin').addEventListener('change', (changeEvent) => {
    view.origin = changeEvent.currentTarget.value;
    view.offset = 0;
    void load();
  });

  element('history-previous').addEventListener('click', (clickEvent) => {
    void move(clickEvent.currentTarget, -1);
  });

  element('history-next').addEventListener('click', (clickEvent) => {
    void move(clickEvent.currentTarget, 1);
  });

  events.on('access-history', (payload) => {
    view.total = payload.total;
    renderPager();

    // Only the first page follows new events. Reloading page four under someone who is reading
    // it would move the rows they are looking at.
    if (view.offset === 0 && !element('section-history').hidden) {
      void load();
    }
  });
}
