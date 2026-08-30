/**
 * Shared vnode builders for navigation count badges.
 *
 * Sits beside the layout components (like `back-target.ts`) rather than in
 * `utils/` because it produces vnodes; the display RULE it defers to is the
 * pure `normalizeNavBadge`. Five surfaces paint the same chip - the sidebar
 * expanded, the sidebar collapsed, the vertical-mix rail, the top menu and
 * the detail section nav - so it is built in exactly one place or the five
 * drift apart.
 *
 * Two shapes, because naive hides a menu row's trailing `extra` region the
 * moment the menu collapses (`.n-menu--collapsed .n-menu-item-content-header
 * { opacity: 0 }`), which is precisely where the count matters most:
 *   - expanded row   -> {@link navBadgeExtra}, an inline chip at the row's trailing edge
 *   - collapsed rail -> {@link navBadgeIcon}, the same chip riding the icon's corner
 *
 * The two shapes use different machinery, and that is deliberate:
 *   - the CORNER chip is `NBadge`, which is exactly what it is built for (and
 *     what `THeaderBell` / `TTabBar` / `TChatLauncher` already use);
 *   - the INLINE chip is our own `.t-nav-badge` span, because a slotless
 *     `NBadge` renders a `<sup>` sized for that corner - it inherits the UA's
 *     `vertical-align: super` (naive resets the font size but not this), is
 *     18px tall against a 14px label, and its `display: flex` swallows the
 *     space naive emits ahead of the extra region. None of the three is
 *     reachable through a prop; all three are wrong for a row.
 *
 * Both draw on the same colour (`--tnzi-error`, which is the framework's copy
 * of the value naive's badge theme resolves to), so the two states of one row
 * do not read as two different features. Styles for the inline chip live in
 * `styles/polish.css` - it renders inside naive's own DOM, which scoped
 * styles cannot reach.
 */
import { h, type VNodeChild } from 'vue'
import { NBadge } from 'naive-ui'
import { normalizeNavBadge, type NavBadgeValue } from '../../utils/nav-badge'

/**
 * Trailing chip for an EXPANDED nav row (naive `MenuOption.extra`, a tab
 * label) - inline, on the label's optical line, one step smaller than it.
 *
 * Returns `undefined` when there is nothing to show so the caller leaves
 * `extra` unset and naive renders no extra region at all, rather than an
 * empty wrapper plus naive's leading space.
 */
export function navBadgeExtra(value: NavBadgeValue): (() => VNodeChild) | undefined {
  const text = normalizeNavBadge(value)
  if (text === null) return undefined
  // Already capped by `normalizeNavBadge` - no `max` here, one rule one place.
  return () => h('span', { class: 't-nav-badge' }, text)
}

/**
 * Icon render function with the chip on its top-right corner - for the
 * collapsed rails, where the trailing `extra` region is invisible. Stays on
 * `NBadge`: a corner overlay on an icon is the shape it is built for.
 *
 * `icon` is optional: an icon-less entry collapses to a blank row today, so a
 * lone chip is strictly more informative than what it replaces. Returns `icon`
 * untouched when there is nothing to show, so a badge-less entry keeps exactly
 * the vnode it had before.
 */
export function navBadgeIcon(
  value: NavBadgeValue,
  icon?: () => VNodeChild,
): (() => VNodeChild) | undefined {
  const text = normalizeNavBadge(value)
  if (text === null) return icon
  return () => h(NBadge, { value: text }, icon ? { default: icon } : undefined)
}
