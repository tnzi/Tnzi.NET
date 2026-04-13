import { ref, computed, watch, type Ref, type ComputedRef } from 'vue'
import type { IMenuItem } from '@tnzi/core/types/shared-ui'

export interface UseMenuOptions {
  items?: IMenuItem[]
  activeKey?: string
  openedKeys?: string[]
  onSelect?: (key: string, item: IMenuItem) => void
  onOpenChange?: (keys: string[]) => void
}

export interface UseMenuReturn {
  internalOpenKeys: Ref<string[]>
  visibleItems: ComputedRef<IMenuItem[]>
  isOpen: (key: string) => boolean
  toggleOpen: (key: string) => void
  selectItem: (item: IMenuItem) => void
}

export function useMenu(options: UseMenuOptions = {}): UseMenuReturn {
  const items = options.items ?? []

  const internalOpenKeys = ref<string[]>([...(options.openedKeys ?? [])])

  watch(
    () => options.openedKeys,
    (value) => {
      internalOpenKeys.value = [...(value ?? [])]
    },
  )

  const visibleItems = computed(() => items.filter((item) => !item.hidden))

  function isOpen(key: string): boolean {
    return internalOpenKeys.value.includes(key)
  }

  function toggleOpen(key: string): void {
    if (isOpen(key)) {
      internalOpenKeys.value = internalOpenKeys.value.filter((k) => k !== key)
    } else {
      internalOpenKeys.value = [...internalOpenKeys.value, key]
    }
    options.onOpenChange?.(internalOpenKeys.value)
  }

  function selectItem(item: IMenuItem): void {
    if (item.disabled) return
    options.onSelect?.(item.key, item)
  }

  return {
    internalOpenKeys,
    visibleItems,
    isOpen,
    toggleOpen,
    selectItem,
  }
}
