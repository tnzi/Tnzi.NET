/**
 * Date-only values on the wire.
 *
 * A `type: 'date'` field (a form field or an advanced-search filter) is an
 * `NDatePicker`, and the picker hands back a number: the local midnight of the
 * picked day, in epoch milliseconds. The backend binds a `DateTime`, and a JSON
 * number or a query string of digits is not one: the request fails model
 * binding and comes back 400 before any service runs. What the operator picked
 * is a calendar date, so that is what goes on the wire: `YYYY-MM-DD`, built
 * from the LOCAL date parts (`toISOString()` would move a pick east of UTC to
 * the previous day).
 *
 * An endpoint that filters a timestamp column (`time >= start AND time <= end`)
 * needs instants instead, and the end bound has to cover the whole of its day;
 * {@link toLocalDayBounds} turns calendar-date filters into those.
 */

const ISO_DATE = /^(\d{4})-(\d{2})-(\d{2})$/

/** Local date (00:00) → the `YYYY-MM-DD` string the backend date binder accepts. */
export function tsToIsoDate(ts: number): string {
  const d = new Date(ts)
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
}

/**
 * A date field's model value → what to send for it, or `null` when it is empty.
 *
 * A picked day is a number and becomes `YYYY-MM-DD`. A string is the value the
 * record arrived with and is sent back unchanged: an untouched field must not
 * move, and a backend value may carry a time of day the picker never showed.
 * A `Date` goes out as the instant it holds, as `JSON.stringify` would send it.
 */
export function toIsoDateValue(value: unknown): string | null {
  if (typeof value === 'number') return Number.isFinite(value) ? tsToIsoDate(value) : null
  if (typeof value === 'string') return value.trim() ? value : null
  if (value instanceof Date) return Number.isNaN(value.getTime()) ? null : value.toISOString()
  return null
}

function localDay(value: unknown): Date | null {
  if (typeof value === 'number') return Number.isFinite(value) ? new Date(value) : null
  if (typeof value !== 'string') return null
  const m = ISO_DATE.exec(value)
  return m ? new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3])) : null
}

/**
 * A date value as it travels → what an `NDatePicker` shows: the inverse of
 * {@link tsToIsoDate}. `YYYY-MM-DD` becomes that day's local midnight, a
 * timestamp passes through, and anything else is `null` (an empty picker).
 */
export function isoDateToTs(value: unknown): number | null {
  const day = localDay(value)
  return day ? day.getTime() : null
}

/**
 * Calendar-date filters → the instants that bound those days in the viewer's
 * time zone, for an endpoint that compares timestamps.
 *
 * `startKey` becomes the first millisecond of its local day and `endKey` the
 * last, so a one-day range matches that whole day rather than nothing, and the
 * last day of a longer range is kept. A value that is not a calendar date (an
 * instant some caller already built) is left as it is. Returns a new object.
 */
export function toLocalDayBounds(
  filters: Readonly<Record<string, unknown>>,
  startKey: string,
  endKey: string,
): Record<string, unknown> {
  const out: Record<string, unknown> = { ...filters }
  const start = localDay(filters[startKey])
  if (start) {
    start.setHours(0, 0, 0, 0)
    out[startKey] = start.toISOString()
  }
  const end = localDay(filters[endKey])
  if (end) {
    end.setHours(23, 59, 59, 999)
    out[endKey] = end.toISOString()
  }
  return out
}
