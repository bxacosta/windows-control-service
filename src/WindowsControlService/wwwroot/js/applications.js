/**
 * Applications: what Windows refuses to run, and whether the policy that says so is actually in
 * force. The list comes from the database; the policy state comes from CiTool and can change
 * without anyone asking, which is why it arrives over the event stream.
 *
 * The decisions this section makes are in rules.js and the markup it depends on is in markup.js.
 * What is left here is the wiring between them: handlers that sequence the calls, and renderers
 * that paint a value and decide nothing.
 */

import * as api from './api.js';
import * as events from './events.js';
import { el, replace } from './dom.js';
import { attributes, css, elementsOf } from './markup.js';
import { describePolicyState, describeRemoval, describeToggle } from './rules.js';
import { optimistic, withPending } from './pending.js';
import { notify, notifyError } from './notices.js';

const ui = elementsOf('applications');

let loaded = false;

// --- Renderers: given a value, paint it ------------------------------------

function renderPolicyState(state) {
  const described = describePolicyState(state);

  ui.policyState.setAttribute(attributes.policyState, described.tone);
  ui.policyState.textContent = described.text;
}

function applicationRow(application, onChanged) {
  const toggle = el('input', {
    type: 'checkbox',
    role: 'switch',
    'aria-label': `Blocking enabled for ${application.name}`,
  });
  toggle.checked = application.isEnabled;

  toggle.addEventListener('change', (changeEvent) => {
    void setEnabled(application, changeEvent.currentTarget, onChanged);
  });

  const remove = el('button', { type: 'button', class: css.quietButton }, [
    el('span', { class: css.spinner, 'aria-hidden': 'true' }),
    `Remove`,
  ]);

  remove.addEventListener('click', (clickEvent) => {
    void removeApplication(application, clickEvent.currentTarget, onChanged);
  });

  return el('div', { class: css.row }, [
    el('div', { class: css.rowMain }, [
      el('div', { class: css.rowTitle, text: application.name }),
      el('div', { class: css.rowDetail, text: application.executablePath }),
      // Which attribute is doing the blocking, not a fixed label: a binary with no
      // OriginalFilename is matched by InternalName or ProductName, and saying otherwise would
      // be the same guess that made the block silently do nothing.
      el('div', { class: css.rowDetail, text: `${application.matchAttribute}: ${application.matchValue}` }),
    ]),
    el('div', { class: css.rowActions }, [toggle, remove]),
  ]);
}

function processRow(process) {
  const pick = el('button', { type: 'button', class: css.quietButton }, [
    el('span', { class: css.spinner, 'aria-hidden': 'true' }),
    'Use',
  ]);

  pick.addEventListener('click', () => {
    // The only two ways to name an executable: this list, or typing the path. A browser
    // never reveals the real path of a file chosen with a file picker.
    ui.path.value = process.executablePath;
    ui.name.value = process.name;
    ui.path.focus();
  });

  return el('div', { class: css.row }, [
    el('div', { class: css.rowMain }, [
      el('div', { class: css.rowTitle, text: process.name }),
      el('div', { class: css.rowDetail, text: process.executablePath }),
    ]),
    el('div', { class: css.rowActions }, [pick]),
  ]);
}

// --- Handlers: what happens, and in what order -----------------------------

async function setEnabled(application, control, onChanged) {
  try {
    await optimistic(control, async (wanted) => {
      await api.setApplicationEnabled(application.id, wanted);
      notify(describeToggle(application.name, wanted), 'ok');
      await onChanged();
    });
  } catch (error) {
    // The switch has already gone back to where it was; this only says why.
    notifyError(error.message);
  }
}

async function removeApplication(application, control, onChanged) {
  if (!window.confirm(`Stop blocking ${application.name}?`)) {
    return;
  }

  await withPending(control, async () => {
    let failure = null;
    try {
      await api.deleteApplication(application.id);
    } catch (error) {
      failure = error.message;
    }

    const outcome = describeRemoval(application.name, failure);
    notify(outcome.text, outcome.tone);

    if (outcome.reload) {
      await onChanged();
    }
  });
}

async function handleAdd(submitEvent) {
  submitEvent.preventDefault();
  ui.formError.textContent = '';

  await withPending(ui.submit, async () => {
    const path = ui.path.value.trim();
    const name = ui.name.value.trim();

    try {
      await api.blockApplication(path, name || undefined);
    } catch (error) {
      ui.formError.textContent = error.message;
      return;
    }

    ui.path.value = '';
    ui.name.value = '';
    notify('Windows will refuse to run it from now on.', 'ok');

    await Promise.all([loadList(), loadPolicyState()]);
  });
}

// --- Loading ---------------------------------------------------------------

async function loadList() {
  let applications;
  try {
    applications = await api.getApplications();
  } catch (error) {
    notifyError(error.message);
    return;
  }

  if (applications.length === 0) {
    replace(ui.list, [el('p', { class: css.empty, text: 'Nothing is blocked yet.' })]);
    return;
  }

  replace(ui.list, applications.map((application) => applicationRow(application, loadList)));
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
    let processes;
    try {
      processes = await api.getProcesses();
    } catch (error) {
      notifyError(error.message);
      return;
    }

    if (processes.length === 0) {
      replace(ui.processList, [el('p', { class: css.empty, text: 'No candidate processes are running.' })]);
      return;
    }

    replace(ui.processList, processes.map(processRow));
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
  ui.form.addEventListener('submit', handleAdd);
  ui.loadProcesses.addEventListener('click', (clickEvent) => {
    void loadProcesses(clickEvent.currentTarget);
  });

  // Nothing here rebuilds the form, which is what keeps a half-typed path alive across a
  // background refresh. Mirroring the fields into module state as well would be two copies of
  // the same truth and no extra protection.

  // Pushed, not polled: the reconciliation worker can put a removed policy back while nobody is
  // touching the browser, and thirty seconds of showing the old answer is thirty seconds of
  // showing something false.
  events.on('policy-state', renderPolicyState);
}
