import { ref, type Ref } from 'vue'

export interface UseAdminLayoutOptions {
  initialCollapsed?: boolean
  onCollapseChange?: (collapsed: boolean) => void
}

export interface UseAdminLayoutReturn {
  sidebarCollapsed: Ref<boolean>
  handleSidebarCollapse: (collapsed: boolean) => void
}

export function useAdminLayout(options: UseAdminLayoutOptions = {}): UseAdminLayoutReturn {
  const sidebarCollapsed = ref(options.initialCollapsed ?? false)

  function handleSidebarCollapse(collapsed: boolean): void {
    sidebarCollapsed.value = collapsed
    options.onCollapseChange?.(collapsed)
  }

  return {
    sidebarCollapsed,
    handleSidebarCollapse,
  }
}
