/**
 * The gate: first-run password setup, sign in, and the single place that decides whether the
 * application is on screen at all.
 */

import * as api from './api.js';
import * as shell from './shell.js';
import { attributes, elementsOf } from './markup.js';
import { showFieldNote } from './dom.js';
import { describePasswordMatch, describePasswordNote, describeServiceHealth } from './rules.js';
import { withPending } from './pending.js';
import { notify, notifyFailure } from './notices.js';

const ui = elementsOf('gate');

/** @type {() => void} */
let onAuthenticated = () => {};
/** @type {() => void} */
let onSignedOut = () => {};
let lostAlreadyShown = false;

/**
 * Every way out of the application ends here once: signing out, changing the password, and a
 * lost session. What has to stop with the session (the event stream) is registered once, instead
 * of being remembered at each exit.
 */
export function whenSignedOut(handler) {
  onSignedOut = handler;
}

/**
 * The rules the service owns and this interface has to obey while typing. They arrive with the
 * session, which is a call that already happens on every load. Defaults only cover the moment
 * before the first answer: nothing is validated against them until one arrives.
 */
let rules = { minimum: 0, requiresLettersAndDigits: false, sessionTimeoutMinutes: 0 };

export const passwordRule = () => rules;
export const sessionTimeoutMinutes = () => rules.sessionTimeoutMinutes;

/**
 * What the sign-in screen says about the machine it is guarding. The same three functions the
 * top bar has, because it is the same indicator: this screen has no bar to put it on, and the
 * words are decided once, in `rules.js`, so the two cannot drift apart.
 *
 * It comes from GET /api/health, which is public -- it has to be, since there is no session yet.
 * That is also the boundary on what may be said here: the name of the machine and how long its
 * service has been up, never what the machine is configured to block.
 */
export function showMachine(health, now = Date.now()) {
  const described = describeServiceHealth(health, now);

  ui.machineName.textContent = health ? health.machineName : '';
  ui.machineStatus.textContent = described.text;
  ui.machineStatus.title = described.title;
}

/** The name stays -- it is still this machine. Only the claim about the service goes. */
export function showMachineUnreachable() {
  ui.machineStatus.textContent = 'unreachable';
  ui.machineStatus.title = '';
}

export function showMachineReachable(reachable) {
  ui.machineDot.setAttribute(attributes.health, reachable ? 'healthy' : 'unreachable');
}

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

/** Validation while typing, not after submitting. The minimum is the service's rule. */
function renderSetupNotes() {
  showFieldNote(ui.setupCount, describePasswordNote(ui.setupPassword.value, rules));
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
  onSignedOut();
  showGate('login');
  notify('Your session ended. Sign in again.', 'warn');
}

async function handleSetup(submitEvent) {
  submitEvent.preventDefault();
  ui.setupError.textContent = '';

  const password = ui.setupPassword.value;
  const confirmation = ui.setupConfirm.value;

  // There is no password reset: a typo here would only be discovered at the next sign in, and
  // recovering means deleting the database. The confirmation is worth the extra field.
  if (password !== confirmation) {
    ui.setupError.textContent = 'The two passwords do not match.';
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
      ui.setupError.textContent = error.message;
    }
  });
}

async function handleLogin(submitEvent) {
  submitEvent.preventDefault();
  ui.loginError.textContent = '';

  const password = ui.loginPassword.value;

  await withPending(ui.loginSubmit, async () => {
    try {
      await api.login(password);
      ui.loginPassword.value = '';
      showApplication();
    } catch (error) {
      ui.loginError.textContent = error.status === 401 ? 'That password is not correct.' : error.message;
      ui.loginPassword.select();
    }
  });
}

/** Used when the caller has already explained why, so a warning notice would only repeat it. */
export function returnToSignIn() {
  lostAlreadyShown = false;
  onSignedOut();
  showGate('login');
}

export async function signOut(control) {
  await withPending(control, async () => {
    try {
      await api.logout();
    } catch (error) {
      notifyFailure(error);
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
      minimum: session.minimumPasswordLength ?? 0,
      requiresLettersAndDigits: session.requiresLettersAndDigits ?? false,
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
    notifyFailure(error);
    showGate('login');
  }
}
