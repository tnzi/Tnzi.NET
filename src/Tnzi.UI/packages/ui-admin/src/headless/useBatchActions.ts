import { ref, computed, type Ref, type ComputedRef } from 'vue'

export interface BatchAction {
  key: string
  label: string
  variant?: string
  confirmMessage?: string
}

export interface UseBatchActionsOptions {
  actions: BatchAction[]
  onAction?: (key: string, selectedKeys: string[]) => Promise<void>
}

export interface UseBatchActionsReturn {
  selectedKeys: Ref<string[]>
  hasSelection: ComputedRef<boolean>
  selectedCount: ComputedRef<number>
  actions: BatchAction[]
  execute: (actionKey: string) => Promise<void>
  clearSelection: () => void
}

export function useBatchActions(options: UseBatchActionsOptions): UseBatchActionsReturn {
  const selectedKeys = ref<string[]>([])
  const hasSelection = computed(() => selectedKeys.value.length > 0)
  const selectedCount = computed(() => selectedKeys.value.length)

  async function execute(actionKey: string): Promise<void> {
    if (selectedKeys.value.length === 0) return
    await options.onAction?.(actionKey, [...selectedKeys.value])
  }

  function clearSelection(): void {
    selectedKeys.value = []
  }

  return {
    selectedKeys,
    hasSelection,
    selectedCount,
    actions: options.actions,
    execute,
    clearSelection,
  }
}
