/**
 * Settings: change the password, sign out. Both are session concerns, which is why they live
 * next to the gate rather than with the features.
 */

import * as api from './api.js';
import * as events from './events.js';
import * as session from './session.js';
import { attributes, elementsOf } from './markup.js';
import { describePasswordLength, describePasswordMatch, describeSessionExpiry } from './rules.js';
import { withPending } from './pending.js';
import { notify } from './notices.js';

const ui = elementsOf('settings');

/** Validation while typing, so the rules a field has to satisfy are visible before it is sent. */
function renderFieldNotes() {
  const length = describePasswordLength(ui.replacement.value, session.minimumPasswordLength());
  ui.replacementCount.textContent = length.text;
  ui.replacementCount.setAttribute(attributes.noteState, length.state);

  const match = describePasswordMatch(ui.replacement.value, ui.confirm.value);
  ui.confirmMatch.textContent = match.text;
  ui.confirmMatch.setAttribute(attributes.noteState, match.state);
}

export function renderSession() {
  ui.sessionExpiry.textContent = describeSessionExpiry(session.sessionTimeoutMinutes());
}

async function handleChangePassword(submitEvent) {
  submitEvent.preventDefault();
  ui.error.textContent = '';

  const current = ui.current.value;
  const replacement = ui.replacement.value;
  const confirmation = ui.confirm.value;

  if (replacement !== confirmation) {
    ui.error.textContent = 'The two new passwords do not match.';
    return;
  }

  await withPending(ui.submit, async () => {
    try {
      await api.changePassword(current, replacement);
    } catch (error) {
      ui.error.textContent =
        error.status === 401 ? 'The current password is not correct.' : error.message;
      return;
    }

    // The security stamp rotated, so this very session is already dead. Saying so and going to
    // the gate is honest; leaving the interface up would show data it can no longer refresh.
    for (const field of [ui.current, ui.replacement, ui.confirm]) {
      field.value = '';
    }

    renderFieldNotes();
    notify('Password changed. Every open session was signed out, including this one.', 'ok');
    events.stop();
    session.returnToSignIn();
  });
}

export function connect() {
  ui.form.addEventListener('submit', handleChangePassword);

  for (const field of [ui.replacement, ui.confirm]) {
    field.addEventListener('input', renderFieldNotes);
  }

  ui.signOut.addEventListener('click', (clickEvent) => {
    events.stop();
    void session.signOut(clickEvent.currentTarget);
  });
}
