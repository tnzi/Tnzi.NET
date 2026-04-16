import { computed, ref, watch, type Ref, type ComputedRef } from 'vue'

export interface ColumnDef {
  key: string
  title: string
  visible?: boolean
  width?: number
  fixed?: 'left' | 'right'
}

export interface UseColumnSettingsOptions {
  pageId: string
  columns: ColumnDef[]
  storageKey?: string
}

export interface UseColumnSettingsReturn {
  visibleColumns: ComputedRef<ColumnDef[]>
  orderedKeys: Ref<string[]>
  hiddenKeys: Ref<Set<string>>
  hide: (key: string) => void
  show: (key: string) => void
  toggle: (key: string) => void
  reorder: (newKeys: string[]) => void
  reset: () => void
}

interface PersistedState {
  hidden: string[]
  order: string[]
}

export function useColumnSettings(options: UseColumnSettingsOptions): UseColumnSettingsReturn {
  const { pageId, columns } = options
  const storageKey = options.storageKey ?? `tnzi-admin:cols:${pageId}`

  const columnMap = new Map(columns.map((c) => [c.key, c]))
  const originalOrder = columns.map((c) => c.key)
  const defaultHidden = new Set(columns.filter((c) => c.visible === false).map((c) => c.key))

  const orderedKeys = ref<string[]>([...originalOrder])
  const hiddenKeys = ref<Set<string>>(new Set(defaultHidden))

  // Attempt restore from storage
  try {
    const raw = typeof localStorage !== 'undefined' ? localStorage.getItem(storageKey) : null
    if (raw) {
      const parsed = JSON.parse(raw) as PersistedState
      if (
        Array.isArray(parsed.order) &&
        parsed.order.length === originalOrder.length &&
        parsed.order.every((k) => columnMap.has(k))
      ) {
        orderedKeys.value = [...parsed.order]
      }
      if (Array.isArray(parsed.hidden)) {
        hiddenKeys.value = new Set(parsed.hidden.filter((k) => columnMap.has(k)))
      }
    }
  } catch {
    // ignore storage errors
  }

  const visibleColumns = computed<ColumnDef[]>(() =>
    orderedKeys.value
      .filter((k) => !hiddenKeys.value.has(k))
      .map((k) => columnMap.get(k))
      .filter((c): c is ColumnDef => c !== undefined),
  )

  function hide(key: string): void {
    if (!columnMap.has(key)) return
    const next = new Set(hiddenKeys.value)
    next.add(key)
    hiddenKeys.value = next
  }

  function show(key: string): void {
    if (!columnMap.has(key)) return
    const next = new Set(hiddenKeys.value)
    next.delete(key)
    hiddenKeys.value = next
  }

  function toggle(key: string): void {
    if (hiddenKeys.value.has(key)) show(key)
    else hide(key)
  }

  function reorder(newKeys: string[]): void {
    // Preserve only known keys; ignore unknown
    const filtered = newKeys.filter((k) => columnMap.has(k))
    orderedKeys.value = [...filtered]
  }

  function reset(): void {
    orderedKeys.value = [...originalOrder]
    hiddenKeys.value = new Set(defaultHidden)
  }

  watch(
    [orderedKeys, hiddenKeys],
    () => {
      try {
        if (typeof localStorage === 'undefined') return
        const payload: PersistedState = {
          hidden: [...hiddenKeys.value],
          order: [...orderedKeys.value],
        }
        localStorage.setItem(storageKey, JSON.stringify(payload))
      } catch {
        // ignore storage errors
      }
    },
    { deep: true },
  )

  return {
    visibleColumns,
    orderedKeys,
    hiddenKeys,
    hide,
    show,
    toggle,
    reorder,
    reset,
  }
}
