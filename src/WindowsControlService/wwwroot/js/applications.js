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
import * as shell from './shell.js';
import { el, icon, replace, setIcon } from './dom.js';
import { attributes, css, elementsOf, focusable, icons } from './markup.js';
import {
  describeMatch,
  describePolicyState,
  describeProcessCount,
  describeProcessEmptiness,
  describeRemoval,
  describeToggle,
  filterProcesses,
} from './rules.js';
import { optimistic, withPending } from './pending.js';
import { notify, notifyError } from './notices.js';

const ui = elementsOf('applications');
const picker = elementsOf('processes');

let loaded = false;
let processes = [];

/** Opening one confirmation closes any other: two rows asking at once is two questions. */
let confirming = null;

// --- Renderers: given a value, paint it ------------------------------------

function renderPolicyState(state) {
  const described = describePolicyState(state);

  // The one strip whose tint is a claim about the machine rather than a section's identity:
  // green while the policy is in force, amber the rest of the time, including "nobody knows".
  ui.strip.setAttribute(attributes.tint, described.tone === 'enforced' ? 'enforced' : 'waiting');
  setIcon(ui.policyIcon, described.icon === 'ok' ? icons.shieldCheck : icons.shieldAlert);

  ui.policyState.setAttribute(attributes.policyState, described.tone);
  ui.policyState.textContent = described.text;
  ui.policyChecked.textContent = described.checked;
}

function applicationRow(application, onChanged) {
  const toggle = el('input', {
    type: 'checkbox',
    class: 'switch',
    role: 'switch',
    'aria-label': `Blocking enabled for ${application.name}`,
  });
  toggle.checked = application.isEnabled;

  toggle.addEventListener('change', (changeEvent) => {
    void setEnabled(application, changeEvent.currentTarget, onChanged);
  });

  const remove = el('button', {
    type: 'button',
    class: css.iconButton,
    'aria-label': `Stop blocking ${application.name}`,
  }, [icon(icons.trash)]);

  const cancel = el('button', { type: 'button', class: css.ghostButton }, ['Cancel']);
  const confirm = el('button', { type: 'button', class: css.dangerButton }, [
    el('span', { class: css.spinner, 'aria-hidden': 'true' }),
    'Remove',
  ]);

  // Which attribute is doing the blocking, not a fixed label: a binary with no
  // OriginalFilename is matched by InternalName or ProductName, and saying otherwise would
  // be the same guess that made the block silently do nothing.
  const detail = el('div', { class: css.rowDetail, text: application.executablePath });
  const chip = el('span', { class: css.chip, text: describeMatch(application) });
  const question = el('div', { class: css.rowConfirm, text: 'Stop blocking this application?', hidden: true });

  const actions = el('div', { class: css.rowActions }, [toggle, remove]);
  const confirmActions = el('div', { class: css.rowActions, hidden: true }, [cancel, confirm]);

  const row = el('div', { class: css.row }, [
    el('div', { class: css.rowMain }, [
      el('div', { class: css.rowTitle }, [el('span', { text: application.name }), chip]),
      detail,
      question,
    ]),
    actions,
    confirmActions,
  ]);

  const ask = (asking) => {
    row.toggleAttribute(attributes.confirming, asking);
    detail.hidden = asking;
    question.hidden = !asking;
    actions.hidden = asking;
    confirmActions.hidden = !asking;
  };

  remove.addEventListener('click', () => {
    confirming?.();
    confirming = () => { ask(false); confirming = null; };
    ask(true);
    // The question is the dangerous button, so that is where the keyboard lands.
    confirm.focus();
  });

  cancel.addEventListener('click', () => {
    confirming = null;
    ask(false);
    remove.focus();
  });

  confirm.addEventListener('click', (clickEvent) => {
    confirming = null;
    void removeApplication(application, clickEvent.currentTarget, onChanged);
  });

  return row;
}

