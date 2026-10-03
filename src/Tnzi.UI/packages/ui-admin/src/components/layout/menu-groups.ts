import type { MenuGroupOption, MenuOption } from 'naive-ui'

/** What `NMenu.options` accepts: naive does not export the union itself. */
export type SidebarMenuOption = MenuOption | MenuGroupOption

/**
 * How a sidebar renders an entry that has children at its first level.
 *
 * - `caption`: the entry becomes a section heading (small muted caption) with
 *   its children listed flat beneath it. The whole menu is visible at once;
 *   nothing to expand.
 * - `submenu`: the entry is a collapsible submenu (naive's default), opened
 *   and closed by the user, auto-opened for the active route.
 *
 * Only the first level of the menu a sidebar shows is affected: in the full
 * sidebar that is the module level; in a sider that shows one module's pages
 * (vertical-mix, top-hybrid) it is that module's own sub-groups. Deeper
 * levels keep their submenus in both styles.
 */
export type AdminMenuGroupStyle = 'caption' | 'submenu'

export const DEFAULT_MENU_GROUP_STYLE: AdminMenuGroupStyle = 'caption'

/**
 * Turn every first-level option that has children into a naive `group` (a
 * heading with its children flat beneath it) instead of a submenu. A heading
 * has neither an icon nor a trailing badge, so a parent's own are dropped;
 * its children keep theirs.
 */
export function captionGroups(options: MenuOption[]): SidebarMenuOption[] {
  return options.map((option) =>
    Array.isArray(option.children) && option.children.length > 0
      ? ({ type: 'group', key: option.key, label: option.label, children: option.children } satisfies MenuGroupOption)
      : option,
  )
}
