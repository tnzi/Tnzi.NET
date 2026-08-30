import type { Router } from 'vue-router'
import { humanise, translatePageKey } from '../../i18n/translate'
import { DEFAULT_ROUTE_ICONS } from '../../router/route-icons'
import type { AdminMenuItem } from '../../stores/useAdminRouteStore'
import { routeToWindowInput, type OpenWindowInput } from '../../stores/useAdminDesktopStore'

/** Glyph used when a route has neither `meta.icon` nor a default-map entry. */
export const FALLBACK_ROUTE_ICON = 'mdi:file-document-outline'

/**
 * Resolve a route's display title, in the same order the tab bar and page
 * header use: consumer translator → bundled locale → humanised key.
 *
 * Window titles hold the raw `meta.title` (an i18n key for built-in pages) so
 * that switching language relabels open windows instead of freezing whatever
 * the language was when the window opened.
 */
export function resolveWindowTitle(
  raw: string,
  translate?: (key: string, fallback?: string) => string,
): string {
  if (translate) {
    const translated = translate(raw)
    if (translated && translated !== raw) return translated
  }
  const bundled = translatePageKey('', raw)
  if (bundled && bundled !== raw) return bundled
  return humanise(raw)
}

/** Resolve a route's glyph: explicit icon → shared default map → fallback. */
export function resolveWindowIcon(icon: string | undefined, routeName?: string): string {
  if (icon) return icon
  return (routeName ? DEFAULT_ROUTE_ICONS[routeName] : undefined) ?? FALLBACK_ROUTE_ICON
}

/**
 * The first page that can actually be opened under a menu node, depth-first.
 *
 * A branch node is a grouping label with no page behind it, so opening a module
 * has to mean opening something inside it. Depth-first in menu order, so it is
 * the entry the sidebar would put at the top - the same page the vertical
 * layout's `autoSelectFirstMenu` lands on.
 *
 * Returns null for a branch whose every descendant is a label (nothing to open)
 * - callers should not render an icon for it at all.
 */
export function firstOpenableLeaf(item: AdminMenuItem): AdminMenuItem | null {
  if (!item.children?.length) return item.path ? item : null
  for (const child of item.children) {
    const found = firstOpenableLeaf(child)
    if (found) return found
  }
  return null
}

/**
 * Turn a menu entry into the shape the desktop store opens windows with.
 *
 * Resolving the path through the router rather than trusting `item.path` keeps
 * a consumer's `basePath` honoured; the fallback covers a consumer route
 * registered without a name, which `resolve({ name })` throws on.
 *
 * The title stays RAW (`meta.title` is an i18n key for built-in pages) so that
 * switching language relabels windows that are already open, instead of
 * freezing whatever the language was when the window opened.
 */
export function menuItemToWindowInput(item: AdminMenuItem, router: Router): OpenWindowInput {
  try {
    const resolved = router.resolve({ name: item.key })
    // The REAL route record, not the menu tree's copy: the tree's meta goes
    // through a whitelist walk, and a field that walk forgets is invisible here.
    return routeToWindowInput(
      { name: item.key, path: resolved.path, meta: resolved.meta as Record<string, unknown> },
      item.label,
    )
  } catch {
    // Consumer route registered without a name - fall back to the menu entry.
    return routeToWindowInput(
      { name: item.key, path: item.path, meta: item.meta as Record<string, unknown> | undefined },
      item.label,
    )
  }
}
