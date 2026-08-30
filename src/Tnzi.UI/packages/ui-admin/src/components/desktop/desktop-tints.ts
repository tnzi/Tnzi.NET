/**
 * Per-app tint colours for the desktop shell.
 *
 * The desktop draws every page as an OS-style app tile - a rounded square
 * filled with a colour gradient - rather than the monochrome line glyph the
 * sidebar uses. That is not decoration: a sidebar shows ~10 rows at a time
 * inside a labelled tree, while the desktop puts every page the user can open
 * on one surface at once. Past about forty, identically-coloured glyphs stop
 * being scannable and the grid reads as a wall of text; colour is what brings
 * the module grouping back.
 *
 * ## Where the colour comes from
 *
 * The MODULE decides, and the menu tree decides the module: `useDesktopTint`
 * asks `useAdminRouteStore().menuModuleByRoute` which top-level entry a route
 * sits under. Route names look like they could answer it themselves - the
 * framework's own are dotted and rooted at the module (`identity.users`) - but
 * that is this package's convention, not a rule. Measured against a real
 * consumer app: its pages are named `shop-orders` / `shop-products`, sharing
 * no prefix at all while sitting under one menu entry, so the name-only rule
 * gave every page in the module a different colour.
 *
 * The name-derived root stays as the FALLBACK, for the routes the tree cannot
 * answer for: a detail page reached by id is `hideInMenu` and therefore absent
 * from the tree, and `identity.users.detail` still needs to look like Identity.
 *
 * Whatever the source, the answer is stable: it does not shift when the menu
 * shifts (assigning colours by menu index would repaint half the desktop the
 * moment a permission change removed a module) and it is the same for every
 * viewer, so two people comparing screenshots agree.
 *
 * Framework modules get a hand-picked hue so related things sit apart on the
 * wheel (identity purple next to authorization indigo, finance green next to
 * payment amber). Anything else falls back to a hash of the module key, which
 * is stable for the life of that key. A route can always override with
 * `meta.color`.
 */
import { useAdminRouteStore } from '../../stores/useAdminRouteStore'

/**
 * Fallback palette for modules with no hand-picked hue.
 *
 * Deliberately DISJOINT from the values in {@link DESKTOP_MODULE_TINTS} - a
 * consumer module landing on the framework's own purple puts two unrelated
 * modules in the same colour on one screen, which is exactly the confusion the
 * colouring exists to remove. (Measured: a consumer's blog module hashed onto
 * identity's violet, and the two blocks read as one.) `desktop-tints.test.ts`
 * asserts the disjointness so a future palette edit cannot quietly break it.
 *
 * The last two are deeper variants of hues the framework already uses: the
 * wheel is only so wide, and a clear lightness step reads as a different tile
 * where a 10-degree hue step would not.
 *
 * Values are the "top" of each tile gradient; {@link shadeTint} derives the
 * bottom.
 */
export const DESKTOP_TINT_PALETTE = [
  '#e11d48', // rose
  '#db2777', // pink
  '#c026d3', // fuchsia
  '#22c55e', // green
  '#eab308', // yellow
  '#78716c', // stone (warm grey - reads apart from the cool slate above)
  '#0369a1', // deep sky
  '#7e22ce', // deep purple
] as const

/**
 * Hand-picked tints for the modules this package ships routes for.
 *
 * Keyed by the top-level menu entry's route name (`identity`, `finance`, …),
 * which is also what the dotted-name fallback yields - so the tree lookup and
 * the fallback land on the same entry without a special case.
 */
export const DESKTOP_MODULE_TINTS: Record<string, string> = {
  dashboard: '#3b82f6',
  identity: '#8b5cf6',
  authorization: '#6366f1',
  system: '#64748b',
  settings: '#64748b',
  'user-center': '#8b5cf6',
  storage: '#0ea5e9',
  audit: '#475569',
  notification: '#ef4444',
  chat: '#06b6d4',
  payment: '#f59e0b',
  finance: '#10b981',
  payroll: '#84cc16',
  ai: '#a855f7',
  template: '#14b8a6',
  signing: '#f97316',
}

