/**
 * Every timestamp the service sends is UTC in ISO 8601. Turning it into local time is this
 * layer's job, and only for display: the raw value stays in state, because a value that has
 * been formatted cannot be compared with the next one that arrives.
 */

const dateTime = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'medium' });

export function formatTimestamp(iso) {
  if (!iso) {
    return '—';
  }

  const parsed = new Date(iso);
  return Number.isNaN(parsed.getTime()) ? '—' : dateTime.format(parsed);
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
