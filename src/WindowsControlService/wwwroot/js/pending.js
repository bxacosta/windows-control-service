/**
 * One mechanism for every action, so that "the control is busy" never has to be re-invented.
 * Blocking USB storage or applying a WDAC policy takes seconds: without this the first click
 * looks like it did not register and the user clicks again.
 */

import { attributes } from './markup.js';

const running = new WeakSet();

/**
 * @param {HTMLButtonElement | HTMLInputElement} control
 * @param {() => Promise<T>} action
 * @returns {Promise<T | undefined>} undefined when the click was ignored as a repeat.
 * @template T
 */
export async function withPending(control, action) {
  if (running.has(control)) {
    // The repeated click. Ignored rather than queued: queueing would apply the same policy
    // twice and the second one would answer 409.
    return undefined;
  }

  running.add(control);
  control.disabled = true;
  control.setAttribute(attributes.busy, 'true');

  try {
    return await action();
  } finally {
    running.delete(control);
    control.disabled = false;
    control.removeAttribute(attributes.busy);
  }
}

export const isPending = (control) => running.has(control);

/**
 * A switch the browser has already moved. The value the user asked for is the one they should
 * see while the service is asked about it, and it goes back if the answer is no -- waiting for
 * the round trip instead would look like a dead click, and leaving it moved after a refusal
 * would be a screen that disagrees with the machine.
 *
 * The failure is re-thrown rather than reported here: what to say about it belongs to the
 * caller, and the only thing this owns is the value.
 *
 * @param {HTMLInputElement} control
 * @param {(wanted: boolean) => Promise<void>} action Receives the value that was asked for.
 */
export async function optimistic(control, action) {
  const wanted = control.checked;

  return withPending(control, async () => {
    try {
      return await action(wanted);
    } catch (error) {
      control.checked = !wanted;
      throw error;
    }
  });
}
