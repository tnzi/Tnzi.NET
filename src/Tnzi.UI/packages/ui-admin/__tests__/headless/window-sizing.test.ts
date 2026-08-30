import { describe, it, expect } from 'vitest'
import {
  DEFAULT_PRESET,
  MIN_VISIBLE,
  MIN_WINDOW_SIZE,
  WINDOW_SIZE_PRESETS,
  clampWindowPosition,
  fitGeometryToSurface,
  isPositionReachable,
  isRememberedSizeUsable,
  placeNewWindow,
  rescaleWindowPosition,
  resolveWindowSize,
} from '../../src/headless/window-sizing'

const LAPTOP = { width: 1366, height: 768 }
const BIG = { width: 2560, height: 1440 }

describe('resolveWindowSize', () => {
  it('defaults to the table-shaped preset when a route says nothing', () => {
    // Most admin pages are tables, and a table that opens with columns cut off
    // is the complaint this module answers.
    expect(resolveWindowSize(undefined, BIG)).toEqual(WINDOW_SIZE_PRESETS[DEFAULT_PRESET as 'wide'])
  })

  it('honours a named preset', () => {
    expect(resolveWindowSize({ preset: 'compact' }, BIG)).toEqual(WINDOW_SIZE_PRESETS.compact)
    expect(resolveWindowSize({ preset: 'medium' }, BIG)).toEqual(WINDOW_SIZE_PRESETS.medium)
  })

  it('honours explicit pixels over a preset', () => {
    expect(resolveWindowSize({ preset: 'compact', width: 900, height: 700 }, BIG)).toEqual({
      width: 900,
      height: 700,
    })
  })

  it('grows `full` to the surface, leaving a margin', () => {
    const size = resolveWindowSize({ preset: 'full' }, LAPTOP)
    expect(size.width).toBeLessThan(LAPTOP.width)
    expect(size.width).toBeGreaterThan(LAPTOP.width - 100)
  })

  it('never returns a window larger than the surface it opens on', () => {
    const size = resolveWindowSize({ width: 4000, height: 4000 }, LAPTOP)
    expect(size).toEqual({ width: LAPTOP.width, height: LAPTOP.height })
  })

  it('never returns one smaller than the minimum', () => {
    expect(resolveWindowSize({ width: 10, height: 10 }, BIG)).toEqual({ ...MIN_WINDOW_SIZE })
  })

  it('falls back to a fixed preset before the surface is measured', () => {
    // Zero is "not measured yet", not "a zero-pixel screen"; treating it as a
    // size would open every window at the minimum on the first paint.
    expect(resolveWindowSize({ preset: 'full' }, { width: 0, height: 0 })).toEqual(
      WINDOW_SIZE_PRESETS.wide,
    )
  })
})

describe('isRememberedSizeUsable', () => {
  it('reuses a size that still fits', () => {
    expect(isRememberedSizeUsable({ width: 1200, height: 700 }, LAPTOP)).toBe(true)
  })

  it('rejects a size carried over from a bigger screen', () => {
    // The reason the whole check exists: 2400px wide is not this user's
    // preference on a 1366px laptop, and squashing it produces a window that is
    // neither what they set nor what the page asked for.
    expect(isRememberedSizeUsable({ width: 2400, height: 1300 }, LAPTOP)).toBe(false)
  })

  it('rejects nonsense', () => {
    expect(isRememberedSizeUsable(undefined, LAPTOP)).toBe(false)
    expect(isRememberedSizeUsable({ width: Number.NaN, height: 500 }, LAPTOP)).toBe(false)
    expect(isRememberedSizeUsable({ width: 20, height: 20 }, LAPTOP)).toBe(false)
  })
})

describe('clampWindowPosition', () => {
  it('keeps a grabbable strip on screen when pushed right or down', () => {
    const geom = { x: 9999, y: 9999, width: 800, height: 600 }
    const { x, y } = clampWindowPosition(geom, LAPTOP)
    expect(x).toBe(LAPTOP.width - MIN_VISIBLE)
    expect(y).toBe(LAPTOP.height - MIN_VISIBLE)
  })

  it('allows hanging off the left, but not past the point of no return', () => {
    // Parking a wide window to read its right-hand columns is legitimate.
    const geom = { x: -9999, y: 50, width: 800, height: 600 }
    const { x } = clampWindowPosition(geom, LAPTOP)
    expect(x).toBe(MIN_VISIBLE - 800)
    expect(x + 800).toBe(MIN_VISIBLE)
  })

  it('pins the top edge at zero', () => {
    // The title bar is the only drag handle; above the top edge it is gone.
    expect(clampWindowPosition({ x: 0, y: -300, width: 800, height: 600 }, LAPTOP).y).toBe(0)
  })

  it('leaves a window that is already fine exactly where it is', () => {
    const geom = { x: 120, y: 80, width: 800, height: 600 }
    expect(clampWindowPosition(geom, LAPTOP)).toEqual({ x: 120, y: 80 })
    expect(isPositionReachable(geom, LAPTOP)).toBe(true)
  })

  it('flags the stranded case', () => {
    expect(isPositionReachable({ x: 1800, y: 40, width: 800, height: 600 }, LAPTOP)).toBe(false)
  })
})

