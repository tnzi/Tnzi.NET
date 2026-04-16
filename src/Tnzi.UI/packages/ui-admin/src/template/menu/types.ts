/**
 * Legacy flat menu item shape used by the built-in `createAdminApp` template.
 *
 * Note: Task 2.19 introduced a route-store-derived `AdminMenuItem` in
 * `stores/useAdminRouteStore.ts` (required `path`, `meta`-based permissions).
 * This file preserves the original template shape (optional `path`, singular
 * `permission`, `hidden`) so `defaultAdminMenus` and `createAdminApp` keep
 * working. Consumer apps migrating to route-driven menus should use the
 * store type instead.
 */
export interface AdminMenuItem {
  key: string
  label: string
  icon?: string
  path?: string
  children?: AdminMenuItem[]
  permission?: string
  hidden?: boolean
}