/** Tile used when a route resolves to nothing at all. */
const NEUTRAL_TINT = '#64748b'

/**
 * FNV-1a over the module key. Any stable string hash would do; this one is
 * short, has no dependencies, and spreads short lowercase words well enough
 * that the buckets stay visibly mixed.
 */
function hashKey(key: string): number {
  let hash = 0x811c9dc5
  for (let i = 0; i < key.length; i += 1) {
    hash ^= key.charCodeAt(i)
    hash = Math.imul(hash, 0x01000193)
  }
  return Math.abs(hash)
}

/** Fallback module derivation: everything before the first dot. */
export function rootModuleKey(routeName: string | undefined): string {
  if (!routeName) return ''
  const dot = routeName.indexOf('.')
  return dot === -1 ? routeName : routeName.slice(0, dot)
}

/**
 * Resolve the tint for a module key.
 *
 * Prefer {@link useDesktopTint} inside a component - it supplies the module key
 * from the menu tree. This is the pure half, and the one the tests drive.
 *
 * @param moduleKey Top-level menu key, or a route name to derive one from.
 * @param override  `meta.color` - an explicit per-route colour always wins.
 */
export function resolveDesktopTint(moduleKey: string | undefined, override?: string): string {
  if (override) return override
  const root = rootModuleKey(moduleKey)
  if (!root) return NEUTRAL_TINT
  const picked = DESKTOP_MODULE_TINTS[root]
  if (picked) return picked
  return DESKTOP_TINT_PALETTE[hashKey(root) % DESKTOP_TINT_PALETTE.length]!
}

/**
 * Component-side tint lookup: menu tree first, route name second.
 *
 * Returns a plain function rather than a computed because callers ask about a
 * different route on every row; the reactivity that matters (the tree
 * repopulating after permissions load) comes from reading the store computed
 * inside the caller's own render.
 */
export function useDesktopTint(): (routeName?: string, override?: string) => string {
  const routeStore = useAdminRouteStore()
  return (routeName, override) =>
    resolveDesktopTint(routeStore.moduleKeyOfRoute(routeName), override)
}

/**
 * Darken a hex tint toward the bottom of the tile gradient.
 *
 * Multiplying the channels rather than mixing toward black keeps the hue and
 * loses only value, which is what makes the tile read as one lit surface
 * instead of two colours stacked. Falls back to the input when handed
 * something that isn't `#rrggbb` (a consumer `meta.color` may be any CSS
 * colour), so the gradient degrades to a flat fill rather than throwing.
 */
export function shadeTint(tint: string, factor = 0.62): string {
  if (!/^#[0-9a-f]{6}$/i.test(tint)) return tint
  const n = Number.parseInt(tint.slice(1), 16)
  const r = Math.round(((n >> 16) & 255) * factor)
  const g = Math.round(((n >> 8) & 255) * factor)
  const b = Math.round((n & 255) * factor)
  return `rgb(${r}, ${g}, ${b})`
}

/**
 * Inline style for one app tile.
 *
 * Inline rather than a CSS class because the colour is per-item data, and a
 * class per module would mean shipping a stylesheet that has to be regenerated
 * whenever a consumer adds one. Size / radius stay parameters so the same tile
 * appears at 44px on the desktop, 26px on the taskbar and 18px in a title bar
 * without three near-copies of this function.
 */
export function desktopTileStyle(
  tint: string,
  size: number,
  radius: number,
): Record<string, string> {
  return {
    width: `${size}px`,
    height: `${size}px`,
    borderRadius: `${radius}px`,
    background: `linear-gradient(160deg, ${tint}, ${shadeTint(tint)})`,
    // Two shadows doing different jobs: the outer one lifts the tile off the
    // wallpaper, the inset highlight along the top edge is what makes it read
    // as a physical key rather than a coloured rectangle.
    boxShadow: '0 4px 12px rgb(0 0 0 / 22%), inset 0 1px 0 rgb(255 255 255 / 28%)',
  }
}
