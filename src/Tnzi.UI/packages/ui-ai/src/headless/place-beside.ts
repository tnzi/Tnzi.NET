/**
 * Where a floating surface goes beside the element it belongs to: a tooltip
 * next to an icon, a flyout next to a rail button.
 *
 * Pure geometry in viewport coordinates, so it can be tested without layout.
 * The surface goes on the preferred side when it fits there, on the opposite
 * side when only that one fits, and is then kept inside the viewport with an
 * `edge` margin on every side.
 */

/** The side of the anchor the surface opens on. */
export type FloatingSide = 'right' | 'left' | 'top' | 'bottom'

/**
 * How the surface lines up with the anchor along that side: `center` on the
 * anchor's middle (a tooltip), `start` with its top or left edge (a flyout
 * whose first row continues the button that opened it), `end` with its bottom
 * or right edge (a popover from a button at the right of a toolbar).
 */
export type FloatingAlign = 'center' | 'start' | 'end'

export interface FloatingRect {
  readonly top: number
  readonly left: number
  readonly width: number
  readonly height: number
}

export interface PlaceBesideOptions {
  readonly side?: FloatingSide
  readonly align?: FloatingAlign
  /** Space between the anchor and the surface. */
  readonly gap?: number
  /** Least space kept between the surface and the viewport's edges. */
  readonly edge?: number
}

export interface FloatingPosition {
  readonly top: number
  readonly left: number
  /** The side used, which is the opposite one when the preferred one had no room. */
  readonly side: FloatingSide
}

const OPPOSITE: Record<FloatingSide, FloatingSide> = {
  right: 'left',
  left: 'right',
  top: 'bottom',
  bottom: 'top',
}

function clamp(value: number, min: number, max: number): number {
  return Math.max(min, Math.min(value, Math.max(min, max)))
}

export function placeBeside(
  anchor: FloatingRect,
  size: { readonly width: number; readonly height: number },
  viewport: { readonly width: number; readonly height: number },
  options: PlaceBesideOptions = {},
): FloatingPosition {
  const preferred = options.side ?? 'right'
  const align = options.align ?? 'center'
  const gap = options.gap ?? 8
  const edge = options.edge ?? 8

  const mainStart = (side: FloatingSide): number => {
    switch (side) {
      case 'right': return anchor.left + anchor.width + gap
      case 'left': return anchor.left - gap - size.width
      case 'bottom': return anchor.top + anchor.height + gap
      case 'top': return anchor.top - gap - size.height
    }
  }
  const fits = (side: FloatingSide): boolean => {
    const start = mainStart(side)
    return side === 'right' || side === 'left'
      ? start >= edge && start + size.width <= viewport.width - edge
      : start >= edge && start + size.height <= viewport.height - edge
  }

  const side = fits(preferred) || !fits(OPPOSITE[preferred]) ? preferred : OPPOSITE[preferred]
  const horizontal = side === 'right' || side === 'left'

  if (horizontal) {
    const top = align === 'start'
      ? anchor.top
      : align === 'end' ? anchor.top + anchor.height - size.height : anchor.top + (anchor.height - size.height) / 2
    return {
      side,
      left: clamp(mainStart(side), edge, viewport.width - size.width - edge),
      top: clamp(top, edge, viewport.height - size.height - edge),
    }
  }
  const left = align === 'start'
    ? anchor.left
    : align === 'end' ? anchor.left + anchor.width - size.width : anchor.left + (anchor.width - size.width) / 2
  return {
    side,
    top: clamp(mainStart(side), edge, viewport.height - size.height - edge),
    left: clamp(left, edge, viewport.width - size.width - edge),
  }
}
