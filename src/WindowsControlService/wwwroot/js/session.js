/**
 * The gate: first-run password setup, sign in, and the single place that decides whether the
 * application is on screen at all.
 */

import * as api from './api.js';
import * as shell from './shell.js';
import { elementsOf } from './markup.js';
import { showFieldNote } from './dom.js';
import { describePasswordLength, describePasswordMatch } from './rules.js';
import { withPending } from './pending.js';
import { notify, notifyError } from './notices.js';

const ui = elementsOf('gate');

/** @type {() => void} */
let onAuthenticated = () => {};
let lostAlreadyShown = false;

/**
 * The two rules the service owns and this interface has to obey while typing. They arrive with
 * the session, which is a call that already happens on every load. Defaults only cover the
 * moment before the first answer: nothing is validated against them until one arrives.
 */
let rules = { minimumPasswordLength: 0, sessionTimeoutMinutes: 0 };

export const minimumPasswordLength = () => rules.minimumPasswordLength;
export const sessionTimeoutMinutes = () => rules.sessionTimeoutMinutes;

function showGate(which) {
  ui.root.hidden = false;
  shell.showApplication(false);
  ui.setupForm.hidden = which !== 'setup';
  ui.loginForm.hidden = which !== 'login';

  const field = which === 'setup' ? ui.setupPassword : ui.loginPassword;
  field.focus();
}

function showApplication() {
  ui.root.hidden = true;
  shell.showApplication(true);
  lostAlreadyShown = false;
  onAuthenticated();
}

/** Field errors live in a slot that is always in the layout, so showing one moves nothing. */
function setFieldError(slot, message) {
  slot.textContent = message ?? '';
}

/** Validation while typing, not after submitting. The minimum is the service's rule. */
function renderSetupNotes() {
  showFieldNote(ui.setupCount, describePasswordLength(ui.setupPassword.value, rules.minimumPasswordLength));
  showFieldNote(ui.setupMatch, describePasswordMatch(ui.setupPassword.value, ui.setupConfirm.value));
}

/**
 * Called from api.js on any 401, and from the event stream when it dies with one. It fires once
 * per lost session: two calls in the same instant must not stack two notices.
 */
export function onSessionLost() {
  if (lostAlreadyShown) {
    return;
  }

  lostAlreadyShown = true;
  showGate('login');
  notify('Your session ended. Sign in again.', 'warn');
}

async function handleSetup(submitEvent) {
  submitEvent.preventDefault();
  setFieldError(ui.setupError, '');

  const password = ui.setupPassword.value;
  const confirmation = ui.setupConfirm.value;

  // There is no password reset: a typo here would only be discovered at the next sign in, and
  // recovering means deleting the database. The confirmation is worth the extra field.
  if (password !== confirmation) {
    setFieldError(ui.setupError, 'The two passwords do not match.');
    return;
  }

  await withPending(ui.setupSubmit, async () => {
    try {
      await api.configurePassword(password);
      await api.login(password);
      notify('Password set. The service is now protected.', 'ok');
      showApplication();
    } catch (error) {
      // The minimum length is the service's rule, so its own message is the one shown.
      setFieldError(ui.setupError, error.message);
    }
  });
}

async function handleLogin(submitEvent) {
  submitEvent.preventDefault();
  setFieldError(ui.loginError, '');

  const password = ui.loginPassword.value;

  await withPending(ui.loginSubmit, async () => {
    try {
      await api.login(password);
      ui.loginPassword.value = '';
      showApplication();
    } catch (error) {
      setFieldError(ui.loginError, error.status === 401 ? 'That password is not correct.' : error.message);
      ui.loginPassword.select();
    }
  });
}

/** Used when the caller has already explained why, so a warning notice would only repeat it. */
export function returnToSignIn() {
  lostAlreadyShown = false;
  showGate('login');
}

export async function signOut(control) {
  await withPending(control, async () => {
    try {
      await api.logout();
    } catch (error) {
      notifyError(error.message);
      return;
    }

    returnToSignIn();
  });
}

/**
 * Decides what the first paint shows. One request, not two: GET /api/auth/session answers both
 * "is this machine configured" and "is this caller signed in", and carries the two rules the
 * interface validates against.
 */
export async function bootstrap(authenticatedHandler) {
  onAuthenticated = authenticatedHandler;

  ui.setupForm.addEventListener('submit', handleSetup);
  ui.loginForm.addEventListener('submit', handleLogin);

  for (const field of [ui.setupPassword, ui.setupConfirm]) {
    field.addEventListener('input', renderSetupNotes);
  }

  try {
    const session = await api.getSession();
    rules = {
      minimumPasswordLength: session.minimumPasswordLength ?? 0,
      sessionTimeoutMinutes: session.sessionTimeoutMinutes ?? 0,
    };

    if (!session.initialized) {
      showGate('setup');
    } else if (!session.authenticated) {
      showGate('login');
    } else {
      showApplication();
    }
  } catch (error) {
    notifyError(error.message);
    showGate('login');
  }
}
