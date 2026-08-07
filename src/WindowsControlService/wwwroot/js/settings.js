/**
 * Settings: change the password, sign out. Both are session concerns, which is why they live
 * next to the gate rather than with the features.
 */

import * as api from './api.js';
import * as session from './session.js';
import { withPending } from './pending.js';
import { notify } from './notices.js';

const element = (id) => document.getElementById(id);

async function handleChangePassword(submitEvent) {
  submitEvent.preventDefault();
  element('change-password-error').textContent = '';

  const current = element('current-password').value;
  const next = element('new-password').value;
  const confirmation = element('confirm-password').value;

  if (next !== confirmation) {
    element('change-password-error').textContent = 'The two new passwords do not match.';
    return;
  }

  await withPending(element('change-password-submit'), async () => {
    try {
      await api.changePassword(current, next);
    } catch (error) {
      element('change-password-error').textContent =
        error.status === 401 ? 'The current password is not correct.' : error.message;
      return;
    }

    // The security stamp rotated, so this very session is already dead. Saying so and going to
    // the gate is honest; leaving the interface up would show data it can no longer refresh.
    for (const id of ['current-password', 'new-password', 'confirm-password']) {
      element(id).value = '';
    }

    notify('Password changed. Every open session was signed out, including this one.', 'ok');
    session.returnToSignIn();
  });
}

export function connect() {
  element('change-password-form').addEventListener('submit', handleChangePassword);
  element('sign-out').addEventListener('click', (clickEvent) => {
    void session.signOut(clickEvent.currentTarget);
  });
}
