/**
 * Entry point. Order matters: the session decides whether anything else is on screen, so the
 * router only starts once there is something to route to.
 */

import * as api from './api.js';
import * as router from './router.js';
import * as session from './session.js';
import * as settings from './settings.js';
import { notifyError } from './notices.js';

for (const name of ['applications', 'devices', 'history', 'settings']) {
  router.register(name);
}

settings.connect();

// Every 401 in the application ends here, and so does an event stream that died with one.
api.whenSessionLost(session.onSessionLost);

/**
 * GET /api/health is public and always answers, so it doubles as the proof that this page is
 * talking to a running service rather than being a tab left open after it was stopped.
 */
async function showServiceStatus() {
  const slot = document.getElementById('service-status');
  try {
    const health = await api.getHealth();
    // The informational version carries the commit after a plus sign. The footer is not the
    // place for a forty character hash.
    const version = String(health.version).split('+')[0];
    slot.textContent = `${health.status} · version ${version}`;
  } catch (error) {
    slot.textContent = 'service unreachable';
    notifyError(error.message);
  }
}

void session.bootstrap(() => router.start());
void showServiceStatus();
