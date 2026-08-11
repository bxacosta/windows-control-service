/**
 * Notices go into a fixed container that is never part of the layout flow. Anything that
 * appears above the content would move the control the user was about to click.
 */

import { css, ids } from './markup.js';

const DEFAULT_TIMEOUT_MS = 6000;

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

  const element = document.createElement('div');
  element.className = css.notice(kind);
  element.textContent = message;
  host.append(element);

  const dismiss = () => element.remove();
  if (timeoutMs > 0) {
    window.setTimeout(dismiss, timeoutMs);
  }

  return dismiss;
}

export const notifyError = (message) => notify(message, 'error');
