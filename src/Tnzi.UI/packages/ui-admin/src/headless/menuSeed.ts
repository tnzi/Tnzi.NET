import type { AdminMenuItem } from '../stores/useAdminRouteStore'
import type { CreateMenuDto } from '@tnzi/core/services/system'
import { MenuType } from '@tnzi/core/services/system'

/**
 * Flatten a route-derived menu tree into a flat list of `CreateMenuDto` seed
 * rows, keyed by route name (`menuKey`). Used by `defineAdminApp.seedMenus()` to
 * mirror the front-end menu into editable `Sys_Menu` rows when first enabling the
 * 'merge' source — the backend `POST /admin/menus/seed` then upserts them
 * (inserting missing keys, skipping existing ones so operator edits survive).
 *
 * The seed is intentionally FLAT (no parent linkage): merge overrides match
 * routes by `menuKey` only — the route table owns the hierarchy, so a seed row's
 * job is just to carry the per-entry override fields (name / icon / order).
 */
export function exportRouteMenuSeed(menus: AdminMenuItem[]): CreateMenuDto[] {
  const out: CreateMenuDto[] = []
  function walk(items: AdminMenuItem[]): void {
    for (const item of items) {
      out.push({
        menuKey: item.key,
        name: item.label,
        icon: item.icon ?? undefined,
        sortOrder: item.meta?.order,
        // Directory (has children) vs leaf — keeps Type meaningful for a future
        // Menus.vue editor; merge itself matches purely by menuKey.
        type: item.children && item.children.length > 0 ? MenuType.Directory : MenuType.Menu,
        isHidden: false,
      })
      if (item.children && item.children.length > 0) walk(item.children)
    }
  }
  walk(menus)
  return out
}
