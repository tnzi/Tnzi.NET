import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import 'pinia-plugin-persistedstate'

export interface AdminTab {
  id: string
  title: string
  fullPath: string
  query?: Record<string, unknown>
  params?: Record<string, unknown>
  meta?: {
    keepAlive?: boolean
    fixedIndexInTab?: number
    multiTab?: boolean
    icon?: string
  }
}

interface RouteLike {
  name: string
  fullPath: string
  path: string
  query?: Record<string, unknown>
  params?: Record<string, unknown>
  meta?: {
    title?: string
    keepAlive?: boolean
    fixedIndexInTab?: number
    multiTab?: boolean
    icon?: string
  }
}

function routeToTab(route: RouteLike): AdminTab {
  // For multiTab routes, the id includes query so different queries create different tabs
  const baseId = route.name
  const hasQuery = route.query && Object.keys(route.query).length > 0
  const id =
    route.meta?.multiTab && hasQuery
      ? `${baseId}?${new URLSearchParams(route.query as Record<string, string>).toString()}`
      : baseId
  return {
    id,
    title: route.meta?.title ?? route.name,
    fullPath: route.fullPath,
    query: route.query,
    params: route.params,
    meta: {
      keepAlive: route.meta?.keepAlive,
      fixedIndexInTab: route.meta?.fixedIndexInTab,
      multiTab: route.meta?.multiTab,
      icon: route.meta?.icon,
    },
  }
}

export const useAdminTabStore = defineStore('admin-tab', () => {
  const tabs = ref<AdminTab[]>([])
  const activeTabId = ref<string>('')
  const homeTab = ref<AdminTab | null>(null)

  const allTabs = computed<AdminTab[]>(() => {
    return homeTab.value ? [homeTab.value, ...tabs.value] : [...tabs.value]
  })

  function findTab(id: string): AdminTab | undefined {
    return tabs.value.find((t) => t.id === id)
  }

  function addTab(route: RouteLike): void {
    const tab = routeToTab(route)
    const existing = findTab(tab.id)
    if (existing) {
      activeTabId.value = tab.id
      return
    }
    tabs.value.push(tab)
    activeTabId.value = tab.id
  }

  function setHomeTab(tab: AdminTab): void {
    homeTab.value = tab
  }

  function removeTab(id: string): string | null {
    const idx = tabs.value.findIndex((t) => t.id === id)
    if (idx < 0) return null
    tabs.value.splice(idx, 1)
    // If we removed the active one, switch to neighbor
    if (activeTabId.value === id) {
      const next = tabs.value[idx] || tabs.value[idx - 1]
      activeTabId.value = next?.id ?? (homeTab.value?.id ?? '')
      return activeTabId.value
    }
    return null
  }

  function removeLeftTabs(id: string): void {
    const idx = tabs.value.findIndex((t) => t.id === id)
    if (idx <= 0) return
    tabs.value = tabs.value.slice(idx)
  }

  function removeRightTabs(id: string): void {
    const idx = tabs.value.findIndex((t) => t.id === id)
    if (idx < 0 || idx >= tabs.value.length - 1) return
    tabs.value = tabs.value.slice(0, idx + 1)
  }

  function removeOtherTabs(id: string): void {
    const tab = findTab(id)
    if (!tab) return
    tabs.value = [tab]
    activeTabId.value = id
  }

  function clearAllTabs(): void {
    tabs.value = []
    activeTabId.value = homeTab.value?.id ?? ''
  }

  function moveTab(fromIndex: number, toIndex: number): void {
    if (fromIndex === toIndex) return
    if (fromIndex < 0 || fromIndex >= tabs.value.length) return
    if (toIndex < 0 || toIndex >= tabs.value.length) return
    const [moved] = tabs.value.splice(fromIndex, 1)
    if (!moved) return
    tabs.value.splice(toIndex, 0, moved)
  }

  function updateTabTitle(id: string, title: string): void {
    const tab = findTab(id)
    if (tab) tab.title = title
  }

  function setActiveTab(id: string): void {
    activeTabId.value = id
  }

  return {
    tabs,
    activeTabId,
    homeTab,
    allTabs,
    addTab,
    setHomeTab,
    removeTab,
    removeLeftTabs,
    removeRightTabs,
    removeOtherTabs,
    clearAllTabs,
    moveTab,
    updateTabTitle,
    setActiveTab,
    findTab,
  }
}, {
  persist: {
    key: 'tnzi-admin-tabs',
    pick: ['tabs', 'activeTabId'],
  },
})
