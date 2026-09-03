/**
 * Draws banner.png, the image at the top of the README.
 *
 *   node banner/generate.mjs
 *
 * It renders the real interface -- the same wwwroot the service serves -- against a simulated
 * machine, screenshots the Applications section, and composes the picture around it. Nothing is
 * installed, no policy is applied and the running service is not touched: `scripts/lib/browser.mjs`
 * serves wwwroot from disk and the whole API is answered from inside the page, exactly as the DOM
 * harness does it.
 *
 * The picture is deterministic. The clock is frozen in machine.mjs, so running this twice
 * produces the same bytes and a redraw only shows up in git when the interface really changed.
 */
import { writeFile } from 'node:fs/promises';
import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { setTimeout as delay } from 'node:timers/promises';

import { DEFAULT_BROWSER, openBrowser, serveDirectory } from '../scripts/lib/browser.mjs';
import { NOW, RESPONSES, SNAPSHOT } from './machine.mjs';

const args = Object.fromEntries(
  process.argv.slice(2).map((argument) => {
    const [name, ...rest] = argument.replace(/^--/, '').split('=');
    return [name, rest.join('=')];
  }),
);

const webRoot = fileURLToPath(new URL('../src/WindowsControlService/wwwroot', import.meta.url));
const here = fileURLToPath(new URL('.', import.meta.url));
const browserPath = args.browser ?? DEFAULT_BROWSER;

// 960 is the page's own width: 900 of column plus its 20px of padding on each side. Wider than
// that and every screenshot carries dead margin that has to be cropped back off later.
const SHOT_WIDTH = 960;
// Down to the row an application is added from, so the section is shown whole: the policy strip,
// the rules under it, and the field they arrive through.
const SHOT_HEIGHT = 600;

const BANNER = { width: 1200, height: 630 };

// --- 1. The interface, against a machine that has something to show ------------------------

const bootstrap = `
(() => {
  const table = ${JSON.stringify(RESPONSES)};
  const snapshot = ${JSON.stringify(SNAPSHOT)};
  Date.now = () => ${NOW};

  // The stream would open a connection that is never answered and hold the page busy. What it
  // does answer is the snapshot the service pushes the moment a browser connects, because the
  // tab indicators are painted from that and from nothing else -- without it the Devices tab
  // carries no dot on a machine whose USB storage is blocked.
  class StubEventSource {
    constructor(url) {
      this.url = url;
      this.readyState = 1;
      this.handlers = new Map();
      setTimeout(() => {
        for (const [name, payload] of Object.entries(snapshot)) {
          this.handlers.get(name)?.({ data: JSON.stringify(payload) });
        }
      }, 0);
    }
    addEventListener(name, handler) { this.handlers.set(name, handler); }
    close() { this.readyState = 2; }
  }
  StubEventSource.CONNECTING = 0; StubEventSource.OPEN = 1; StubEventSource.CLOSED = 2;
  window.EventSource = StubEventSource;

  let inFlight = 0;
  window.__banner = {
    calls: [],
    settle: async () => {
      const pause = () => new Promise((resolve) => setTimeout(resolve, 20));
      for (let i = 0; i < 150 && window.__banner.calls.length === 0; i++) { await pause(); }
      let idle = 0;
      for (let i = 0; i < 200 && idle < 3; i++) { await pause(); idle = inFlight === 0 ? idle + 1 : 0; }
    },
  };

  window.fetch = async (path, init) => {
    const key = ((init && init.method) || 'GET') + ' ' + path;
    window.__banner.calls.push(key);
    inFlight++;
    try {
      await new Promise((resolve) => setTimeout(resolve, 0));
      const body = table[key];
      if (!body) { return new Response(JSON.stringify({ title: 'no answer for ' + key }), { status: 599 }); }
      return new Response(JSON.stringify(body), { status: 200, headers: { 'content-type': 'application/json' } });
    } finally { inFlight--; }
  };
})();
`;

/**
 * The last rule is caught mid-removal, asking its question. It is the interface's own state,
 * reached the way anyone reaches it -- by pressing the button -- and it shows what a column of
 * switches cannot: a rule is not permanent, and it asks before it goes.
 *
 * The caret is sent away afterwards. The confirmation puts focus on the dangerous button, which
 * is right for someone using it and a ring around a button in a picture nobody is using.
 */
const askToRemoveTheLastRule = `
  document.querySelector('#application-list .row:last-child [aria-label^="Stop blocking"]').click();
  document.activeElement?.blur();
`;

