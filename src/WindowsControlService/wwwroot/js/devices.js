/**
 * USB mass storage. One switch, and the only subtle thing about it: the registry is the source
 * of truth, so what the switch shows after an operation is whatever the service reports, never
 * what was asked for.
 */

import * as api from './api.js';
import * as events from './events.js';
import { formatTimestamp } from './format.js';
import { isPending, withPending } from './pending.js';
import { notify, notifyError } from './notices.js';

const element = (id) => document.getElementById(id);

function render(status) {
  const control = element('usb-switch');

  // A pushed update must not yank the switch out from under a click that is still in flight.
  if (!isPending(control)) {
    control.checked = status.blocked;
  }

  element('usb-state-title').textContent = status.blocked
    ? 'Blocked. New drives will not mount.'
    : 'Allowed. Drives mount normally.';

  element('usb-last-modified').textContent = status.lastModified
    ? `Last changed through this service: ${formatTimestamp(status.lastModified)}`
    : 'Never changed through this service.';
}

async function handleToggle(changeEvent) {
  const control = changeEvent.currentTarget;

  // Optimistic: the browser has already moved the switch and the user should see what they
  // asked for. Writing the registry takes long enough that waiting would look like a dead click.
  const wanted = control.checked;

  await withPending(control, async () => {
    try {
      await api.setUsbBlocked(wanted);
    } catch (error) {
      control.checked = !wanted;
      notifyError(error.message);
      return;
    }

    notify(wanted ? 'USB mass storage is blocked.' : 'USB mass storage is allowed again.', 'ok');

    // Read back rather than trust the request: if someone edited the registry by hand a moment
    // ago, this is where the interface finds out.
    try {
      render(await api.getUsb());
    } catch {
      // The write succeeded; a failed read is not worth a second notice.
    }
  });
}

export async function enter() {
  try {
    render(await api.getUsb());
  } catch (error) {
    notifyError(error.message);
  }
}

export function connect() {
  element('usb-switch').addEventListener('change', (changeEvent) => {
    void handleToggle(changeEvent);
  });

  events.on('usb', render);
}
