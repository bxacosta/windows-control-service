/**
 * Entry point. Stage 1 wires the shell: routing between the four sections and the footer
 * status. Each section fills its own placeholder in a later stage.
 */

import * as router from './router.js';
import { notifyError } from './notices.js';

for (const name of ['applications', 'devices', 'history', 'settings']) {
  router.register(name);
}

router.start();

/**
 * GET /api/health is the one endpoint that is public and always answers, so it doubles as the
 * proof that the interface is talking to the service that served it, not to a stale tab left
 * open while the service was stopped.
 */
async function showServiceStatus() {
  const slot = document.getElementById('service-status');
  try {
    const response = await fetch('/api/health', { headers: { Accept: 'application/json' } });
    if (!response.ok) {
      throw new Error(`health answered ${response.status}`);
    }

    const health = await response.json();
    slot.textContent = `${health.status} · version ${health.version}`;
  } catch {
    // Not fatal: the shell is usable, so this states the fact instead of blocking on it.
    slot.textContent = 'service unreachable';
    notifyError('The service did not answer. It may be stopped.');
  }
}

void showServiceStatus();