const files = await serveDirectory(webRoot);
const page = await openBrowser({
  browserPath,
  port: Number(args.port ?? 9403),
  profile: 'C:\\Windows\\Temp\\wcs-banner-profile',
  windowSize: `${SHOT_WIDTH},900`,
  headless: 'new',
});

await page.send('Emulation.setDeviceMetricsOverride', {
  width: SHOT_WIDTH, height: 900, deviceScaleFactor: 2, mobile: false,
});
await page.send('Page.addScriptToEvaluateOnNewDocument', { source: bootstrap });

/** @param {string} hash The section. @param {string} [arrange] Driven once it has loaded. */
const capture = async (hash, arrange) => {
  // Through about:blank: moving between two hashes of one URL is a same-document navigation and
  // fires no load event, so waiting for one after the first shot would wait for ever.
  await page.blank();
  await delay(150);
  await page.navigate(`${files.origin}/${hash}`);
  await page.evaluate('await window.__banner.settle();');

  if (arrange) {
    await page.evaluate(arrange);
  }

  await delay(400);

  const shot = await page.send('Page.captureScreenshot', {
    format: 'png',
    captureBeyondViewport: true,
    clip: { x: 0, y: 0, width: SHOT_WIDTH, height: SHOT_HEIGHT, scale: 2 },
  });

  return `data:image/png;base64,${shot.result.data}`;
};

const applications = await capture('#/applications', askToRemoveTheLastRule);

// --- 2. The composition --------------------------------------------------------------------

const mark = (size) => `<svg viewBox="0 0 32 32" width="${size}" height="${size}" style="display:block">
  <defs>
    <linearGradient id="steel" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="#F6F6F6"/><stop offset="0.55" stop-color="#D2D2D2"/><stop offset="1" stop-color="#9C9C9C"/>
    </linearGradient>
    <mask id="panes" maskUnits="userSpaceOnUse" x="0" y="0" width="32" height="32">
      <path fill="#fff" d="M6 4.4 H26 A1.6 1.6 0 0 1 27.6 6 V16.8 C27.6 23.2 22.8 27.6 16.8 29.6 A2.2 2.2 0 0 1 15.2 29.6 C9.2 27.6 4.4 23.2 4.4 16.8 V6 A1.6 1.6 0 0 1 6 4.4 Z"/>
      <rect x="10.3" y="10.6" width="5.1" height="5.1" rx="0.8" fill="#000"/>
      <rect x="16.6" y="10.6" width="5.1" height="5.1" rx="0.8" fill="#000"/>
      <rect x="10.3" y="16.9" width="5.1" height="5.1" rx="0.8" fill="#000"/>
      <rect x="16.6" y="16.9" width="5.1" height="5.1" rx="0.8" fill="#000"/>
    </mask>
  </defs>
  <rect width="32" height="32" fill="url(#steel)" mask="url(#panes)"/>
  <rect x="10.3" y="10.6" width="5.1" height="5.1" rx="0.8" fill="#4A9EEB"/>
</svg>`;

// The interface's own state colours, and the same three meanings they carry there.
const pill = (label, ink, wash) =>
  `<span style="display:inline-flex;align-items:center;height:26px;padding:0 12px;border-radius:999px;
     background:${wash};color:${ink};font-size:12px;font-weight:600;letter-spacing:0.02em">${label}</span>`;

// One window rather than two. The second one sat behind this one showing almost nothing of
// itself, which read as a shadow with text in it rather than as another section.
const PANEL_WIDTH = 720;
const PANEL_HEIGHT = Math.round(SHOT_HEIGHT * (PANEL_WIDTH / SHOT_WIDTH));

