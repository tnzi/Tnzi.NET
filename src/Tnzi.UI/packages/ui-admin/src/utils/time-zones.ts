/**
 * IANA time zone helpers for forms that let a person say which zone their
 * wall-clock times are read in (notification quiet hours, for one).
 *
 * Kept to the runtime's own `Intl` data: no zone table is bundled, and the
 * server accepts exactly the IANA ids a browser reports.
 */

/** The browser's IANA zone, or `'UTC'` when the runtime cannot tell. */
export function browserTimeZone(): string {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC'
  } catch {
    return 'UTC'
  }
}

/**
 * Every IANA zone this runtime knows, plus the ids a form must always be able
 * to show (the stored value, the browser's own), de-duplicated and sorted.
 *
 * The extras matter: a stored legacy alias ("Asia/Calcutta") or "UTC" is not
 * in every runtime's canonical list, and a select that cannot display its
 * current value renders blank and then saves something else.
 */
export function timeZoneIds(extra: ReadonlyArray<string | null | undefined> = []): string[] {
  let known: string[] = []
  try {
    known = typeof Intl.supportedValuesOf === 'function' ? Intl.supportedValuesOf('timeZone') : []
  } catch {
    known = []
  }
  const ids = new Set<string>(['UTC', ...known])
  for (const id of extra) {
    if (id) ids.add(id)
  }
  return [...ids].sort((a, b) => a.localeCompare(b))
}

interface WallClock {
  year: number
  month: number
  day: number
  hour: number
  minute: number
  second: number
}

/** The wall clock `zone` shows at `instant` (epoch milliseconds). Throws on an unknown zone. */
function wallClockAt(instant: number, zone: string): WallClock {
  const parts = new Intl.DateTimeFormat('en-US', {
    timeZone: zone,
    hourCycle: 'h23',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  }).formatToParts(new Date(instant))
  const field = (type: Intl.DateTimeFormatPartTypes): number => Number(parts.find((p) => p.type === type)?.value ?? 0)
  return {
    year: field('year'),
    month: field('month'),
    day: field('day'),
    // Some engines still print midnight as "24" despite h23.
    hour: field('hour') % 24,
    minute: field('minute'),
    second: field('second'),
  }
}

/** How far `zone` is ahead of UTC at `instant`, in milliseconds (whole seconds). */
function offsetAt(instant: number, zone: string): number {
  const w = wallClockAt(instant, zone)
  return Date.UTC(w.year, w.month - 1, w.day, w.hour, w.minute, w.second) - Math.floor(instant / 1000) * 1000
}

function parseTimeOfDay(time: string): [number, number, number] | null {
  const m = /^(\d{1,2}):(\d{2})(?::(\d{2}))?/.exec(time)
  return m ? [Number(m[1]), Number(m[2]), Number(m[3] ?? 0)] : null
}

function pad2(n: number): string {
  return String(n).padStart(2, '0')
}

/**
 * Convert a wall-clock time of day ("HH:mm:ss") from one IANA zone to another,
 * keeping the instant it names on the day `at` falls on in `fromZone`.
 *
 * This is what moving a quiet-hours window to another zone must do: 03:00 UTC
 * read in Toronto in July is 23:00, so the window keeps applying at the same
 * moments today. Relabelling the zone instead would shift every message by the
 * offset between the two zones.
 *
 * Returns `null` when either zone is unknown to this runtime or the time does
 * not parse; the caller then leaves the window where it is.
 */
export function convertTimeOfDay(time: string, fromZone: string, toZone: string, at: Date = new Date()): string | null {
  const hms = parseTimeOfDay(time)
  if (!hms) return null
  if (fromZone === toZone) return time
  try {
    const day = wallClockAt(at.getTime(), fromZone)
    const asUtc = Date.UTC(day.year, day.month - 1, day.day, hms[0], hms[1], hms[2])
    // Two passes settle the offset on days where the zone changes it.
    const firstGuess = asUtc - offsetAt(asUtc, fromZone)
    const instant = asUtc - offsetAt(firstGuess, fromZone)
    const target = wallClockAt(instant, toZone)
    return `${pad2(target.hour)}:${pad2(target.minute)}:${pad2(target.second)}`
  } catch {
    return null
  }
}
