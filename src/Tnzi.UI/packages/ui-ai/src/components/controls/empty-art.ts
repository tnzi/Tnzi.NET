/**
 * The drawings `TEmptyArt` has: one per kind of thing a page holds.
 * A page whose kind is not here draws its own in `TEmptyState`'s `#art` slot.
 */
export const EMPTY_ART_NAMES = [
  'stopwatch',
  'history',
  'blocks',
  'apps',
  'store',
  'project',
  'device',
  'folder',
  'trash',
  'library',
  'note',
  'bell',
  'mail',
] as const

export type EmptyArt = (typeof EMPTY_ART_NAMES)[number]
