/**
 * Serving `wwwroot` from disk and driving a headless browser over CDP, in one place.
 *
 * Both things that render this interface without a service behind it need exactly this: the DOM
 * harness, which captures markup and behaviour, and the banner generator, which captures pixels.
 * They had a copy each of the same ninety lines, which is one copy too many for a file that
 * decides how the interface gets rendered at all.
 *
 * Nothing here knows what a scenario or a screenshot is. What to load, what to stub and what to
 * take away belongs to the caller.
 */
import { spawn } from 'node:child_process';
import { readFile } from 'node:fs/promises';
import { createServer } from 'node:http';
import { extname, join, normalize, sep } from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';

export const DEFAULT_BROWSER = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';

const CONTENT_TYPES = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.mjs': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.svg': 'image/svg+xml',
  '.json': 'application/json; charset=utf-8',
};

/**
 * Serves one directory over http on a free loopback port. The page has to come over http rather
 * than from a file:// URL because ES modules will not load from one.
 *
 * @returns {Promise<{origin: string, close: () => void}>}
 */
export async function serveDirectory(root) {
  const webRoot = normalize(root);

  const files = createServer(async (request, response) => {
    const path = new URL(request.url, 'http://x').pathname;
    // Normalised and re-checked against the root: a served directory is a served directory.
    const target = normalize(join(webRoot, path === '/' ? 'index.html' : path));

    if (!target.startsWith(webRoot + sep)) {
      response.writeHead(403).end();
      return;
    }

    try {
      const body = await readFile(target);
      response.writeHead(200, { 'content-type': CONTENT_TYPES[extname(target)] ?? 'application/octet-stream' });
      response.end(body);
    } catch {
      response.writeHead(404).end();
    }
  });

  await new Promise((resolve) => files.listen(0, '127.0.0.1', resolve));

  return {
    origin: `http://127.0.0.1:${files.address().port}`,
    close: () => files.close(),
  };
}

/**
 * Starts a headless browser, attaches to one page, and hands back the four things a caller
 * needs. `evaluate` wraps its expression in an async function, so `await` works inside it and a
 * `return` is what comes back.
 *
 * @returns {Promise<{send: Function, evaluate: Function, navigate: Function, close: Function}>}
 */
export async function openBrowser({
  browserPath = DEFAULT_BROWSER,
  port = 9333,
  profile = 'C:\\Windows\\Temp\\wcs-browser-profile',
  windowSize = '1200,1400',
  headless = 'old',
} = {}) {
  const browser = spawn(browserPath, [
    `--headless=${headless}`,
    '--disable-gpu',
    '--no-sandbox',
    '--no-first-run',
    '--hide-scrollbars',
    `--remote-debugging-port=${port}`,
    `--user-data-dir=${profile}`,
    `--window-size=${windowSize}`,
    'about:blank',
  ], { stdio: 'ignore' });

  let socketUrl = null;
  for (let attempt = 0; attempt < 60 && !socketUrl; attempt++) {
    await delay(500);
    try {
      socketUrl = (await (await fetch(`http://127.0.0.1:${port}/json/version`)).json()).webSocketDebuggerUrl;
    } catch {
      // Not listening yet.
    }
  }

  if (!socketUrl) {
    browser.kill();
    throw new Error('The browser never opened its debugging port.');
  }

  const socket = new WebSocket(socketUrl);
  await new Promise((resolve, reject) => {
    socket.addEventListener('open', resolve, { once: true });
    socket.addEventListener('error', reject, { once: true });
  });

  let nextId = 0;
  const waiting = new Map();
  let onLoad = null;

  socket.addEventListener('message', (message) => {
    const payload = JSON.parse(message.data);
    if (payload.method === 'Page.loadEventFired') {
      onLoad?.();
      return;
    }

    const pending = waiting.get(payload.id);
    if (pending) {
      waiting.delete(payload.id);
      pending(payload);
    }
  });

  const send = (method, params = {}) =>
    new Promise((resolve) => {
      const id = ++nextId;
      waiting.set(id, resolve);
      socket.send(JSON.stringify({ id, method, params, sessionId: session }));
    });

  // Sent before `session` exists, so they cannot go through `send`.
  const raw = (method, params = {}) =>
    new Promise((resolve) => {
      const id = ++nextId;
      waiting.set(id, resolve);
      socket.send(JSON.stringify({ id, method, params }));
    });

  const { result: target } = await raw('Target.createTarget', { url: 'about:blank' });
  const { result: attached } = await raw('Target.attachToTarget', { targetId: target.targetId, flatten: true });
  const session = attached.sessionId;

  await send('Page.enable');
  await send('Runtime.enable');

  const evaluate = async (expression) => {
    const answer = await send('Runtime.evaluate', {
      expression: `(async () => { ${expression} })()`,
      awaitPromise: true,
      returnByValue: true,
    });

    const details = answer.result.exceptionDetails;
    if (details) {
      throw new Error(`${expression}\n  -> ${details.exception?.description ?? details.text}`);
    }

    return answer.result.result.value;
  };

  /**
   * Waits for the load event rather than for the call to return. Navigating between two hashes
   * of the same URL is a same-document navigation and fires no load event at all, so a caller
   * that moves between routes has to go through about:blank -- which is what `blank` is for.
   */
  const navigate = async (url) => {
    const loaded = new Promise((resolve) => { onLoad = resolve; });
    await send('Page.navigate', { url });
    await loaded;
  };

  return {
    send,
    evaluate,
    navigate,
    blank: () => send('Page.navigate', { url: 'about:blank' }),

    // Browser.close before killing the launcher. Edge spawns a tree of child processes and
    // killing the one we started leaves the rest running: a few capture runs had left 148 of
    // them alive, and the next run then found its debugging port taken and failed to start.
    close: async () => {
      try {
        await raw('Browser.close');
      } catch {
        // Already gone, or never answered. The kill below is the fallback.
      }

      socket.close();
      browser.kill();
    },
  };
}
