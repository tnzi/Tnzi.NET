import { computed, inject, type ComputedRef, type Ref } from 'vue'
import {
  darkTheme,
  lightTheme,
  type GlobalComponentConfig,
  type GlobalTheme,
  type GlobalThemeOverrides,
} from 'naive-ui'
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

/**
 * The size every button and form control inside an overlay falls back to.
 *
 * naive's own overlay chrome already renders this way - `useDialog` and
 * `NPopconfirm` hard-code `small` on their action buttons - but a control a
 * page drops into a shell's slot fell back to naive's global `medium`. The
 * size of a dialog's Cancel / Confirm therefore depended on whether its author
 * remembered `size="small"`, and across the admin pages roughly half did.
 * Admin dialogs are dense and the shells already scroll the body at 65vh;
 * `medium` spends that height on padding.
 */
export const OVERLAY_CONTROL_SIZE = 'small'

/**
 * The `componentOptions` entries that carry a `size` AND render as a form
 * control or a button. Everything else naive sizes through the same channel
 * (DataTable, Pagination, Tabs, Descriptions, Card, Tag, Space, Dropdown, …)
 * is deliberately left alone: a table in a drawer is not what the density ask
 * is about, and each of those has its own visual weight to reconsider.
 *
 * `NCheckboxGroup` / `NRadioGroup` are absent because naive never consults
 * `componentOptions` for them: a group outside any `NFormItem` resolves to
 * `medium` before its children get to look up `Checkbox` / `Radio`. Inside an
 * `NFormItem` (the normal case) the `Form` entry covers them.
 */
const OVERLAY_SIZED_COMPONENTS = [
  'Button',
  // `NFormItem.mergedSize` never returns undefined - a control inside one
  // takes the Form-level default and never sees its own entry below, so the
  // Form entry and the per-component entries are both needed: the former for
  // controls inside form items, the latter for bare ones (a footer button).
  'Form',
  'Input',
  'InputNumber',
  'Select',
  'DatePicker',
  'TimePicker',
  'Cascader',
  'TreeSelect',
  'AutoComplete',
  'Checkbox',
  'Radio',
  'Switch',
  'DynamicTags',
  'Mention',
  'ColorPicker',
  'InputOtp',
  'Transfer',
  'Rate',
] as const

type OverlaySizedComponent = (typeof OVERLAY_SIZED_COMPONENTS)[number]

/**
 * naive's ConfigProvider injection, the one slice read here. naive exports
 * neither the key nor the type; the key is the plain string its
 * `createInjectionKey('n-config-provider')` returns (it hands back its
 * argument verbatim). A naive upgrade that changes it would not throw, it
 * would make every overlay REPLACE the app's root `componentOptions` instead
 * of extending them - which is why a test plants a root-level entry and reads
 * it back through a shell.
 */
const NAIVE_CONFIG_PROVIDER_KEY = 'n-config-provider'

interface NaiveConfigProviderInjection {
  mergedComponentPropsRef: Ref<GlobalComponentConfig | undefined>
}

/** The inherited entry with `size` filled in where the app left it unset. */
function withOverlaySize<K extends OverlaySizedComponent>(
  inherited: GlobalComponentConfig,
  key: K,
): NonNullable<GlobalComponentConfig[K]> {
  const entry = inherited[key]
  return { ...entry, size: entry?.size ?? OVERLAY_CONTROL_SIZE } as NonNullable<
    GlobalComponentConfig[K]
  >
}

/**
 * `componentOptions` for the same overlay provider: every button and form
 * control rendered inside the overlay defaults to {@link OVERLAY_CONTROL_SIZE}.
 *
 * Resolution stays naive's: an explicit `size` on the control, on an enclosing
 * `NForm` / `NFormItem`, or on the app's ROOT `componentOptions` all still win
 * - this only fills the holes, so a consumer who sized the whole app on
 * purpose keeps that size in overlays too, and one medium modal is one
 * `size="medium"` on its `NForm`.
 *
 * A nested `NConfigProvider`'s `componentOptions` REPLACES the inherited one
 * (naive falls back to the parent only when the prop is undefined), so the
 * inherited object is read back through naive's own injection and extended,
 * not overwritten: a root-level `Select.renderEmpty` or `DataTable.size` still
 * reaches the overlay.
 */
export function useOverlayComponentOptions(): ComputedRef<GlobalComponentConfig> {
  const naiveConfig = inject<NaiveConfigProviderInjection | null>(NAIVE_CONFIG_PROVIDER_KEY, null)
  return computed(() => {
    const inherited = naiveConfig?.mergedComponentPropsRef.value ?? {}
    const sized = Object.fromEntries(
      OVERLAY_SIZED_COMPONENTS.map((key) => [key, withOverlaySize(inherited, key)]),
    ) as Pick<GlobalComponentConfig, OverlaySizedComponent>
    // No `DynamicInput.buttonSize` entry: its add / remove buttons are plain
    // `NButton`s that fall through to the `Button` / `Form` entries above
    // (verified), and a separate fill would let them disagree with every
    // other button once an app sizes `Button` at the root.
    return { ...inherited, ...sized }
  })
}
