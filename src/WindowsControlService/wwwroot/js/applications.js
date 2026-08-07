/**
 * Applications: what Windows refuses to run, and whether the policy that says so is actually in
 * force. The list comes from the database; the policy state comes from CiTool and can change
 * without anyone asking, which is why it arrives over the event stream.
 */

import * as api from './api.js';
import * as events from './events.js';
import { el, replace } from './dom.js';
import { formatAgo } from './format.js';
import { withPending } from './pending.js';
import { notify, notifyError } from './notices.js';

const element = (id) => document.getElementById(id);

/** What was typed lives here, not only in the DOM, or a background refresh would wipe it. */
const draft = { path: '', name: '' };
let loaded = false;

function renderPolicyState(state) {
  const line = element('policy-state-line');
  if (!state) {
    line.textContent = 'Unavailable.';
    line.dataset.state = 'unknown';
    return;
  }

  line.dataset.state = state.state.toLowerCase();

  // Three states, and the third one is the point: Unknown means the service could not ask, not
  // that nothing is blocked. Collapsing it into "not enforced" would tell the user the machine
  // is unprotected when the truth is that nobody knows.
  const description = {
    Enforced: `Enforced · ${state.enabledRuleCount} rule(s) · checked ${formatAgo(state.lastReconciledAt)}`,
    NotEnforced: state.enabledRuleCount > 0
      ? `Not enforced, with ${state.enabledRuleCount} rule(s) waiting. The next check re-applies the policy.`
      : 'No policy deployed. Nothing is blocked.',
    Unknown: 'Unknown — the service could not ask Windows. This is not the same as "nothing is blocked".',
  };

  line.textContent = description[state.state] ?? state.state;
}

function applicationRow(application, onChanged) {
  const toggle = el('input', {
    type: 'checkbox',
    role: 'switch',
    'aria-label': `Blocking enabled for ${application.name}`,
  });
  toggle.checked = application.isEnabled;

  toggle.addEventListener('change', (changeEvent) => {
    const control = changeEvent.currentTarget;
    // Optimistic: the browser already moved the switch, and the value the user asked for is the
    // one they should see. If the service refuses, it goes back.
    const wanted = control.checked;

    void withPending(control, async () => {
      try {
        await api.setApplicationEnabled(application.id, wanted);
        notify(wanted ? `${application.name} is blocked again.` : `${application.name} is no longer blocked.`, 'ok');
        await onChanged();
      } catch (error) {
        control.checked = !wanted;
        notifyError(error.message);
      }
    });
  });

  const remove = el('button', { type: 'button', class: 'button-quiet' }, [
    el('span', { class: 'spinner', 'aria-hidden': 'true' }),
    `Remove`,
  ]);

  remove.addEventListener('click', (clickEvent) => {
    if (!window.confirm(`Stop blocking ${application.name}?`)) {
      return;
    }

    void withPending(clickEvent.currentTarget, async () => {
      try {
        await api.deleteApplication(application.id);
        notify(`${application.name} was removed.`, 'ok');
      } catch (error) {
        // A failure here means the policy was not changed, so the entry is still real and still
        // blocking. Dropping the row would be a lie.
        notifyError(error.message);
      }

      await onChanged();
    });
  });

  return el('div', { class: 'row' }, [
    el('div', { class: 'row-main' }, [
      el('div', { class: 'row-title', text: application.name }),
      el('div', { class: 'row-detail', text: application.executablePath }),
      // The field WDAC actually matches. Shown because it explains why renaming the file does
      // not get around the block.
      el('div', { class: 'row-detail', text: `OriginalFilename: ${application.originalFileName}` }),
    ]),
    el('div', { class: 'row-actions' }, [toggle, remove]),
  ]);
}

async function loadList() {
  const list = element('application-list');

  let applications;
  try {
    applications = await api.getApplications();
  } catch (error) {
    notifyError(error.message);
    return;
  }

  if (applications.length === 0) {
    replace(list, [el('p', { class: 'empty', text: 'Nothing is blocked yet.' })]);
    return;
  }

  replace(list, applications.map((application) => applicationRow(application, loadList)));
}

async function loadPolicyState() {
  try {
    renderPolicyState(await api.getPolicyState());
  } catch (error) {
    renderPolicyState(null);
    notifyError(error.message);
  }
}

async function loadProcesses(control) {
  await withPending(control, async () => {
    const container = element('process-list');
    let processes;
    try {
      processes = await api.getProcesses();
    } catch (error) {
      notifyError(error.message);
      return;
    }

    if (processes.length === 0) {
      replace(container, [el('p', { class: 'empty', text: 'No candidate processes are running.' })]);
      return;
    }

    replace(container, processes.map((process) => {
      const pick = el('button', { type: 'button', class: 'button-quiet' }, [
        el('span', { class: 'spinner', 'aria-hidden': 'true' }),
        'Use',
      ]);

      pick.addEventListener('click', () => {
        // The only two ways to name an executable: this list, or typing the path. A browser
        // never reveals the real path of a file chosen with a file picker.
        draft.path = process.executablePath;
        draft.name = process.name;
        element('application-path').value = draft.path;
        element('application-name').value = draft.name;
        element('application-path').focus();
      });

      return el('div', { class: 'row' }, [
        el('div', { class: 'row-main' }, [
          el('div', { class: 'row-title', text: process.name }),
          el('div', { class: 'row-detail', text: process.executablePath }),
        ]),
        el('div', { class: 'row-actions' }, [pick]),
      ]);
    }));
  });
}

async function handleAdd(submitEvent) {
  submitEvent.preventDefault();
  element('add-application-error').textContent = '';

  await withPending(element('add-application-submit'), async () => {
    const path = element('application-path').value.trim();
    const name = element('application-name').value.trim();

    try {
      await api.blockApplication(path, name || undefined);
    } catch (error) {
      element('add-application-error').textContent = error.message;
      return;
    }

    draft.path = '';
    draft.name = '';
    element('application-path').value = '';
    element('application-name').value = '';
    notify('Windows will refuse to run it from now on.', 'ok');

    await Promise.all([loadList(), loadPolicyState()]);
  });
}

/** Called by the router when the section becomes visible. */
export async function enter() {
  if (!loaded) {
    loaded = true;
    await Promise.all([loadList(), loadPolicyState()]);
    return;
  }

  await loadList();
}

export function connect() {
  element('add-application-form').addEventListener('submit', handleAdd);
  element('load-processes').addEventListener('click', (clickEvent) => {
    void loadProcesses(clickEvent.currentTarget);
  });

  for (const [id, field] of [['application-path', 'path'], ['application-name', 'name']]) {
    element(id).addEventListener('input', (inputEvent) => {
      draft[field] = inputEvent.currentTarget.value;
    });
  }

  // Pushed, not polled: the reconciliation worker can put a removed policy back while nobody is
  // touching the browser, and thirty seconds of showing the old answer is thirty seconds of
  // showing something false.
  events.on('policy-state', renderPolicyState);
}
