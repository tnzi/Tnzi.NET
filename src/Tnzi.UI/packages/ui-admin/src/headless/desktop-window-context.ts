import { hasInjectionContext, inject, provide, type InjectionKey } from 'vue'

/**
 * Id of the desktop window a subtree is rendering inside, or absent outside the
 * desktop layout.
 *
 * Exists because two facilities in this package write to **application-global,
 * single-instance** stores keyed by route: `useTabTitle` (tab store) and
 * `useBreadcrumbTrail` / `useBreadcrumbLabel` (breadcrumb store). Both key by
 * `multiInstanceKey(route)`, which is fine when only one page is mounted - and
 * wrong the moment two windows sit on the same route, because they then share
 * one key and overwrite each other.
 *
 * Note the direction of the failure: providing a per-window route made these two
 * *start working* (they no-op when no route is injectable), which is precisely
 * what surfaces the collision. So the window shell has to tell them where they
 * are, and each decides what "here" means for it.
 *
 * Kept in its own module so `useTabTitle` / `use-breadcrumb` can read it without
 * importing `useWindowRoute` and, through it, vue-router's injection keys and
 * the desktop store.
 */
export const DESKTOP_WINDOW_ID: InjectionKey<string> = Symbol('tnzi-desktop-window-id')

/** Called by the window shell. */
export function provideDesktopWindowId(id: string): void {
  provide(DESKTOP_WINDOW_ID, id)
}

/**
 * Id of the enclosing desktop window, or `null` outside one.
 *
 * Safe to call from anywhere: returns `null` rather than warning when there is
 * no injection context (a composable invoked outside `setup`, an isolated unit
 * test), matching how the facilities that read it already degrade.
 */
export function useDesktopWindowId(): string | null {
  if (!hasInjectionContext()) return null
  return inject(DESKTOP_WINDOW_ID, null)
}
