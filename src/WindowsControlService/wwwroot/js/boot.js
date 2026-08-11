/**
 * The only script index.html loads, and the reason it exists is that a module cannot report its
 * own failure to start. `app.js` imports every section statically, so a module that throws while
 * being evaluated -- which is what elementsOf does when an id in index.html was renamed -- fails
 * before a single line of app.js runs. A try/catch inside app.js cannot see that, and the page
 * is left with the gate hidden, the application hidden, and the explanation only in the console.
 *
 * A dynamic import can be caught, so this is where the error is turned into something on screen.
 * It has no imports of its own on purpose: whatever is broken downstream, this still runs.
 */

import('./app.js').catch((error) => {
  // The console still gets the real thing, stack and all.
  console.error(error);

  const panel = document.createElement('div');
  panel.className = 'boot-error';
  panel.setAttribute('role', 'alert');

  const heading = document.createElement('strong');
  heading.textContent = 'The interface did not start.';

  const detail = document.createElement('p');
  // Reported verbatim: this message names the id or the module, and paraphrasing it would throw
  // away the only thing that says where to look.
  detail.textContent = String(error?.message ?? error);

  panel.append(heading, detail);
  document.body.append(panel);
});
