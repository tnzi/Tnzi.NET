/**
 * Window sizing and placement policy for the `desktop` layout.
 *
 * Pure functions, no store and no DOM, because this is the part that is easy to
 * get subtly wrong and worth testing directly: every rule here exists to stop a
 * window ending up somewhere the user cannot fix by hand.
 *
 * ## The three questions
 *
 * 1. **How big should a window open?** Not one size for everything - a register
 *    of journal lines and a profile form want different windows. A route says
 *    what it needs through `meta.window`; anything that says nothing gets the
 *    `wide` preset, because most admin pages are tables and a table that opens
 *    with columns cut off is the complaint this whole module answers.
 *
 * 2. **Can we reuse what the user last set?** Yes, when it still makes sense.
 *    Someone who widened a window to see a hidden column should not have to do
 *    it again. But a size remembered from a 2560px monitor is not a size on a
 *    1366px laptop, so a remembered geometry that no longer fits is discarded
 *    and the route's own default is used instead.
 *
 * 3. **Does it land on top of something?** A new window that opens exactly over
 *    an existing one looks like nothing happened. {@link placeNewWindow} walks
 *    a cascade until it finds a spot nothing is at - including when the
 *    remembered position of one module is where another module already sits.
 *
 * 4. **Is it reachable?** A window whose title bar is off-canvas cannot be
 *    dragged back, cannot be resized, and cannot be closed except through the
 *    taskbar. {@link clampWindowPosition} is the single definition of "enough
 *    of it is on screen", used by the drag gesture, by the surface-resize
 *    rescue, and by placement - so they cannot disagree.
 */

/** Smallest a window may be dragged down to. */
export const MIN_WINDOW_SIZE = { width: 360, height: 240 } as const

/**
 * How much of a window must stay inside the surface.
 *
 * Enough to grab and to read part of the title - the point is that no gesture
 * and no screen-size change can leave a window with no handle on it.
 */
export const MIN_VISIBLE = 96

/** Gap left around a `full` window so it still reads as a window. */
const FULL_MARGIN = 40

/** Diagonal offset between cascaded windows. */
const CASCADE_STEP = 28
/**
 * How far the cascade will walk looking for a free spot.
 *
 * Past this it gives up and accepts an overlap: with that many windows open
 * they are overlapping anyway, and marching one further off the edge every time
 * is worse than landing on something the user can move.
 */
const MAX_CASCADE_STEPS = 16
/**
 * Origins closer together than this read as "the same place".
 *
 * Below one cascade step, so the very next slot always counts as free - the
 * search would otherwise never terminate.
 */
const OVERLAP_TOLERANCE = 24

export interface WindowSize {
  width: number
  height: number
}

export interface WindowGeometry extends WindowSize {
  x: number
  y: number
}

export interface Surface {
  width: number
  height: number
}

/**
 * Named sizes, so a route can say what it needs without hard-coding pixels
 * that stop making sense when the chrome around them changes.
 */
export type WindowSizePreset = 'compact' | 'medium' | 'wide' | 'full'

/**
 * What a route asks for. Explicit `width`/`height` win over `preset`; both are
 * optional, and a route that declares neither gets {@link DEFAULT_PRESET}.
 */
export interface WindowSizeHint {
  preset?: WindowSizePreset
  width?: number
  height?: number
}

/**
 * Preset dimensions.
 *
 * `wide` allows for the ~190px module navigation the window now carries on its
 * left, so a table still gets roughly a full-page column budget beside it.
 */
export const WINDOW_SIZE_PRESETS: Record<Exclude<WindowSizePreset, 'full'>, WindowSize> = {
  compact: { width: 560, height: 560 },
  medium: { width: 860, height: 640 },
  wide: { width: 1180, height: 760 },
}

/** Most admin pages are tables; tables want width. */
export const DEFAULT_PRESET: WindowSizePreset = 'wide'

function isFiniteNumber(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value)
}

/** Whether a surface has been measured yet. Zero means "no idea", not "tiny". */
function isMeasured(surface: Surface | undefined): surface is Surface {
  return !!surface && surface.width > 0 && surface.height > 0
}

