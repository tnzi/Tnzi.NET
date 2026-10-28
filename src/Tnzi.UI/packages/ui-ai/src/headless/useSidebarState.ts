import { ref, computed, watch, onScopeDispose, type Ref, type ComputedRef } from 'vue'

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
  /**
   * Detach the resize listener and stop the breakpoint watcher. Called
   * automatically when the owning effect scope (component or `effectScope()`)
   * is disposed; call it by hand only when there is no scope to hang off.
   */
  dispose: () => void
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
 * Starting below the breakpoint starts `hidden` too, with the persisted mode
 * kept for when the viewport widens.
 *
 * Only modes chosen on a desktop viewport are persisted: below the breakpoint
 * the sidebar is a drawer, and opening or closing it is not a layout
 * preference (persisting it would bring a later desktop load back hidden).
 *
 * Below the breakpoint the drawer is only ever open (`expanded`) or closed
 * (`hidden`): `setMode('icon')` there opens it expanded. A drawer holding the
 * icon rail is a wide panel of unlabelled icons with no conversations in it,
 * which is what a phone got whenever anything asked for the rail while narrow
 * (the drawer's own collapse button, the rail, a product's own toggle).
 */
export function useSidebarState(options: UseSidebarStateOptions = {}): UseSidebarStateReturn {
  const storageKey = options.storageKey === undefined ? 'tnzi-ui-ai-sidebar-mode' : options.storageKey
  const mobileBreakpoint = options.mobileBreakpoint ?? 768
  const desktopMode = readPersisted(storageKey) ?? options.initialMode ?? 'expanded'
  const windowWidth = ref(typeof window === 'undefined' ? 1280 : window.innerWidth)

  const isMobile = computed(() => windowWidth.value < mobileBreakpoint)

  const mode = ref<SidebarMode>(isMobile.value ? 'hidden' : desktopMode)
  const rememberedDesktopMode = ref<SidebarMode>(desktopMode === 'hidden' ? 'expanded' : desktopMode)

  function setMode(next: SidebarMode): void {
    if (isMobile.value) {
      mode.value = next === 'hidden' ? 'hidden' : 'expanded'
      return
    }
    mode.value = next
    rememberedDesktopMode.value = next
    writePersisted(storageKey, next)
  }

  function cycle(): void {
    if (isMobile.value) {
      setMode(mode.value === 'hidden' ? 'expanded' : 'hidden')
      return
    }
    const order: SidebarMode[] = ['expanded', 'icon', 'hidden']
    const currentIndex = order.indexOf(mode.value)
    const nextMode = order[(currentIndex + 1) % order.length] ?? 'expanded'
    setMode(nextMode)
  }

  let lastIsMobile = isMobile.value
  const stopBreakpointWatch = watch(isMobile, (nowMobile) => {
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

  // Attach immediately: Vue's onMounted does not fire when composables are
  // called outside a component setup context (e.g. unit tests or a
  // consumer's store factory), so deferring would break those callers.
  if (typeof window !== 'undefined') {
    window.addEventListener('resize', onResize)
    /* Read again once the first frame is laid out: a mobile browser can report
       a desktop-sized layout viewport while the page's viewport meta is still
       being applied, and does not always follow it with a resize event. */
    if (typeof window.requestAnimationFrame === 'function') window.requestAnimationFrame(onResize)
  }

  let disposed = false
  function dispose(): void {
    if (disposed) return
    disposed = true
    stopBreakpointWatch()
    if (typeof window !== 'undefined') {
      window.removeEventListener('resize', onResize)
    }
  }

  // onScopeDispose covers components AND standalone effectScope() owners; the
  // `failSilently` flag keeps it quiet for the documented scope-less callers,
  // who are expected to call `dispose()` themselves. Registering the listener
  // unconditionally while only cleaning it up inside a component would leak on
  // every scope-less call.
  onScopeDispose(dispose, true)

  return { mode, isMobile, setMode, cycle, dispose }
}
