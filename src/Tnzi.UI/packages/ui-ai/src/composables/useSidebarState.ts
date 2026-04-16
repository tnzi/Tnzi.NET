import { ref, computed, watch, onBeforeUnmount, getCurrentInstance, type Ref, type ComputedRef } from 'vue'

export type SidebarMode = 'expanded' | 'icon' | 'hidden'

export interface UseSidebarStateOptions {
  initialMode?: SidebarMode
  mobileBreakpoint?: number
  storageKey?: string | null
}

export interface UseSidebarStateReturn {
  mode: Ref<SidebarMode>
  isMobile: ComputedRef<boolean>
  setMode: (mode: SidebarMode) => void
  cycle: () => void
}

const VALID_MODES: readonly SidebarMode[] = ['expanded', 'icon', 'hidden']

function isSidebarMode(value: unknown): value is SidebarMode {
  return typeof value === 'string' && (VALID_MODES as readonly string[]).includes(value)
}

function readPersisted(key: string | null | undefined): SidebarMode | null {
  if (!key || typeof localStorage === 'undefined') return null
  try {
    const raw = localStorage.getItem(key)
    return isSidebarMode(raw) ? raw : null
  } catch {
    return null
  }
}

function writePersisted(key: string | null | undefined, mode: SidebarMode): void {
  if (!key || typeof localStorage === 'undefined') return
  try {
    localStorage.setItem(key, mode)
  } catch {
    // ignore quota/SSR errors
  }
}

/**
 * @experimental
 * Manages the three-mode sidebar state machine (expanded / icon / hidden)
 * with localStorage persistence and mobile breakpoint auto-collapse.
 *
 * Entering the mobile breakpoint saves the current desktop mode and forces
 * `hidden`. Leaving the mobile breakpoint restores the remembered mode.
 */
export function useSidebarState(options: UseSidebarStateOptions = {}): UseSidebarStateReturn {
  const storageKey = options.storageKey === undefined ? 'tnzi-ui-ai-sidebar-mode' : options.storageKey
  const mobileBreakpoint = options.mobileBreakpoint ?? 768
  const initial = readPersisted(storageKey) ?? options.initialMode ?? 'expanded'

  const mode = ref<SidebarMode>(initial)
  const rememberedDesktopMode = ref<SidebarMode>(initial === 'hidden' ? 'expanded' : initial)
  const windowWidth = ref(typeof window === 'undefined' ? 1280 : window.innerWidth)

  const isMobile = computed(() => windowWidth.value < mobileBreakpoint)

  function setMode(next: SidebarMode): void {
    mode.value = next
    if (!isMobile.value) {
      rememberedDesktopMode.value = next
    }
    writePersisted(storageKey, next)
  }

  function cycle(): void {
    const order: SidebarMode[] = ['expanded', 'icon', 'hidden']
    const currentIndex = order.indexOf(mode.value)
    const nextMode = order[(currentIndex + 1) % order.length] ?? 'expanded'
    setMode(nextMode)
  }

  let lastIsMobile = isMobile.value
  watch(isMobile, (nowMobile) => {
    if (nowMobile && !lastIsMobile) {
      if (mode.value !== 'hidden') {
        rememberedDesktopMode.value = mode.value
      }
      mode.value = 'hidden'
    } else if (!nowMobile && lastIsMobile) {
      mode.value = rememberedDesktopMode.value
    }
    lastIsMobile = nowMobile
  })

  function onResize(): void {
    if (typeof window !== 'undefined') {
      windowWidth.value = window.innerWidth
    }
  }

  // Attach immediately — Vue's onMounted does not fire when composables are
  // called outside a component setup context (e.g. unit tests or the
  // playground store factory), so deferring would break those callers.
  if (typeof window !== 'undefined') {
    window.addEventListener('resize', onResize)
  }

  // Cleanup only fires in a real component setup context.
  if (getCurrentInstance()) {
    onBeforeUnmount(() => {
      if (typeof window !== 'undefined') {
        window.removeEventListener('resize', onResize)
      }
    })
  }

  return { mode, isMobile, setMode, cycle }
}