/**
 * The size a route should open at, fitted to the surface.
 *
 * Always returns something usable: the requested size is clamped up to the
 * minimum and down to the surface, so even a 320px viewport gets a window it
 * can show rather than one hanging off both edges.
 */
export function resolveWindowSize(hint: WindowSizeHint | undefined, surface?: Surface): WindowSize {
  const measured = isMeasured(surface)
  let size: WindowSize

  if (isFiniteNumber(hint?.width) && isFiniteNumber(hint?.height)) {
    size = { width: hint.width, height: hint.height }
  } else if (hint?.preset === 'full') {
    size = measured
      ? { width: surface.width - FULL_MARGIN, height: surface.height - FULL_MARGIN }
      : WINDOW_SIZE_PRESETS.wide
  } else {
    const preset = hint?.preset ?? DEFAULT_PRESET
    size = WINDOW_SIZE_PRESETS[preset as Exclude<WindowSizePreset, 'full'>] ?? WINDOW_SIZE_PRESETS.wide
  }

  return {
    width: fit(size.width, MIN_WINDOW_SIZE.width, measured ? surface.width : undefined),
    height: fit(size.height, MIN_WINDOW_SIZE.height, measured ? surface.height : undefined),
  }
}

function fit(value: number, min: number, max: number | undefined): number {
  const lower = Math.max(value, min)
  return max === undefined ? lower : Math.min(lower, Math.max(max, min))
}

/**
 * Whether a remembered size may be reused.
 *
 * Rejects anything that no longer fits rather than shrinking it: a size carried
 * over from a larger screen is not the user's preference for THIS screen, and
 * silently squashing it produces a window that is neither what they set nor
 * what the page asked for. Falling back to the route's default is the answer a
 * user can predict.
 */
export function isRememberedSizeUsable(size: Partial<WindowSize> | undefined, surface?: Surface): boolean {
  if (!size || !isFiniteNumber(size.width) || !isFiniteNumber(size.height)) return false
  if (size.width < MIN_WINDOW_SIZE.width || size.height < MIN_WINDOW_SIZE.height) return false
  if (!isMeasured(surface)) return true
  return size.width <= surface.width && size.height <= surface.height
}

/**
 * Bound a position so the window keeps a grabbable edge on the surface.
 *
 * Horizontally a window may hang off either side - that is how you park a wide
 * one to read its right-hand columns - as long as {@link MIN_VISIBLE} stays in
 * view. Vertically the top is hard-bounded at 0: the title bar is the only
 * drag handle, and above the top edge it is gone entirely, which is precisely
 * the "dragged off screen, now unusable" case.
 */
export function clampWindowPosition(
  geometry: WindowGeometry,
  surface?: Surface,
): { x: number; y: number } {
  if (!isMeasured(surface)) return { x: Math.max(geometry.x, 0), y: Math.max(geometry.y, 0) }
  const minX = Math.min(0, MIN_VISIBLE - geometry.width)
  const maxX = Math.max(minX, surface.width - MIN_VISIBLE)
  const maxY = Math.max(0, surface.height - MIN_VISIBLE)
  return {
    x: Math.min(Math.max(geometry.x, minX), maxX),
    y: Math.min(Math.max(geometry.y, 0), maxY),
  }
}

/** Whether a position needs no rescue. */
export function isPositionReachable(geometry: WindowGeometry, surface?: Surface): boolean {
  const clamped = clampWindowPosition(geometry, surface)
  return clamped.x === geometry.x && clamped.y === geometry.y
}

/** Top-left corner of a window already on screen. */
export interface OccupiedSpot {
  x: number
  y: number
}

function isSpotTaken(x: number, y: number, taken: readonly OccupiedSpot[]): boolean {
  return taken.some(
    (spot) =>
      Math.abs(spot.x - x) < OVERLAP_TOLERANCE && Math.abs(spot.y - y) < OVERLAP_TOLERANCE,
  )
}

