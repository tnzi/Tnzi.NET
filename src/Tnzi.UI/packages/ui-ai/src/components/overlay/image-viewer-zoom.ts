/**
 * The zoom and pan of `TImageViewer`, as plain values: how far the picture is scaled and how far
 * its centre is moved from the centre of its area. At scale 1 the picture fits its area (the
 * viewer lays it out with `object-fit: contain`), so 1 is also the least; it cannot be moved
 * there. Every change returns a new view, already held inside the bounds.
 */

export interface ImageView {
  /** 1 = fitted to the area. */
  readonly scale: number
  /** The picture's centre from the area's centre, in CSS pixels. */
  readonly x: number
  readonly y: number
}

export interface Size {
  readonly width: number
  readonly height: number
}

export const FITTED: ImageView = { scale: 1, x: 0, y: 0 }
export const MIN_SCALE = 1
export const MAX_SCALE = 8
/** One press of a zoom button or key. */
export const ZOOM_STEP = 1.5
/** What a double click zooms to from the fitted picture. */
export const DOUBLE_CLICK_SCALE = 2.5

/**
 * Holds the picture so it never leaves its area with space showing on a side it could cover:
 * along an axis where the scaled picture is smaller than the area it stays centred, otherwise its
 * edges go no further in than the area's.
 */
export function clampView(view: ImageView, picture: Size, area: Size): ImageView {
  const scale = Math.min(MAX_SCALE, Math.max(MIN_SCALE, view.scale))
  const limitX = Math.max(0, (picture.width * scale - area.width) / 2)
  const limitY = Math.max(0, (picture.height * scale - area.height) / 2)
  return {
    scale,
    x: Math.min(limitX, Math.max(-limitX, view.x)),
    y: Math.min(limitY, Math.max(-limitY, view.y)),
  }
}

/**
 * Scales to `scale` around a point (from the area's centre) that stays where it is on the screen:
 * the pointer's place for a wheel or a pinch, the centre for a button.
 */
export function zoomAt(view: ImageView, scale: number, point: { x: number; y: number }, picture: Size, area: Size): ImageView {
  const next = Math.min(MAX_SCALE, Math.max(MIN_SCALE, scale))
  const ratio = next / view.scale
  return clampView(
    { scale: next, x: point.x - (point.x - view.x) * ratio, y: point.y - (point.y - view.y) * ratio },
    picture,
    area,
  )
}

/** Moves the picture by a pointer's or a key's travel. */
export function panBy(view: ImageView, dx: number, dy: number, picture: Size, area: Size): ImageView {
  return clampView({ ...view, x: view.x + dx, y: view.y + dy }, picture, area)
}

export function isZoomed(view: ImageView): boolean {
  return view.scale > MIN_SCALE
}
