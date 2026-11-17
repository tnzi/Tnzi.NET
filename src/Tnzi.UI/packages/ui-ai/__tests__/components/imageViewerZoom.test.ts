// @vitest-environment node
/** The viewer's zoom and pan as values: bounds, the point that stays put, and the clamp. */
import { describe, expect, it } from 'vitest'
import { FITTED, MAX_SCALE, clampView, isZoomed, panBy, zoomAt } from '../../src/components/overlay/image-viewer-zoom'

const picture = { width: 400, height: 300 }
const area = { width: 1000, height: 800 }

describe('image viewer zoom', () => {
  it('keeps the scale between fitted and the most', () => {
    expect(zoomAt(FITTED, 0.2, { x: 0, y: 0 }, picture, area)).toEqual(FITTED)
    expect(zoomAt(FITTED, 99, { x: 0, y: 0 }, picture, area).scale).toBe(MAX_SCALE)
    expect(isZoomed(FITTED)).toBe(false)
  })

  it('keeps the point it zooms around where it was on the screen', () => {
    const point = { x: 60, y: -40 } // near enough the centre that the clamp leaves it alone
    const view = zoomAt(FITTED, 4, point, picture, area)
    // The picture's point under `point` before: (point - offset) / scale; it must map to `point` again.
    expect((point.x - view.x) / view.scale).toBeCloseTo(point.x)
    expect((point.y - view.y) / view.scale).toBeCloseTo(point.y)
  })

  it('moves the picture no further than its edges, and not at all along a side it does not fill', () => {
    const wide = { scale: 3, x: 0, y: 0 } // 1200 x 900: 100 x 50 to move each way
    expect(panBy(wide, 500, -500, picture, area)).toEqual({ scale: 3, x: 100, y: -50 })
    expect(clampView({ scale: 2, x: 40, y: 40 }, picture, area)).toEqual({ scale: 2, x: 0, y: 0 })
  })
})