/**
 * Where to put a window that is about to open.
 *
 * Starts from `preferred` when there is one (the position this module was last
 * left at) and otherwise from the centre - horizontally centred and a third
 * down, which leaves room for the shadow and reads better than dead centre.
 *
 * Then it walks the cascade until it finds a spot no open window is already at.
 * Searching for a FREE slot rather than counting windows is the point: indexing
 * by "how many are open" hands out a slot that is still occupied as soon as one
 * of the earlier windows is closed, and wraps back onto the first window once
 * the counter laps. Both were measured doing exactly that.
 *
 * `taken` should list the windows actually on screen. A minimised one occupies
 * no space, and treating it as an obstacle pushes new windows down the cascade
 * for a reason the user cannot see.
 */
export function placeNewWindow(
  size: WindowSize,
  surface?: Surface,
  taken: readonly OccupiedSpot[] = [],
  preferred?: OccupiedSpot,
): WindowGeometry {
  const base = preferred ?? defaultOrigin(size, surface)

  let last = { x: base.x, y: base.y }
  for (let i = 0; i < MAX_CASCADE_STEPS; i += 1) {
    const offset = i * CASCADE_STEP
    const candidate = clampWindowPosition(
      { x: base.x + offset, y: base.y + offset, ...size },
      surface,
    )
    if (!isSpotTaken(candidate.x, candidate.y, taken)) return { ...candidate, ...size }
    // Clamping saturates near the edges, so further steps stop moving. Nothing
    // is gained by walking the rest of the way.
    if (i > 0 && candidate.x === last.x && candidate.y === last.y) break
    last = candidate
  }
  return { ...clampWindowPosition({ ...base, ...size }, surface), ...size }
}

function defaultOrigin(size: WindowSize, surface?: Surface): OccupiedSpot {
  if (!isMeasured(surface)) return { x: 24, y: 24 }
  return {
    x: Math.round((surface.width - size.width) / 2),
    y: Math.round((surface.height - size.height) / 3),
  }
}

/**
 * Move a window so it keeps its place RELATIVE to a resized surface.
 *
 * The position is expressed as a fraction of the free space around the window,
 * not as raw pixels: that is what makes a centred window stay centred and one
 * parked against the right edge stay against the right edge, instead of the
 * whole layout drifting left as the browser narrows.
 *
 * Size is not touched. Shrinking on every resize event would ratchet windows
 * smaller as someone drags their browser narrower and never give the size back
 * - and a window merely hanging over an edge is still perfectly usable, whereas
 * one that has silently lost 300px of table is not.
 *
 * A window wider than the surface has no free space to take a fraction of, so
 * it goes to the near edge.
 */
export function rescaleWindowPosition(
  geometry: WindowGeometry,
  from: Surface | undefined,
  to: Surface | undefined,
): { x: number; y: number } {
  if (!isMeasured(from) || !isMeasured(to)) return clampWindowPosition(geometry, to)
  const scale = (value: number, size: number, before: number, after: number): number => {
    const freeBefore = before - size
    const freeAfter = after - size
    if (freeBefore <= 0 || freeAfter <= 0) return freeAfter <= 0 ? 0 : value
    return Math.round((value / freeBefore) * freeAfter)
  }
  return clampWindowPosition(
    {
      ...geometry,
      x: scale(geometry.x, geometry.width, from.width, to.width),
      y: scale(geometry.y, geometry.height, from.height, to.height),
    },
    to,
  )
}

/**
 * Full fit: size AND position, for geometry restored from a previous session.
 *
 * Unlike the resize-time rescue this one may shrink, because a window persisted
 * from a wider monitor genuinely cannot be shown at that size here.
 */
export function fitGeometryToSurface(geometry: WindowGeometry, surface?: Surface): WindowGeometry {
  const measured = isMeasured(surface)
  const size: WindowSize = {
    width: fit(geometry.width, MIN_WINDOW_SIZE.width, measured ? surface.width : undefined),
    height: fit(geometry.height, MIN_WINDOW_SIZE.height, measured ? surface.height : undefined),
  }
  return { ...clampWindowPosition({ ...geometry, ...size }, surface), ...size }
}
