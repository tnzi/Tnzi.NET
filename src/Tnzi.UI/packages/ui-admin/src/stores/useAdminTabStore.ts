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
  // Phase G — pinned (fixed) tab IDs. soybean's `tabStore.fixTab/unfixTab/
  // isTabRetain` powers the right-click context menu's "Pin / Unpin" item
  // and prevents pinned tabs from being closed (close button is hidden,
  // close-others / close-left / close-right skip them).
  const fixedTabIds = ref<string[]>([])

  const allTabs = computed<AdminTab[]>(() => {
    return homeTab.value ? [homeTab.value, ...tabs.value] : [...tabs.value]
  })

  function findTab(id: string): AdminTab | undefined {
    return tabs.value.find((t) => t.id === id)
  }

  // Phase G — pin/unpin/isTabRetain. `isTabRetain` is true for both the
  // home tab and any pinned tab; close-related ops should skip these.
  function isTabPinned(id: string): boolean {
    return fixedTabIds.value.includes(id)
  }
  function isTabRetain(id: string): boolean {
    if (homeTab.value && homeTab.value.id === id) return true
    return isTabPinned(id)
  }
  function fixTab(id: string): void {
    if (!findTab(id)) return
    if (fixedTabIds.value.includes(id)) return
    fixedTabIds.value = [...fixedTabIds.value, id]
  }
  function unfixTab(id: string): void {
    fixedTabIds.value = fixedTabIds.value.filter((x) => x !== id)
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
    // Phase G: refuse to remove pinned tabs (or the home tab).
    if (isTabRetain(id)) return null
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
    // Phase G: keep pinned tabs that fall in the to-remove range.
    const removed = tabs.value.slice(0, idx)
    const survivors = removed.filter((t) => isTabPinned(t.id))
    tabs.value = [...survivors, ...tabs.value.slice(idx)]
  }

  function removeRightTabs(id: string): void {
    const idx = tabs.value.findIndex((t) => t.id === id)
    if (idx < 0 || idx >= tabs.value.length - 1) return
    const survivors = tabs.value.slice(idx + 1).filter((t) => isTabPinned(t.id))
    tabs.value = [...tabs.value.slice(0, idx + 1), ...survivors]
  }

  function removeOtherTabs(id: string): void {
    const tab = findTab(id)
    if (!tab) return
    // Phase G: keep pinned tabs alongside the chosen one.
    const pinnedOthers = tabs.value.filter((t) => t.id !== id && isTabPinned(t.id))
    tabs.value = [...pinnedOthers, tab]
    activeTabId.value = id
  }

  function clearAllTabs(): void {
    // Phase G: pinned tabs are preserved across "close all".
    tabs.value = tabs.value.filter((t) => isTabPinned(t.id))
    if (!tabs.value.find((t) => t.id === activeTabId.value)) {
      activeTabId.value = homeTab.value?.id ?? (tabs.value[0]?.id ?? '')
    }
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

  // Presentation stub — Task 2.29 router guard will replace this with real navigation.
  // Keeping it on the store lets TAdminTabs stay router-agnostic and unit-testable.
  function switchRouteByTab(tab: AdminTab): void {
    activeTabId.value = tab.id
  }

  return {
    tabs,
    activeTabId,
    homeTab,
    fixedTabIds,
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
    switchRouteByTab,
    findTab,
    isTabPinned,
    isTabRetain,
    fixTab,
    unfixTab,
  }
}, {
  persist: {
    key: 'tnzi-admin-tabs',
    pick: ['tabs', 'activeTabId', 'fixedTabIds'],
  },
})
