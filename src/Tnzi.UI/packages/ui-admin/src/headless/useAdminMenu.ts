import { ref, computed, type Ref, type ComputedRef } from 'vue'

export interface AdminMenuItem {
  key: string
  label: string
  icon?: string
  path?: string
  children?: AdminMenuItem[]
  permission?: string
  hidden?: boolean
}

export interface UseAdminMenuOptions {
  items: AdminMenuItem[]
  hasPermission?: (permission: string) => boolean
}

export interface UseAdminMenuReturn {
  visibleItems: ComputedRef<AdminMenuItem[]>
  activeKey: Ref<string>
  openKeys: Ref<string[]>
  setActive: (key: string) => void
  toggleOpen: (key: string) => void
  findItemByPath: (path: string) => AdminMenuItem | undefined
}

function filterItems(items: AdminMenuItem[], hasPermission?: (p: string) => boolean): AdminMenuItem[] {
  return items
    .filter((item) => {
      if (item.hidden) return false
      if (item.permission && hasPermission && !hasPermission(item.permission)) return false
      return true
    })
    .map((item) => {
      if (!item.children) return item
      const filteredChildren = filterItems(item.children, hasPermission)
      return { ...item, children: filteredChildren }
    })
}

function findByPath(items: AdminMenuItem[], path: string): AdminMenuItem | undefined {
  for (const item of items) {
    if (item.path === path) return item
    if (item.children) {
      const found = findByPath(item.children, path)
      if (found) return found
    }
  }
  return undefined
}

export function useAdminMenu(options: UseAdminMenuOptions): UseAdminMenuReturn {
  const activeKey = ref('')
  const openKeys = ref<string[]>([])

  const visibleItems = computed(() => filterItems(options.items, options.hasPermission))

  function setActive(key: string): void {
    activeKey.value = key
  }

  function toggleOpen(key: string): void {
    if (openKeys.value.includes(key)) {
      openKeys.value = openKeys.value.filter((k) => k !== key)
    } else {
      openKeys.value = [...openKeys.value, key]
    }
  }

  function findItemByPath(path: string): AdminMenuItem | undefined {
    return findByPath(options.items, path)
  }

  return {
    visibleItems,
    activeKey,
    openKeys,
    setActive,
    toggleOpen,
    findItemByPath,
  }
}
