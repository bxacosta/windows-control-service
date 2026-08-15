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

// Every call, not one at boot: the indicator stays current for the whole session instead of
// reporting whatever was true when the page loaded. Both halves of it follow this one signal,
// because a version printed next to a red dot is a claim about a service that is not there.
//
// This is also what puts the first values on screen -- the first call the page makes is the
// first transition -- which is why nothing below asks for the health separately.
//
// Coming back asks the service again rather than restoring the words from memory: what was true
// before the gap is not evidence about now, and a service that was restarted in the meantime can
// answer with a different version than the one this page loaded with.
api.whenReachabilityChanges((reachable) => {
  shell.showReachable(reachable);
  if (reachable) {
    void showServiceStatus();
  } else {
    shell.showUnreachable();
  }
});

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
    // The words are not written here on failure. "Unreachable" belongs to a call that never
    // arrived, and that case is already handled above; a health call that arrived and failed
    // says nothing about reachability, and printing the word anyway would put it next to a
    // green dot -- the two halves contradicting each other, which is the whole point of
    // having them follow one signal.
    notifyError(error.message);
  }
}

void session.bootstrap(() => {
  router.start();
  events.start();
});
