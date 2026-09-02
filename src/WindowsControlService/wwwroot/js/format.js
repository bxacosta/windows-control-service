/**
 * Every timestamp the service sends is UTC in ISO 8601. Turning it into local time is this
 * layer's job, and only for display: the raw value stays in state, because a value that has
 * been formatted cannot be compared with the next one that arrives.
 */

/**
 * Twenty-four hours, everywhere, whatever the machine's locale prefers. This is a control panel
 * for a machine: 18:45:03 is one reading and 6:45:03 PM is two, and the log this interface shows
 * comes out of the Windows event log in 24 hours to begin with.
 *
 * Built from explicit components rather than from `timeStyle`, because a style is a request to
 * the locale and the components are an instruction.
 */
const CLOCK = { hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false };
const CALENDAR = { day: '2-digit', month: 'short', year: 'numeric' };

const time = new Intl.DateTimeFormat(undefined, CLOCK);
const dateTime = new Intl.DateTimeFormat(undefined, { ...CALENDAR, ...CLOCK });

const parse = (iso) => {
  const parsed = new Date(iso);
  return Number.isNaN(parsed.getTime()) ? null : parsed;
};

export function formatTimestamp(iso) {
  if (!iso) {
    return '—';
  }

  const parsed = parse(iso);
  return parsed === null ? '—' : dateTime.format(parsed);
}

/** Under this, "how long ago" is the useful reading; over it, the clock is. */
const RELATIVE_LIMIT = 6 * 3600;
const SAME_DAY_LIMIT = 24 * 3600;

/**
 * When something happened, said the way it is actually read at that distance. Minutes ago is a
 * duration; this morning is a time; last week is a date. "19 h ago" is none of the three -- it
 * makes the reader do the subtraction the interface was supposed to have done.
 */
export function formatWhen(iso, now = Date.now()) {
  const parsed = parse(iso);
  if (parsed === null) {
    return 'never';
  }

  const seconds = Math.max(0, Math.round((now - parsed.getTime()) / 1000));

  if (seconds < RELATIVE_LIMIT) {
    return formatAgo(iso, now);
  }

  return seconds < SAME_DAY_LIMIT ? time.format(parsed) : dateTime.format(parsed);
}

/** "40 s ago", "3 min ago". Coarse on purpose: this is a freshness cue, not a measurement. */
export function formatAgo(iso, now = Date.now()) {
  if (!iso) {
    return 'never';
  }

  const seconds = Math.max(0, Math.round((now - new Date(iso).getTime()) / 1000));
  if (seconds < 60) {
    return `${seconds} s ago`;
  }

  const minutes = Math.round(seconds / 60);
  return minutes < 60 ? `${minutes} min ago` : `${Math.round(minutes / 60)} h ago`;
}

/** Session lengths run from seconds to days, so the unit follows the size. */
export function formatDuration(seconds) {
  if (seconds === null || seconds === undefined) {
    return '—';
  }

  if (seconds < 60) {
    return `${seconds} s`;
  }

  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) {
    return `${minutes} min`;
  }

  const hours = Math.floor(minutes / 60);
  return hours < 24 ? `${hours} h ${minutes % 60} min` : `${Math.floor(hours / 24)} d ${hours % 24} h`;
}

/**
 * How long the service has been up, from the instant it started.
 *
 * The first unit shown is not padded and every unit after it is two digits: "4d 06h 12m" reads
 * as one measurement, where "4d 6h 12m" reads as three numbers that happen to be adjacent.
 *
 * A unit is dropped only while nothing larger has been shown. Zero hours between days and
 * minutes is information, and dropping it would turn four days and twelve minutes into
 * "4d 12m" -- which is a different, much shorter, duration.
 */
export function formatUptime(iso, now = Date.now()) {
  const parsed = parse(iso);
  if (parsed === null) {
    return '—';
  }

  // Clamped rather than allowed to go negative. The service and this page run on one machine and
  // read one clock, so the only way to get here is a clock that moved under both of them.
  const totalMinutes = Math.floor(Math.max(0, now - parsed.getTime()) / 60000);
  if (totalMinutes < 1) {
    return '<1m';
  }

  const days = Math.floor(totalMinutes / 1440);
  const hours = Math.floor(totalMinutes / 60) % 24;
  const minutes = totalMinutes % 60;
  const pad = (value) => String(value).padStart(2, '0');

  if (days > 0) {
    return `${days}d ${pad(hours)}h ${pad(minutes)}m`;
  }

  return hours > 0 ? `${hours}h ${pad(minutes)}m` : `${minutes}m`;
}