function processRow(process) {
  const pick = el('button', { type: 'button', class: css.secondaryButton }, ['Use']);

  pick.addEventListener('click', () => {
    // The only two ways to name an executable: this list, or typing the path. A browser
    // never reveals the real path of a file chosen with a file picker.
    ui.path.value = process.executablePath;
    ui.name.value = process.name;
    // Not back to the button that opened the dialog: this is the one exit with somewhere better
    // to send the caret, which is the field the value just landed in.
    pickerOpener = ui.path;
    closePicker();
  });

  return el('div', { class: css.row }, [
    el('div', { class: css.rowMain }, [
      el('div', { class: css.rowTitle }, [el('span', { text: process.name })]),
      el('div', { class: css.rowDetail, text: process.executablePath }),
    ]),
    el('div', { class: css.rowActions }, [pick]),
  ]);
}

function renderProcesses() {
  const shown = filterProcesses(processes, picker.search.value);

  picker.count.textContent = describeProcessCount(shown.length, processes.length);

  replace(picker.list, shown.length === 0
    ? [el('p', { class: css.empty, text: describeProcessEmptiness(picker.search.value, processes.length) })]
    : shown.map(processRow));
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

/**
 * What had focus when the dialog opened. Held here so that every way out restores it, rather
 * than each exit remembering to -- which is how the scrim came to be the one that did not.
 */
let pickerOpener = null;

function closePicker() {
  picker.root.hidden = true;
  document.removeEventListener('keydown', handlePickerKey);

  // A disabled control refuses focus without saying so, and the caret lands on <body> -- the one
  // place a keyboard user cannot navigate onward from. withPending can still be holding the
  // opener disabled when the dialog closes, so ask before handing it back.
  const target = pickerOpener?.disabled ? ui.path : pickerOpener;
  target?.focus();
  pickerOpener = null;
}

/**
 * `aria-modal="true"` says the rest of the page is inert, and Tab walking out to the controls
 * behind the scrim makes that a false claim: they are still reachable, still operable, and
 * invisible under the veil. Wrapping at both edges is what makes the attribute true.
 */
function keepFocusInside(keyEvent) {
  const stops = [...picker.root.querySelectorAll(focusable)];
  if (stops.length === 0) {
    return;
  }

  const leaving = keyEvent.shiftKey ? stops[0] : stops.at(-1);
  if (document.activeElement !== leaving && picker.root.contains(document.activeElement)) {
    return;
  }

  keyEvent.preventDefault();
  (keyEvent.shiftKey ? stops.at(-1) : stops[0]).focus();
}

function handlePickerKey(keyEvent) {
  if (keyEvent.key === 'Escape') {
    closePicker();
    return;
  }

  if (keyEvent.key === 'Tab') {
    keepFocusInside(keyEvent);
  }
}

async function openPicker(control) {
  pickerOpener = control;
  picker.root.hidden = false;
  document.addEventListener('keydown', handlePickerKey);
  // The search is the point of the dialog, so it is where the caret goes.
  picker.search.focus();

  await loadProcesses(control);
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

  confirming = null;
  shell.showApplicationCount(applications.length);

  if (applications.length === 0) {
    replace(ui.list, [el('p', { class: css.empty, text: 'No applications are blocked.' })]);
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
    try {
      processes = await api.getProcesses();
    } catch (error) {
      notifyError(error.message);
      return;
    }

    renderProcesses();
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
  ui.openProcesses.addEventListener('click', (clickEvent) => {
    void openPicker(clickEvent.currentTarget);
  });

  picker.close.addEventListener('click', closePicker);

  picker.refresh.addEventListener('click', (clickEvent) => {
    void loadProcesses(clickEvent.currentTarget);
  });

  picker.search.addEventListener('input', renderProcesses);

  // Clicking the scrim, but not the dialog on it, is the other way out of a modal.
  picker.root.addEventListener('click', (clickEvent) => {
    if (clickEvent.target === picker.root) {
      closePicker();
    }
  });

  // Nothing here rebuilds the form, which is what keeps a half-typed path alive across a
  // background refresh. Mirroring the fields into module state as well would be two copies of
  // the same truth and no extra protection.

  // Pushed, not polled: the reconciliation worker can put a removed policy back while nobody is
  // touching the browser, and thirty seconds of showing the old answer is thirty seconds of
  // showing something false.
  events.on('policy-state', renderPolicyState);
}
