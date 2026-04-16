import { computed, ref, type ComputedRef, type Ref } from 'vue'

export interface UseBatchActionsReturn<TId> {
  selected: Ref<Set<TId>>
  selectedIds: ComputedRef<TId[]>
  selectedCount: ComputedRef<number>
  hasSelection: ComputedRef<boolean>
  select: (id: TId) => void
  unselect: (id: TId) => void
  toggle: (id: TId) => void
  selectAll: (ids: TId[]) => void
  clear: () => void
  isSelected: (id: TId) => boolean
}

export function useBatchActions<TId = string | number>(): UseBatchActionsReturn<TId> {
  const selected = ref(new Set<TId>()) as Ref<Set<TId>>

  const selectedCount = computed(() => selected.value.size)
  const hasSelection = computed(() => selected.value.size > 0)
  const selectedIds = computed<TId[]>(() => [...selected.value])

  function select(id: TId): void {
    if (selected.value.has(id)) return
    const next = new Set(selected.value)
    next.add(id)
    selected.value = next
  }

  function unselect(id: TId): void {
    if (!selected.value.has(id)) return
    const next = new Set(selected.value)
    next.delete(id)
    selected.value = next
  }

  function toggle(id: TId): void {
    if (selected.value.has(id)) unselect(id)
    else select(id)
  }

  function selectAll(ids: TId[]): void {
    selected.value = new Set(ids)
  }

  function clear(): void {
    if (selected.value.size === 0) return
    selected.value = new Set()
  }

  function isSelected(id: TId): boolean {
    return selected.value.has(id)
  }

  return {
    selected,
    selectedIds,
    selectedCount,
    hasSelection,
    select,
    unselect,
    toggle,
    selectAll,
    clear,
    isSelected,
  }
}
