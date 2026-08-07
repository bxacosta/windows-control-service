/**
 * One mechanism for every action, so that "the control is busy" never has to be re-invented.
 * Blocking USB storage or applying a WDAC policy takes seconds: without this the first click
 * looks like it did not register and the user clicks again.
 */

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
  control.dataset.busy = 'true';

  try {
    return await action();
  } finally {
    running.delete(control);
    control.disabled = false;
    delete control.dataset.busy;
  }
}

export const isPending = (control) => running.has(control);
