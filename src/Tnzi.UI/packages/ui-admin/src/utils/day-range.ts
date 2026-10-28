/**
 * Whole-day bounds for a `daterange` picker answer.
 *
 * `NDatePicker type="daterange"` hands back the start (local midnight) of each
 * picked day for BOTH ends. A backend filter of `time >= start AND time <= end`
 * fed those two values as they are drops everything on the last day: a single
 * day range (start = end = midnight) matches nothing at all, and a ten-day
 * range quietly loses its tenth day, in the list and in any export built from
 * the same filter.
 *
 * The end is therefore moved to the last millisecond of its local day, and the
 * start to the first, so a range already normalised this way maps to itself.
 */
export function dayRangeBounds(range: readonly [number, number]): { start: string; end: string } {
  const start = new Date(range[0])
  start.setHours(0, 0, 0, 0)
  const end = new Date(range[1])
  end.setHours(23, 59, 59, 999)
  return { start: start.toISOString(), end: end.toISOString() }
}
