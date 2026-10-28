import { isNavigationFailure, NavigationFailureType, type Router, type RouteLocationRaw } from 'vue-router'
import type { AdminTab } from '../stores/useAdminTabStore'

/** The admin landing route: the shell's own route, which redirects to the dashboard. */
const LANDING_ROUTE = 'admin-root'

/**
 * The tab store's navigator for the admin router (`tabStore.bindNavigation`):
 * showing a tab navigates to its page, and `null` (no tab is left to show)
 * goes to the admin landing page.
 *
 * `retrack` puts the page on screen back in the tab bar. It runs when the page
 * to show is already the one on screen: Close all on the dashboard leaves the
 * bar empty while the route stays where it is, so the route watcher that adds
 * tabs never fires.
 */
export function createTabNavigator(router: Router, retrack: () => void): (tab: AdminTab | null) => Promise<void> {
  return async (tab) => {
    if (tab && tab.fullPath === router.currentRoute.value.fullPath) {
      retrack()
      return
    }
    const target: RouteLocationRaw = tab
      ? tab.fullPath
      : router.hasRoute(LANDING_ROUTE)
        ? { name: LANDING_ROUTE }
        : '/'
    // A guard that throws reports through the router's own onError; the bar
    // has nothing to add to that.
    const failure = await router.push(target).catch(() => undefined)
    if (isNavigationFailure(failure, NavigationFailureType.duplicated)) retrack()
  }
}