describe('placeNewWindow', () => {
  const SIZE = { width: 800, height: 600 }

  it('centres the first window', () => {
    const first = placeNewWindow(SIZE, LAPTOP, [])
    expect(first.x).toBe(Math.round((LAPTOP.width - 800) / 2))
  })

  it('steps clear of a window already at that spot', () => {
    const first = placeNewWindow(SIZE, LAPTOP, [])
    const second = placeNewWindow(SIZE, LAPTOP, [first])
    expect(second.x).toBeGreaterThan(first.x)
    expect(second.y).toBeGreaterThan(first.y)
  })

  it('reuses a slot freed by a closed window instead of stacking past it', () => {
    // The measured bug: slots were handed out by counting open windows, so
    // closing one in the middle handed the next window a number that another
    // window still occupied - an exact overlap.
    const slot0 = placeNewWindow(SIZE, LAPTOP, [])
    const slot1 = placeNewWindow(SIZE, LAPTOP, [slot0])
    const slot2 = placeNewWindow(SIZE, LAPTOP, [slot0, slot1])

    // slot1's window is closed; two remain.
    const next = placeNewWindow(SIZE, LAPTOP, [slot0, slot2])
    expect(next).toMatchObject({ x: slot1.x, y: slot1.y })
    expect(next.x).not.toBe(slot2.x)
  })

  it('never hands out a spot another window is already at', () => {
    const taken: { x: number; y: number }[] = []
    for (let i = 0; i < 9; i += 1) {
      const spot = placeNewWindow(SIZE, LAPTOP, taken)
      // The old cascade lapped after eight and put the ninth on the first.
      expect(taken.some((t) => t.x === spot.x && t.y === spot.y)).toBe(false)
      taken.push({ x: spot.x, y: spot.y })
    }
  })

  it('keeps a remembered position when nothing is there', () => {
    const preferred = { x: 60, y: 40 }
    expect(placeNewWindow(SIZE, LAPTOP, [], preferred)).toMatchObject(preferred)
  })

  it('steps off a remembered position another window occupies', () => {
    // Two modules dragged to the same corner: the second would otherwise open
    // exactly under the first and look like it never opened at all.
    const preferred = { x: 60, y: 40 }
    const placed = placeNewWindow(SIZE, LAPTOP, [preferred], preferred)
    expect(placed.x).not.toBe(preferred.x)
    expect(placed.y).not.toBe(preferred.y)
  })

  it('still lands somewhere reachable on a surface smaller than the window', () => {
    const tiny = { width: 500, height: 400 }
    const placed = placeNewWindow(SIZE, tiny, [])
    expect(isPositionReachable({ ...placed }, tiny)).toBe(true)
  })

  it('gives up gracefully rather than marching off the edge', () => {
    // Enough windows that every slot is taken. Overlap is unavoidable then;
    // what must not happen is a window placed somewhere unreachable.
    const taken = Array.from({ length: 40 }, (_, i) => ({ x: 100 + i * 28, y: 40 + i * 28 }))
    const placed = placeNewWindow(SIZE, LAPTOP, taken)
    expect(isPositionReachable({ ...placed }, LAPTOP)).toBe(true)
  })
})

describe('rescaleWindowPosition', () => {
  const WIDE = { width: 1600, height: 900 }
  const NARROW = { width: 1200, height: 700 }
  const SIZE = { width: 600, height: 400 }

  it('keeps a centred window centred', () => {
    const centred = { x: (WIDE.width - SIZE.width) / 2, y: (WIDE.height - SIZE.height) / 2, ...SIZE }
    const moved = rescaleWindowPosition(centred, WIDE, NARROW)
    expect(moved.x).toBe(Math.round((NARROW.width - SIZE.width) / 2))
    expect(moved.y).toBe(Math.round((NARROW.height - SIZE.height) / 2))
  })

  it('keeps a window parked against the right edge against the right edge', () => {
    // The point of scaling the FREE SPACE rather than the raw offset: a pixel
    // offset means something different on a different-sized screen.
    const right = { x: WIDE.width - SIZE.width, y: 40, ...SIZE }
    expect(rescaleWindowPosition(right, WIDE, NARROW).x).toBe(NARROW.width - SIZE.width)
  })

  it('leaves a window at the origin at the origin', () => {
    expect(rescaleWindowPosition({ x: 0, y: 0, ...SIZE }, WIDE, NARROW)).toEqual({ x: 0, y: 0 })
  })

  it('sends a window with no room left to the near edge, still reachable', () => {
    const tiny = { width: 500, height: 350 }
    const moved = rescaleWindowPosition({ x: 900, y: 400, ...SIZE }, WIDE, tiny)
    expect(isPositionReachable({ ...moved, ...SIZE }, tiny)).toBe(true)
  })

  it('falls back to a plain clamp when the previous surface is unknown', () => {
    const moved = rescaleWindowPosition({ x: 9999, y: 9999, ...SIZE }, undefined, NARROW)
    expect(isPositionReachable({ ...moved, ...SIZE }, NARROW)).toBe(true)
  })
})

describe('fitGeometryToSurface', () => {
  it('shrinks and moves geometry restored from a wider monitor', () => {
    const fixed = fitGeometryToSurface({ x: 2000, y: 1100, width: 2000, height: 1200 }, LAPTOP)
    expect(fixed.width).toBe(LAPTOP.width)
    expect(fixed.height).toBe(LAPTOP.height)
    expect(isPositionReachable(fixed, LAPTOP)).toBe(true)
  })
})
