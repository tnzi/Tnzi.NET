import { ref } from 'vue'

interface DrawerOptions {
  title?: string
  width?: string
  placement?: 'left' | 'right' | 'top' | 'bottom'
}

interface DrawerState extends DrawerOptions {
  show: boolean
}

export function useDrawer(defaults: DrawerOptions = {}) {
  const state = ref<DrawerState>({
    show: false,
    title: defaults.title ?? '',
    width: defaults.width ?? '400px',
    placement: defaults.placement ?? 'right',
  })

  function open(options: DrawerOptions = {}): void {
    state.value = { ...state.value, ...options, show: true }
  }

  function close(): void {
    state.value.show = false
  }

  return { state, open, close }
}
