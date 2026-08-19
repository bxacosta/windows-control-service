/**
 * USB mass storage. One switch, and the only subtle thing about it: the registry is the source
 * of truth, so what the switch shows after an operation is whatever the service reports, never
 * what was asked for.
 */

import * as api from './api.js';
import * as events from './events.js';
import * as shell from './shell.js';
import { css, elementsOf } from './markup.js';
import { acceptsPushedValue, describeUsbChange, describeUsbState } from './rules.js';
import { isPending, optimistic } from './pending.js';
import { notify, notifyError } from './notices.js';

const ui = elementsOf('devices');

/** The renderer: a pill and one line of text from one status, and no decision of its own. */
function renderUsb(status) {
  const described = describeUsbState(status);

  ui.usbPill.className = css.pill(described.pill.tone);
  ui.usbPill.textContent = described.pill.text;
  ui.usbDetail.textContent = described.detail;
  // The exact time behind the relative one, for the pointer. Empty when there is none to show.
  ui.usbDetail.title = described.detailExactly;
}

/**
 * Showing a status is not the same as painting it, and the difference is the switch: it belongs
 * to whoever is clicking it, so a status that arrived on its own only moves it while nothing is
 * in flight.
 */
function showUsb(status) {
  if (acceptsPushedValue(isPending(ui.usbSwitch))) {
    ui.usbSwitch.checked = status.blocked;
  }

  renderUsb(status);
  shell.showDeviceSignal(status.blocked);
}

async function handleToggle(control) {
  try {
    await optimistic(control, async (wanted) => {
      await api.setUsbBlocked(wanted);
      notify(describeUsbChange(wanted), 'ok');

      // Read back rather than trust the request: if someone edited the registry by hand a
      // moment ago, this is where the interface finds out.
      try {
        showUsb(await api.getUsb());
      } catch {
        // The write succeeded; a failed read is not worth a second notice.
      }
    });
  } catch (error) {
    // The switch is already back where it was.
    notifyError(error.message);
  }
}

export async function enter() {
  try {
    showUsb(await api.getUsb());
  } catch (error) {
    notifyError(error.message);
  }
}

export function connect() {
  ui.usbSwitch.addEventListener('change', (changeEvent) => {
    void handleToggle(changeEvent.currentTarget);
  });

  // The stream sends the current state on connect, so the tab indicator is right from the first
  // paint without this section having been opened, and without a second request to ask.
  events.on('usb', showUsb);
}
