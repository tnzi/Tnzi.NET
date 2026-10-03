/**
 * `useAdminShellConfig()` - install-time configuration for the admin *chrome*
 * (the frame around the pages), as opposed to the per-page configs
 * (`dashboard`, `settings`, `userCenter`) that configure one route.
 *
 * It carries the host's own chrome actions and the sidebar's menu-group style.
 *
 * ## Why this exists
 *
 * A host app frequently has one small, high-traffic, app-level utility - the
 * kind of thing that is a verb, not a noun. Before this option the only homes
 * for it were a top-level sidebar nav route (which puts a verb in a list of
 * nouns and gives a one-click utility the visual weight of a whole module) or
 * the single `login.headerNotification` slot (which is spoken for the moment
 * the app has a real notification bell).
 *
 * ## Where they render
 *
 * Exactly where the built-in Settings gear renders, and for the same reason:
 * `useSettingsEntry` already establishes that these are *shell-level*
 * affordances that follow the layout's persistent utility strip rather than
 * living in one component. Concretely that is the sidebar's bottom footer
 * (`TSidebarSettingsFooter`, shared by the full sider, the collapsed rail, the
 * vertical-mix rail, the hybrid sider and the mobile drawer) and the desktop
 * layout's start-menu footer - which is the layout mode that has no sider at
 * all, and where the built-ins had to be re-homed for the same reason.
 *
 * `horizontal` mode renders no utility strip at all, so it drops the built-in
 * Settings entry too; host actions are at parity with the built-ins, not worse
 * off than them.
 *
 * @example
 * ```ts
 * defineAdminApp({
 *   client: http,
 *   shell: {
 *     actions: [
 *       {
 *         key: 'returned-scans',
 *         // 你自己的 Iconify 图标名，形如 `mdi:inbox-arrow-down-outline`。
 *         // ★ 这里刻意不写成真实的 `'集合:名字'` 字面量：图标门禁扫的是源码里
 *         //   所有引号包住的那个形态、认不出这是注释，于是一个文档示例会被算成
 *         //   「框架渲染了它」，进而要求它出现在生成清单里（而框架并不渲染它）。
 *         icon: '<your iconify name>',
 *         label: () => t('scans.inbox'),
 *         show: () => can('scans.file'),
 *         onClick: () => openScanInbox(),
 *       },
 *     ],
 *   },
 * })
 * ```
 */
import type { App, InjectionKey } from 'vue'
import { inject } from 'vue'
import type { AdminMenuGroupStyle } from '../components/layout/menu-groups'

/**
 * One host-contributed action in the admin chrome.
 *
 * Rendered as an **icon-only** button sitting beside the framework's own
 * chrome buttons - it never grows a visible label, because the strip it joins
 * is a row of equally-weighted glyphs and one labelled outsider would read as
 * a different kind of thing.
 *
 * Deliberately NOT unified with `TAdminHeader`'s internal action row: that row
 * folds into an overflow dropdown under a width budget (`maxInlineActions`,
 * `overflowMenuBreakpoint`), so a shared contract would have to answer "what
 * happens to this action when it overflows" for a strip that has no overflow.
 * If a header contribution point is ever added, it can reuse this shape and
 * pair it with its own config key.
 */
export interface AdminChromeAction {
  /** Stable identity - the `v-for` key. Must be unique within the list. */
  key: string
  /** Iconify icon name, e.g. `mdi:inbox-arrow-down-outline` (backticks keep it out of the icon gate's scan). */
  icon: string
  /**
   * Hover / accessible-name text. Pass a getter (rather than a plain string)
   * when it has to follow the active locale - the config object itself is
   * captured once at install time and never re-read.
   */
  label: string | (() => string)
  /**
   * Whether to render the action at all. Omitted = always. Evaluated inside a
   * `computed`, so a predicate reading reactive state (a permission store, a
   * feature flag) makes the button appear and disappear on its own - which is
   * what a per-user permission gate needs.
   */
  show?: boolean | (() => boolean)
  /**
   * Paint the action with the strip's primary "on" tint. Same evaluation rules
   * as {@link show}. Omitted = never active.
   */
  active?: boolean | (() => boolean)
  /** Click handler. Exceptions are NOT swallowed - a broken handler should be visible. */
  onClick: () => void
}

export interface AdminShellConfig {
  /**
   * Host actions appended to the chrome's built-in action strip, AFTER the
   * framework's own entries (Settings, and the super-admin built-in-menus
   * toggle) rather than instead of them. An empty / omitted list renders
   * nothing and changes nothing.
   */
  actions?: AdminChromeAction[]
  /**
   * How the sidebars render a menu entry that has children at their first
   * level. `caption` (default): a small heading with the children listed flat
   * beneath it, the whole menu visible at once. `submenu`: a collapsible
   * submenu the user opens and closes. Applies to every vertical sider (the
   * full sidebar, the vertical-mix and hybrid child siders, the mobile
   * drawer); the horizontal top menu keeps its dropdowns either way.
   */
  menuGroups?: AdminMenuGroupStyle
}

export const ADMIN_SHELL_CONFIG_KEY: InjectionKey<AdminShellConfig> = Symbol(
  'tnzi-admin-shell-config',
)

export function provideAdminShellConfig(app: App, config: AdminShellConfig): void {
  app.provide(ADMIN_SHELL_CONFIG_KEY, config)
}

/**
 * Inject the consumer-supplied shell config. Returns an empty object when no
 * `defineAdminApp({ shell: … })` was passed, so every read site gets a stable
 * shape without null checks.
 */
export function useAdminShellConfig(): AdminShellConfig {
  return inject(ADMIN_SHELL_CONFIG_KEY, {})
}
