/**
 * Entry point. Order matters: the session decides whether anything else is on screen, so the
 * router only starts once there is something to route to.
 */

import * as api from './api.js';
import * as applications from './applications.js';
import * as devices from './devices.js';
import * as events from './events.js';
import * as history from './history.js';
import * as router from './router.js';
import * as session from './session.js';
import * as settings from './settings.js';
import * as shell from './shell.js';
import { notifyError } from './notices.js';

router.register('applications', { enter: applications.enter });
router.register('devices', { enter: devices.enter });
router.register('history', { enter: history.enter });
router.register('settings', { enter: settings.renderSession });

applications.connect();
devices.connect();
history.connect();
settings.connect();

// Two ways out, one path: the top bar and the Settings card both end here.
shell.connect((control) => {
  events.stop();
  void session.signOut(control);
});

// Every 401 in the application ends here, and so does an event stream that died with one. The
// stream is torn down first: reconnecting it while signed out would only earn another 401.
api.whenSessionLost(() => {
  events.stop();
  session.onSessionLost();
});

// Every call, not just the one below: the dot in the top bar stays current for the whole
// session instead of reporting whatever was true when the page loaded.
api.whenReachabilityChanges(shell.showReachable);

/**
 * GET /api/health is public and always answers, so it doubles as the proof that this page is
 * talking to a running service rather than being a tab left open after it was stopped. This
 * writes the words; the dot beside them is set from every call, in shell.showReachable. They
 * agree because the only way a public endpoint that always returns 200 fails is by not arriving.
 */
async function showServiceStatus() {
  try {
    shell.showHealth(await api.getHealth());
  } catch (error) {
    shell.showUnreachable();
    notifyError(error.message);
  }
}

void session.bootstrap(() => {
  router.start();
  events.start();
});
void showServiceStatus();
