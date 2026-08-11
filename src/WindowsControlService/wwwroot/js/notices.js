/**
 * Toasts stack at the bottom right and never occupy layout: a notice that pushes the page down
 * moves the control the user was about to click.
 */

import { el, icon } from './dom.js';
import { css, icons, ids } from './markup.js';

const DEFAULT_TIMEOUT_MS = 4500;

const container = () => document.getElementById(ids.shell.notices);

/**
 * @param {string} message
 * @param {'ok' | 'warn' | 'error'} kind
 * @param {number} timeoutMs Pass 0 to keep it until it is dismissed by hand.
 * @returns {() => void} Dismisses the notice.
 */
export function notify(message, kind = 'ok', timeoutMs = DEFAULT_TIMEOUT_MS) {
  const host = container();
  if (!host) {
    return () => {};
  }

  const dismiss = el('button', { type: 'button', class: css.toastDismiss, 'aria-label': 'Dismiss' }, [
    icon(icons.close),
  ]);

  const toast = el('div', { class: css.toastOf(kind) }, [
    icon(icons[kind] ?? icons.ok, 'toast-icon'),
    el('div', { class: css.toastText, text: message }),
    dismiss,
  ]);

  host.append(toast);

  const remove = () => toast.remove();
  dismiss.addEventListener('click', remove);

  if (timeoutMs > 0) {
    window.setTimeout(remove, timeoutMs);
  }

  return remove;
}

export const notifyError = (message) => notify(message, 'error');
