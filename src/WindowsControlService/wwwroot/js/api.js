/**
 * The only module that is allowed to call fetch. A test enforces that, and the reason is the
 * 401: routing every call through one place is what makes "a lost session lands on login, once"
 * a single code path instead of a habit each section has to remember.
 */

/** @type {() => void} */
let sessionLostHandler = () => {};

export function whenSessionLost(handler) {
  sessionLostHandler = handler;
}

/** The stream in events.js reports a dead session too, and it is the same door. */
export function reportSessionLost() {
  sessionLostHandler();
}

/** @type {(reachable: boolean) => void} */
let reachabilityHandler = () => {};

/**
 * Unknown until the first call answers or fails to, which is what the indicator shows before
 * anything has been asked. Starting here rather than at "reachable" is deliberate: it makes the
 * first call of the page a transition like any other, so one code path paints the indicator at
 * boot and keeps it right afterwards.
 * @type {boolean | null}
 */
let reachable = null;

/**
 * Whether the service is answering at all. Reported from here because this is the only module
 * that calls fetch, so it is the only place that sees every attempt -- which makes the answer
 * stay current for the whole session instead of being whatever was true at boot.
 *
 * A response is a response whatever its status: a 500 means the service is there and unhappy,
 * not that it is gone. Only a fetch that rejects means nothing arrived.
 *
 * Called on transitions, as the name says. The handler asks the service for the words to put
 * beside the dot whenever it becomes reachable, and doing that after every successful call would
 * be one extra request per request.
 */
export function whenReachabilityChanges(handler) {
  reachabilityHandler = handler;
}

function reportReachable(next) {
  if (next === reachable) {
    return;
  }

  reachable = next;
  reachabilityHandler(next);
}

export class ApiError extends Error {
  /**
   * @param {number} status HTTP status, or 0 when the request never reached the service.
   * @param {string} message Already suitable for showing to a person.
   */
  constructor(status, message) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
  }
}

const JSON_HEADERS = { Accept: 'application/json', 'Content-Type': 'application/json' };

/**
 * Every error the service produces is problem+json, so there is exactly one shape to read.
 * Validation failures put their reasons under `errors`, business failures under `detail`.
 */
async function describeFailure(response) {
  try {
    const problem = await response.json();
    if (problem.detail) {
      return problem.detail;
    }

    if (problem.errors) {
      const first = Object.values(problem.errors).flat()[0];
      if (first) {
        return first;
      }
    }

    return problem.title ?? `The service answered ${response.status}.`;
  } catch {
    return `The service answered ${response.status}.`;
  }
}

/**
 * @param {{anonymous?: boolean}} options `anonymous` marks the calls where a 401 is an answer
 * rather than an expired session: a wrong password at login is a 401 and must not be mistaken
 * for being signed out.
 */
async function request(method, path, body, { anonymous = false } = {}) {
  let response;
  try {
    response = await fetch(path, {
      method,
      headers: JSON_HEADERS,
      body: body === undefined ? undefined : JSON.stringify(body),
    });
  } catch {
    reportReachable(false);
    throw new ApiError(0, 'The service did not answer. It may be stopped.');
  }

  reportReachable(true);

  if (response.status === 401 && !anonymous) {
    sessionLostHandler();
  }

  if (!response.ok) {
    throw new ApiError(response.status, await describeFailure(response));
  }

  if (response.status === 204) {
    return null;
  }

  const text = await response.text();
  return text ? JSON.parse(text) : null;
}

// Authentication ------------------------------------------------------------

export const getSession = () => request('GET', '/api/auth/session', undefined, { anonymous: true });

export const configurePassword = (password) =>
  request('POST', '/api/auth/password', { password }, { anonymous: true });

export const login = (password) => request('POST', '/api/auth/login', { password }, { anonymous: true });

export const logout = () => request('POST', '/api/auth/logout');

export const changePassword = (currentPassword, newPassword) =>
  request('PUT', '/api/auth/password', { currentPassword, newPassword });

export const getHealth = () => request('GET', '/api/health', undefined, { anonymous: true });

// Applications --------------------------------------------------------------

export const getApplications = () => request('GET', '/api/applications');

export const getPolicyState = () => request('GET', '/api/applications/policy-state');

export const blockApplication = (executablePath, name) =>
  request('POST', '/api/applications', { executablePath, name });

export const setApplicationEnabled = (id, enabled) =>
  request('PATCH', `/api/applications/${id}`, { enabled });

export const deleteApplication = (id) => request('DELETE', `/api/applications/${id}`);

export const getProcesses = () => request('GET', '/api/processes');

// Devices -------------------------------------------------------------------

export const getUsb = () => request('GET', '/api/devices/usb');

export const setUsbBlocked = (blocked) => request('PUT', '/api/devices/usb', { blocked });

// History -------------------------------------------------------------------

export const getAccessHistory = ({ limit, offset, origin }) => {
  const query = new URLSearchParams({ limit: String(limit), offset: String(offset) });
  if (origin && origin !== 'all') {
    query.set('origin', origin);
  }

  return request('GET', `/api/access-history?${query}`);
};
