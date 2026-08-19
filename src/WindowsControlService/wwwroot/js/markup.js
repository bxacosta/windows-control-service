/**
 * The contract between index.html and the modules, in one place. None of it is enforced by
 * anything: renaming an id or a class breaks nothing at build time, it breaks at run time, only
 * in one section, and only once somebody opens it. Gathering it here makes changing the markup
 * one edit per section instead of a search across seven files.
 */

/**
 * Grouped by the module that owns them, not by where they sit on screen: the point is that one
 * module's markup can be rewritten without reading any other.
 */
export const ids = Object.freeze({
  shell: Object.freeze({
    topBar: 'top-bar',
    nav: 'app-nav',
    main: 'main',
    notices: 'notices',
    serviceStatus: 'service-status',
    healthDot: 'health-dot',
    signOut: 'top-sign-out',
    applicationCount: 'tab-count-applications',
    deviceSignal: 'tab-signal-devices',
  }),
  gate: Object.freeze({
    root: 'gate',
    setupForm: 'setup-form',
    setupPassword: 'setup-password',
    setupCount: 'setup-count',
    setupConfirm: 'setup-confirm',
    setupMatch: 'setup-match',
    setupError: 'setup-error',
    setupSubmit: 'setup-submit',
    loginForm: 'login-form',
    loginPassword: 'login-password',
    loginError: 'login-error',
    loginSubmit: 'login-submit',
  }),
  applications: Object.freeze({
    strip: 'policy-strip',
    policyIcon: 'policy-icon',
    policyState: 'policy-state-line',
    policyChecked: 'policy-checked',
    list: 'application-list',
    form: 'add-application-form',
    path: 'application-path',
    name: 'application-name',
    formError: 'add-application-error',
    submit: 'add-application-submit',
    openProcesses: 'load-processes',
  }),
  processes: Object.freeze({
    root: 'process-modal',
    search: 'process-search',
    list: 'process-list',
    count: 'process-count',
    refresh: 'process-refresh',
    close: 'process-close',
  }),
  devices: Object.freeze({
    usbSwitch: 'usb-switch',
    usbPill: 'usb-state-pill',
    usbDetail: 'usb-state-title',
  }),
  history: Object.freeze({
    origin: 'history-origin',
    summary: 'history-summary',
    rows: 'history-rows',
    empty: 'history-empty',
    previous: 'history-previous',
    next: 'history-next',
    pages: 'history-pages',
  }),
  settings: Object.freeze({
    form: 'change-password-form',
    current: 'current-password',
    replacement: 'new-password',
    replacementCount: 'new-password-count',
    confirm: 'confirm-password',
    confirmMatch: 'confirm-password-match',
    error: 'change-password-error',
    submit: 'change-password-submit',
    signOut: 'sign-out',
    passwordRule: 'password-rule',
    sessionPill: 'session-pill',
    sessionExpiry: 'session-expiry',
  }),
});

/**
 * The one id that is computed rather than written down: the router derives it from the route
 * name. Kept here anyway so the rule is visible next to the ids it generates.
 */
export const sectionId = (route) => `section-${route}`;

/** Icons are one inlined sprite, referenced by id. Named here so a rename is one edit. */
export const icons = Object.freeze({
  shield: 'i-shield',
  shieldCheck: 'i-shield-check',
  shieldAlert: 'i-shield-alert',
  trash: 'i-trash',
  refresh: 'i-refresh',
  ok: 'i-check-circle',
  warn: 'i-alert-triangle',
  error: 'i-x-circle',
  close: 'i-x',
  caretIn: 'i-caret-right',
  caretOut: 'i-caret-left',
});

/**
 * What a field's own note shows beside its text. The rules in rules.js name the icon rather than
 * the symbol -- they know nothing about the sprite -- and this is where the two meet.
 */
export const noteIcons = Object.freeze({
  alert: 'i-alert-triangle',
  no: 'i-x',
});

/**
 * Class names the modules write by hand. They are vocabulary defined in app.css, so a rename
 * there has to be answered here -- and nowhere else.
 */
