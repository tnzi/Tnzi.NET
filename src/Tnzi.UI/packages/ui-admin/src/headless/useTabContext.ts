import { provide, inject, type InjectionKey } from 'vue'
import { useAdminTabStore } from '../stores/useAdminTabStore'

export interface TabContext {
  /** Switch to the configured home tab. */
  goHome: () => void
  /** Close the currently active tab (no-op when pointing at the home tab). */
  closeCurrent: () => void
}

const TabContextKey: InjectionKey<TabContext> = Symbol('TnziTabContext')

/**
 * Provides a tab context with convenience helpers that delegate to
 * `useAdminTabStore`. Intended for admin page layouts that embed nested
 * content needing simple tab operations without a direct store dependency.
 */
export function provideTabContext(): TabContext {
  const tabStore = useAdminTabStore()

  function goHome(): void {
    const home = tabStore.homeTab
    if (home) tabStore.switchRouteByTab(home)
  }

  function closeCurrent(): void {
    const activeId = tabStore.activeTabId
    if (!activeId) return
    if (tabStore.homeTab && tabStore.homeTab.id === activeId) return
    tabStore.removeTab(activeId)
  }

  const context: TabContext = { goHome, closeCurrent }
  provide(TabContextKey, context)
  return context
}

/**
 * Injects the tab context. Throws if no ancestor called `provideTabContext()`.
 */
export function injectTabContext(): TabContext {
  const context = inject(TabContextKey, null)
  if (!context) {
    throw new Error(
      '[useTabContext] No TabContext found. Call provideTabContext() in an ancestor component.',
    )
  }
  return context
}

/** Convenience alias preferred by consumer components. */
export function useTabContext(): TabContext {
  return injectTabContext()
}
