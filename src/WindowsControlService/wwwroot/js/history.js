/**
 * The access timeline. Paged on the server, filtered on the server, and derived on the server:
 * a session's duration and the origin an event inherits come from its neighbours, so nothing
 * here recomputes them.
 */

import * as api from './api.js';
import * as events from './events.js';
import { currentRoute } from './router.js';
import { el, replace } from './dom.js';
import { attributes, css, elementsOf } from './markup.js';
import { describeEvent, followsPushedEvents, offsetAfterEmptyPage, pageNumbers, pagerState } from './rules.js';
import { withPending } from './pending.js';
import { notifyError } from './notices.js';

const PAGE_SIZE = 10;

const ui = elementsOf('history');

/** The view lives here, not in the DOM, so a pushed update cannot reset the page you are on. */
const view = { offset: 0, origin: 'all', total: 0, shown: 0 };

/**
 * Asked of the router rather than read off a section's `hidden` attribute: how a section is
 * taken off screen is the navigation's business, and this rule must not have an opinion about it.
 */
const isOnScreen = () => currentRoute() === 'history';

// --- Renderers -------------------------------------------------------------

function entryRow(entry) {
  const described = describeEvent(entry);

  return el('div', { class: css.row }, [
    el('span', { class: css.eventMark, [attributes.direction]: described.direction, 'aria-hidden': 'true' }),
    el('div', { class: css.rowMain }, [
      el('div', { class: css.rowTitle }, [
        el('span', { text: described.label }),
        el('span', { class: css.pill(described.origin.tone), text: described.origin.text }),
      ]),
      el('div', { class: css.rowDetail, text: described.detail }),
    ]),
    el('div', { class: css.eventWhen }, [
      el('div', { class: css.eventAgo, text: described.ago }),
      // Only the events that close a session carry one, and null is not zero.
      el('div', { class: css.eventDuration, text: described.duration }),
    ]),
  ]);
}

function pageButton(number, current) {
  const button = el('button', {
    type: 'button',
    class: css.pagerPage,
    text: String(number),
  });

  if (number === current) {
    button.setAttribute(attributes.currentNav, 'page');
  }

  button.addEventListener('click', (clickEvent) => {
    void goTo(clickEvent.currentTarget, (number - 1) * PAGE_SIZE);
  });

  return button;
}

function renderPager() {
  const pager = pagerState(view.offset, view.total, PAGE_SIZE, view.shown);

  ui.summary.textContent = pager.summary;
  ui.previous.disabled = !pager.canGoNewer;
  ui.next.disabled = !pager.canGoOlder;

  replace(ui.pages, pageNumbers(pager.page, pager.pages).map((number) =>
    // null is the gap where the pages nobody asked for were left out.
    (number === null
      ? el('span', { class: css.pagerGap, text: '…', 'aria-hidden': 'true' })
      : pageButton(number, pager.page))));
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

  view.shown = page.entries.length;

  replace(ui.rows, page.entries.map(entryRow));
  ui.empty.hidden = page.entries.length > 0;
  renderPager();
}

async function goTo(control, offset) {
  await withPending(control, async () => {
    view.offset = Math.max(0, offset);
    await load();
  });
}

export async function enter() {
  await load();
}

export function connect() {
  ui.origin.addEventListener('click', (clickEvent) => {
    const segment = clickEvent.target.closest(`[${attributes.origin}]`);
    if (segment === null) {
      return;
    }

    for (const other of ui.origin.children) {
      other.setAttribute(attributes.pressed, String(other === segment));
    }

    view.origin = segment.getAttribute(attributes.origin);
    view.offset = 0;
    void load();
  });

  ui.previous.addEventListener('click', (clickEvent) => {
    void goTo(clickEvent.currentTarget, view.offset - PAGE_SIZE);
  });

  ui.next.addEventListener('click', (clickEvent) => {
    void goTo(clickEvent.currentTarget, view.offset + PAGE_SIZE);
  });

  events.on('access-history', (payload) => {
    view.total = payload.total;

    if (followsPushedEvents(view.offset, isOnScreen())) {
      // load() repaints the pager itself, once it knows what is on the page.
      void load();
      return;
    }

    // A total is not a page. Repainting a pager that has never had rows under it is what used to
    // put "1–0 of 30" on screen; where there are rows, the new total is the one thing that did
    // change and the pager has to follow it.
    if (view.shown > 0) {
      renderPager();
    }
  });
}