export const css = Object.freeze({
  row: 'row',
  rowMain: 'row-main',
  rowTitle: 'row-title',
  rowDetail: 'row-detail',
  rowMeta: 'row-meta',
  rowActions: 'row-actions',
  rowConfirm: 'row-confirm',
  /** An access event is one line and a time, so its row carries less air than an application's. */
  historyRow: 'row row-compact',
  chip: 'chip',
  empty: 'empty',
  /** The same line, while what goes in its place is still being read. */
  loading: 'empty shimmer',
  primaryButton: 'button button-primary',
  secondaryButton: 'button button-secondary',
  ghostButton: 'button button-ghost',
  dangerButton: 'button button-danger',
  iconButton: 'button button-ghost button-icon',
  /** The word inside a button, so it can shimmer while the button is working. */
  buttonLabel: 'button-label',
  /** The one action in a row that cannot be undone, so hovering it answers in its own colour. */
  removeButton: 'button button-ghost button-icon button-destructive',
  /** The weight below the four: an action that belongs to one row or one dialog. */
  smallSecondaryButton: 'button button-secondary button-small',
  smallGhostButton: 'button button-ghost button-small',
  smallDangerButton: 'button button-danger button-small',
  eventMark: 'icon-caret event-mark',
  eventWhen: 'event-when',
  eventAgo: 'event-ago',
  eventDuration: 'event-duration',
  pagerPage: 'pager-page',
  pagerGap: 'pager-gap',
  toastText: 'toast-text',
  toastDismiss: 'toast-dismiss',
  /** @param {'ok' | 'warn' | 'error'} kind */
  toastOf: (kind) => `toast toast-${kind}`,
  /** @param {'signal' | 'enforced' | 'waiting' | 'denied' | 'remote' | 'muted'} tone */
  pill: (tone) => `pill pill-${tone}`,
});

/**
 * What the browser will stop on when Tab is pressed, in document order. It lives here because it
 * describes the markup rather than any one module.
 *
 * `a[href]` and not a bare `[href]`: every icon in this interface is an `<svg><use href="#id">`,
 * and a bare attribute selector matches those. They are not focusable, so the last "stop" in a
 * dialog came out as a `<use>` element, `.focus()` on it did nothing, and the trap silently let
 * Tab through -- which is how it read on the first run of the scenario written to prove it.
 */
export const focusable = [
  'a[href]',
  'button:not([disabled])',
  'input:not([disabled])',
  'select:not([disabled])',
  'textarea:not([disabled])',
  '[tabindex]:not([tabindex="-1"])',
].join(', ');

/**
 * Attributes the modules write and app.css reads. Each one is a styling hook, which is why they
 * belong to this file rather than to the module that happens to set them.
 */
export const attributes = Object.freeze({
  /** On the policy line. app.css takes the tint and the icon colour from this. */
  policyState: 'data-state',
  /** On the policy strip, so the whole band follows the state rather than the section. */
  tint: 'data-tint',
  /** Set by withPending. app.css sweeps a shimmer across anything carrying it. */
  busy: 'data-busy',
  /** On the navigation links, matched against the route name. */
  navTarget: 'data-nav',
  /** The only navigation state, and what marks the current page in the pager. */
  currentNav: 'aria-current',
  /** On a row that is asking whether to go through with a removal. */
  confirming: 'data-confirming',
  /** Which way an access event points: into the machine, or out of it. */
  direction: 'data-direction',
  /** On the health dot in the top bar. */
  health: 'data-health',
  /** On a field's own note: neutral, satisfied, or not. */
  noteState: 'data-state',
  /** On a segment of the segmented control. */
  origin: 'data-origin',
  /** The pressed state of a segment. */
  pressed: 'aria-pressed',
});

/**
 * Resolves one group in one go, at load, and says which id is missing if any is. Left to
 * itself the first symptom of a renamed id is a TypeError on null somewhere unrelated, long
 * after the edit that caused it and with nothing in it that names the id.
 *
 * @param {keyof typeof ids} group
 * @returns {Readonly<Record<string, HTMLElement>>}
 */
export function elementsOf(group) {
  const wanted = ids[group];
  if (wanted === undefined) {
    throw new Error(`markup.js has no element table called "${group}".`);
  }

  const found = {};
  const missing = [];

  for (const [key, id] of Object.entries(wanted)) {
    const element = document.getElementById(id);
    if (element === null) {
      missing.push(id);
    } else {
      found[key] = element;
    }
  }

  if (missing.length > 0) {
    throw new Error(
      `index.html is missing ${missing.join(', ')}, listed under "${group}" in markup.js.`);
  }

  return Object.freeze(found);
}
