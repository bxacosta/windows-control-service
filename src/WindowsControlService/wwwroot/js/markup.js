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
    nav: 'app-nav',
    main: 'main',
    notices: 'notices',
    serviceStatus: 'service-status',
  }),
  gate: Object.freeze({
    root: 'gate',
    setupForm: 'setup-form',
    setupPassword: 'setup-password',
    setupConfirm: 'setup-confirm',
    setupError: 'setup-error',
    setupSubmit: 'setup-submit',
    loginForm: 'login-form',
    loginPassword: 'login-password',
    loginError: 'login-error',
    loginSubmit: 'login-submit',
  }),
  applications: Object.freeze({
    policyState: 'policy-state-line',
    form: 'add-application-form',
    path: 'application-path',
    name: 'application-name',
    formError: 'add-application-error',
    submit: 'add-application-submit',
    loadProcesses: 'load-processes',
    processList: 'process-list',
    list: 'application-list',
  }),
  devices: Object.freeze({
    usbSwitch: 'usb-switch',
    usbTitle: 'usb-state-title',
    usbLastModified: 'usb-last-modified',
  }),
  history: Object.freeze({
    origin: 'history-origin',
    summary: 'history-summary',
    rows: 'history-rows',
    empty: 'history-empty',
    previous: 'history-previous',
    next: 'history-next',
  }),
  settings: Object.freeze({
    form: 'change-password-form',
    current: 'current-password',
    replacement: 'new-password',
    confirm: 'confirm-password',
    error: 'change-password-error',
    submit: 'change-password-submit',
    signOut: 'sign-out',
  }),
});

/**
 * The one id that is computed rather than written down: the router derives it from the route
 * name. Kept here anyway so the rule is visible next to the ids it generates.
 */
export const sectionId = (route) => `section-${route}`;

/**
 * Class names the modules write by hand. They are vocabulary defined in app.css, so a rename
 * there has to be answered here -- and nowhere else.
 */
export const css = Object.freeze({
  row: 'row',
  rowMain: 'row-main',
  rowTitle: 'row-title',
  rowDetail: 'row-detail',
  rowActions: 'row-actions',
  spinner: 'spinner',
  empty: 'empty',
  quietButton: 'button-quiet',
  /** @param {'ok' | 'warn' | 'error'} kind */
  notice: (kind) => `notice notice-${kind}`,
});

/**
 * Attributes the modules write and app.css reads. Each one is a styling hook, which is why they
 * belong to this file rather than to the module that happens to set them.
 */
export const attributes = Object.freeze({
  /** On the policy line. app.css takes the colour of its left border from this. */
  policyState: 'data-state',
  /** Set by withPending. app.css shows the spinner inside anything carrying it. */
  busy: 'data-busy',
  /** On the navigation links, matched against the route name. */
  navTarget: 'data-nav',
  /** The only navigation state. Its border is reserved so switching sections does not reflow. */
  currentNav: 'aria-current',
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
