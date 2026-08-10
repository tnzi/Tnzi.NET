import { computed, inject, type ComputedRef } from 'vue'
import { darkTheme, lightTheme, type GlobalTheme, type GlobalThemeOverrides } from 'naive-ui'
import { THEME_CONTEXT_KEY, type ThemeContext } from './useTheme'

/**
 * Naive theme for teleported overlays (modals / drawers) so they track the
 * GLOBAL light/dark mode instead of inheriting the content area's per-surface
 * "Card / List" theme.
 *
 * The content area (`TAdminContent`) wraps pages in an inner `NConfigProvider`
 * that switches to naive's dark base when the card surface is dark, so inputs /
 * borders / buttons on a dark card auto-match. naive forwards that theme through
 * provide/inject ACROSS the Teleport, so a modal / drawer opened from such a
 * page would otherwise render dark even under global light mode - inconsistent
 * with shell-level overlays (theme drawer, chat), which are rendered outside the
 * content and already follow the global mode.
 *
 * Wrapping the overlay in an `abstract` `NConfigProvider` with this theme resets
 * it: content surfaces stay a content-area concern; overlays are chrome and
 * track the global mode. Falls back to the light base when no theme context is
 * provided (e.g. isolated component tests).
 */
export function useOverlayTheme(): ComputedRef<GlobalTheme | null> {
  const themeCtx = inject<ThemeContext | undefined>(THEME_CONTEXT_KEY, undefined)
  return computed(() => (themeCtx?.isDark.value ? darkTheme : null))
}

/**
 * Companion overrides for the same overlay provider. Resetting `:theme` alone
 * is not enough: naive's ConfigProvider still INHERITS the parent's merged
 * `themeOverrides` when the prop is undefined, so the content area's "Card /
 * List" repaint (`TAdminContent.innerOverrides` - Card.color / DataTable
 * td/thColor painted to the custom card color) would leak into the overlay and
 * render a dark card / dark table inside a light modal. Passing `null` is no
 * fix either - it stops the WHOLE inheritance chain, dropping the app's
 * primary color / radius overrides too.
 *
 * So this pins exactly the keys TAdminContent overrides back to the naive
 * defaults of the overlay's own mode (from light/darkTheme.common - no
 * hardcoded hex), while everything else (primary, radius, …) keeps
 * inheriting through the deep merge.
 */
export function useOverlayThemeOverrides(): ComputedRef<GlobalThemeOverrides> {
  const themeCtx = inject<ThemeContext | undefined>(THEME_CONTEXT_KEY, undefined)
  return computed(() => {
    const common = themeCtx?.isDark.value ? darkTheme.common : lightTheme.common
    return {
      Card: {
        color: common.cardColor,
        colorEmbedded: common.actionColor,
        textColor: common.textColor2,
        titleTextColor: common.textColor1,
      },
      DataTable: {
        tdColor: common.cardColor,
        thColor: common.tableHeaderColor,
        tdTextColor: common.textColor2,
        thTextColor: common.textColor1,
      },
    }
  })
}