const html = `<!doctype html>
<html><head><meta charset="utf-8"><style>
  * { box-sizing: border-box; }
  body { margin: 0; background: #030303; }

  .banner {
    position: relative;
    width: ${BANNER.width}px; height: ${BANNER.height}px;
    background: #030303; overflow: hidden;
    font-family: "Segoe UI Variable Text", "Segoe UI", system-ui, sans-serif;
    -webkit-font-smoothing: antialiased;
  }

  /* The sign-in screen's own decoration, on the layout's 56px pitch. */
  .lattice {
    position: absolute; inset: 0; pointer-events: none;
    background-image:
      repeating-linear-gradient(0deg, rgba(255,255,255,0.045) 0 1px, transparent 1px 56px),
      repeating-linear-gradient(90deg, rgba(255,255,255,0.045) 0 1px, transparent 1px 56px);
    -webkit-mask-image: radial-gradient(135% 115% at 20% 48%, #000 0%, transparent 82%);
  }
  .lift {
    position: absolute; inset: 0; pointer-events: none;
    background: radial-gradient(70% 60% at 20% 42%, rgba(255,255,255,0.030), transparent 70%);
  }

  .copy {
    position: absolute; left: 56px; top: 0; bottom: 0; width: 404px;
    display: flex; flex-direction: column; justify-content: center; z-index: 2;
  }

  /* Larger than the 14px the wordmark takes inside the product, and deliberately: in the top bar
     the name is the thing nobody needs to read, and here it is the subject. */
  .name {
    font-family: "Segoe UI Variable Display", "Segoe UI Variable Text", "Segoe UI", system-ui, sans-serif;
    font-size: 34px; font-weight: 600; letter-spacing: -0.02em; line-height: 1.15;
    white-space: nowrap;
    color: #EDEDED; margin: 22px 0 0;
  }
  /* One line, and it says the thing the badges below cannot: where the blocking lives. */
  .lead { margin: 16px 0 0; max-width: 396px; color: #A6A6A6; font-size: 15px; line-height: 1.55; }
  .pills { display: flex; flex-wrap: wrap; gap: 8px; margin-top: 26px; }
  .meta {
    margin-top: 26px; color: #858585; font-size: 12.5px;
    font-family: "Cascadia Mono", Consolas, ui-monospace, monospace; letter-spacing: -0.01em;
  }

  /* Set against the right edge and down into the bottom fade, so the window dissolves rather than
     ends. It is not pushed further out than that: the switches and the buttons that answer the
     question in the last row are the subject, and a bleed wide enough to cut them off wastes it. */
  .stage { position: absolute; inset: 0; z-index: 1; }
  .panel {
    position: absolute; left: 478px; top: 112px;
    width: ${PANEL_WIDTH}px; height: ${PANEL_HEIGHT}px;
    border: 1px solid #272727; border-radius: 14px;
    overflow: hidden; background: #0B0B0B;
    box-shadow: 0 34px 80px rgba(0,0,0,0.8), 0 2px 4px rgba(0,0,0,0.5);
  }
  .panel img { display: block; width: 100%; }

  /* The edges fade rather than stop, so the window reads as continuing past the frame. */
  .fade-right {
    position: absolute; top: 0; right: 0; bottom: 0; width: 96px; z-index: 3;
    background: linear-gradient(to right, rgba(3,3,3,0), rgba(3,3,3,0.92) 88%); pointer-events: none;
  }
  .fade-bottom {
    position: absolute; left: 400px; right: 0; bottom: 0; height: 90px; z-index: 3;
    background: linear-gradient(to bottom, rgba(3,3,3,0), rgba(3,3,3,0.92) 88%); pointer-events: none;
  }
</style></head>
<body>
  <div class="banner">
    <div class="lattice"></div>
    <div class="lift"></div>

    <div class="stage">
      <div class="panel"><img src="${applications}" alt=""></div>
    </div>

    <div class="fade-right"></div>
    <div class="fade-bottom"></div>

    <div class="copy">
      ${mark(52)}
      <h1 class="name">Windows Control Service</h1>
      <p class="lead">Blocking that lives in the Windows kernel, and stays in force even when the service is stopped.</p>
      <div class="pills">
        ${pill('Policy enforced', '#3ECF8E', '#15281F')}
        ${pill('USB blocked', '#4A9EEB', '#182632')}
        ${pill('RDP recorded', '#9B87F5', '#23202F')}
      </div>
      <div class="meta">.NET 10 &nbsp;·&nbsp; Windows 11 &nbsp;·&nbsp; localhost:5150</div>
    </div>
  </div>
</body></html>`;

const pagePath = `${here}banner.html`;
await writeFile(pagePath, html, 'utf8');

await page.close();
files.close();

// --- 3. The picture --------------------------------------------------------------------------

// A second, plain browser run rather than another CDP screenshot: --screenshot renders the page
// at exactly the window size, so the banner comes out at its own dimensions with no clip to keep
// in step with them.
const shooter = spawn(browserPath, [
  '--headless=new', '--disable-gpu', '--hide-scrollbars',
  '--force-device-scale-factor=2',
  `--window-size=${BANNER.width},${BANNER.height}`,
  `--screenshot=${here.replace(/\//g, '\\')}banner.png`,
  `file:///${pagePath}`,
], { stdio: 'ignore' });

await new Promise((resolve) => shooter.on('exit', resolve));
await delay(500);

process.stdout.write(`banner.png  ${BANNER.width}x${BANNER.height} at 2x\n`);
