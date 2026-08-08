/**
 * The push side. One EventSource for the whole application, because this is plain HTTP: that
 * means HTTP/1.1 and six connections per origin, and one stream per section would spend them.
 */

import { reportSessionLost } from './api.js';

/** @type {Map<string, (payload: unknown) => void>} */
const handlers = new Map();
let source = null;
let wanted = false;

/** Registered before start(), and re-attached every time the stream is reopened. */
export function on(name, handler) {
  handlers.set(name, handler);
}

function open() {
  if (source) {
    return;
  }

  source = new EventSource('/api/events');

  for (const [name, handler] of handlers) {
    source.addEventListener(name, (message) => handler(JSON.parse(message.data)));
  }

  source.onerror = () => {
    // CONNECTING means the browser is already retrying on its own, which is the normal end of a
    // stream that reached its lifetime. CLOSED means it has given up, and the only thing that
    // makes it give up is a response that is not a stream: a 401. Measured, not assumed.
    if (source && source.readyState === EventSource.CLOSED) {
      stop();
      reportSessionLost();
    }
  };
}

export function stop() {
  wanted = false;
  source?.close();
  source = null;
}

export function start() {
  wanted = true;
  open();
}

// Registered once, at module load, and not inside start(): signing in again would otherwise add
// a second listener, and a third. It reopens only while a stream is wanted, so coming back to a
// tab that is sitting on the sign-in screen does not open /api/events just to be told 401.
//
// A hidden tab holds one of the browser's six connections for nothing. Six of those and an
// ordinary request never completes, with no error anywhere.
document.addEventListener('visibilitychange', () => {
  if (document.hidden) {
    source?.close();
    source = null;
  } else if (wanted) {
    open();
  }
});
