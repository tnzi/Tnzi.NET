import { provide, inject, ref, computed, type Ref, type ComputedRef, type InjectionKey } from 'vue'
import { useAdminRouteStore, type AdminMenuItem } from '../stores/useAdminRouteStore'

export interface MixMenuContext {
  /** The currently highlighted first-level (top-bar) menu key. */
  activeFirstLevelKey: Ref<string>
  /** Second-level (sider) menus derived from the active first-level key. */
  secondLevelMenus: ComputedRef<AdminMenuItem[]>
  /** Set the active first-level menu key. */
  setActiveFirstLevelKey: (key: string) => void
}

const MixMenuContextKey: InjectionKey<MixMenuContext> = Symbol('TnziMixMenuContext')

/**
 * Provides a mix-menu context (top-bar first level + sider second level) to
 * descendant components. Intended for `TAdminMixLayout` and its children.
 */
export function provideMixMenuContext(): MixMenuContext {
  const routeStore = useAdminRouteStore()
  const activeFirstLevelKey = ref<string>('')

  const secondLevelMenus = computed<AdminMenuItem[]>(() => {
    const top = routeStore.menus.find((m) => m.key === activeFirstLevelKey.value)
    return top?.children ?? []
  })

  function setActiveFirstLevelKey(key: string): void {
    activeFirstLevelKey.value = key
  }

  const context: MixMenuContext = {
    activeFirstLevelKey,
    secondLevelMenus,
    setActiveFirstLevelKey,
  }

  provide(MixMenuContextKey, context)
  return context
}

/**
 * Injects the mix-menu context. Throws if no ancestor called
 * `provideMixMenuContext()`.
 */
export function injectMixMenuContext(): MixMenuContext {
  const context = inject(MixMenuContextKey, null)
  if (!context) {
    throw new Error(
      '[useMixMenuContext] No MixMenuContext found. Call provideMixMenuContext() in an ancestor component.',
    )
  }
  return context
}

/** Convenience alias preferred by consumer components. */
export function useMixMenuContext(): MixMenuContext {
  return injectMixMenuContext()
}
