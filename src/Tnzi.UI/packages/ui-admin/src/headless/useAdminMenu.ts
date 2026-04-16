import { ref, computed } from 'vue'
import { useAdminRouteStore, type AdminMenuItem } from '../stores/useAdminRouteStore'

/**
 * Admin menu composable — resolves the active menu item and its parent chain
 * from a key against the route store's derived menu tree.
 *
 * Menus themselves live on `useAdminRouteStore.menus`; this composable owns
 * only the presentation-level "which key is active" state and the flat index
 * used for fast lookups / breadcrumb rendering.
 */
export function useAdminMenu() {
  const routeStore = useAdminRouteStore()
  const activeKey = ref<string>('')

  const flatIndex = computed(() => {
    const map = new Map<string, { menu: AdminMenuItem; parents: AdminMenuItem[] }>()
    function walk(list: AdminMenuItem[], parents: AdminMenuItem[]): void {
      for (const m of list) {
        const chain = [...parents, m]
        map.set(m.key, { menu: m, parents: chain })
        if (m.children && m.children.length > 0) walk(m.children, chain)
      }
    }
    walk(routeStore.menus, [])
    return map
  })

  const activeMenu = computed<AdminMenuItem | null>(
    () => flatIndex.value.get(activeKey.value)?.menu ?? null,
  )

  const parentChain = computed<AdminMenuItem[]>(
    () => flatIndex.value.get(activeKey.value)?.parents ?? [],
  )

  return {
    activeKey,
    activeMenu,
    parentChain,
  }
}
