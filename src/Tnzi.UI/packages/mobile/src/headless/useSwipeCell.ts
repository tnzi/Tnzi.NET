import { ref, computed, type Ref, type ComputedRef } from 'vue'

export type SwipeDirection = 'left' | 'right' | 'none'

export interface UseSwipeCellOptions {
  disabled?: boolean
  onOpen?: (direction: 'left' | 'right') => void
  onClose?: () => void
}

export interface UseSwipeCellReturn {
  openDirection: Ref<SwipeDirection>
  isOpen: ComputedRef<boolean>
  isOpenLeft: ComputedRef<boolean>
  isOpenRight: ComputedRef<boolean>
  open: (direction: 'left' | 'right') => void
  close: () => void
  toggle: (direction: 'left' | 'right') => void
}

export function useSwipeCell(options: UseSwipeCellOptions = {}): UseSwipeCellReturn {
  const openDirection = ref<SwipeDirection>('none')

  const isOpen = computed(() => openDirection.value !== 'none')
  const isOpenLeft = computed(() => openDirection.value === 'left')
  const isOpenRight = computed(() => openDirection.value === 'right')

  function open(direction: 'left' | 'right'): void {
    if (options.disabled) return
    openDirection.value = direction
    options.onOpen?.(direction)
  }

  function close(): void {
    if (openDirection.value === 'none') return
    openDirection.value = 'none'
    options.onClose?.()
  }

  function toggle(direction: 'left' | 'right'): void {
    if (options.disabled) return
    if (openDirection.value === direction) {
      close()
    } else {
      open(direction)
    }
  }

  return {
    openDirection,
    isOpen,
    isOpenLeft,
    isOpenRight,
    open,
    close,
    toggle,
  }
}
