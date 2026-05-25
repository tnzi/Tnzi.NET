import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { nextTick } from 'vue'
import { useBreakpoint, __resetTouchProbeForTests } from '../../src/headless/useBreakpoint'

/**
 * happy-dom doesn't expose `window.matchMedia` by default. The tests here
 * install a stub that resolves Tailwind breakpoint queries by comparing
 * against `window.innerWidth`, which `@vueuse/core`'s `useBreakpoints` /
 * `useWindowSize` consult internally.
 */
type MqlListener = (e: MediaQueryListEvent) => void
const listeners: Set<MqlListener> = new Set()

function installMatchMedia(initialWidth: number, opts: { coarse?: boolean } = {}): void {
  Object.defineProperty(window, 'innerWidth', {
    configurable: true,
    writable: true,
    value: initialWidth,
  })
  Object.defineProperty(window, 'innerHeight', {
    configurable: true,
    writable: true,
    value: 768,
  })
  Object.defineProperty(window, 'matchMedia', {
    configurable: true,
    writable: true,
    value: (query: string): MediaQueryList => {
      const parseMinWidth = (q: string): number | null => {
        const m = q.match(/min-width:\s*([\d.]+)px/)
        return m ? Number(m[1]) : null
      }
      const parseMaxWidth = (q: string): number | null => {
        const m = q.match(/max-width:\s*([\d.]+)px/)
        return m ? Number(m[1]) : null
      }
      const evaluate = (): boolean => {
        if (query.includes('pointer: coarse')) return !!opts.coarse
        const min = parseMinWidth(query)
        const max = parseMaxWidth(query)
        if (min !== null && max !== null) {
          return window.innerWidth >= min && window.innerWidth <= max
        }
        if (min !== null) return window.innerWidth >= min
        if (max !== null) return window.innerWidth <= max
        return false
      }
      const mql: Partial<MediaQueryList> = {
        media: query,
        matches: evaluate(),
        onchange: null,
        addEventListener: (_type: string, listener: EventListener) => {
          listeners.add(listener as unknown as MqlListener)
        },
        removeEventListener: (_type: string, listener: EventListener) => {
          listeners.delete(listener as unknown as MqlListener)
        },
        addListener: (listener: EventListener) =>
          listeners.add(listener as unknown as MqlListener),
        removeListener: (listener: EventListener) =>
          listeners.delete(listener as unknown as MqlListener),
        dispatchEvent: () => true,
      }
      return mql as MediaQueryList
    },
  })
}

function setViewportWidth(width: number): void {
  ;(window as unknown as { innerWidth: number }).innerWidth = width
  window.dispatchEvent(new Event('resize'))
}

describe('useBreakpoint', () => {
  beforeEach(() => {
    listeners.clear()
    __resetTouchProbeForTests()
  })
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('reports xs/sm/md/lg/desktop bands at a phone-portrait width', async () => {
    installMatchMedia(375)
    const bp = useBreakpoint()
    await nextTick()
    expect(bp.isXs.value).toBe(true)
    expect(bp.isSm.value).toBe(true)
    expect(bp.isMd.value).toBe(true)
    expect(bp.isLg.value).toBe(true)
    expect(bp.isDesktop.value).toBe(false)
  })

  it('reports tablet band at 800px', async () => {
    installMatchMedia(800)
    const bp = useBreakpoint()
    await nextTick()
    expect(bp.isXs.value).toBe(false)
    expect(bp.isSm.value).toBe(false)
    expect(bp.isMd.value).toBe(true)
    expect(bp.isDesktop.value).toBe(false)
  })

  it('reports desktop band at 1440px', async () => {
    installMatchMedia(1440)
    const bp = useBreakpoint()
    await nextTick()
    expect(bp.isXs.value).toBe(false)
    expect(bp.isSm.value).toBe(false)
    expect(bp.isMd.value).toBe(false)
    expect(bp.isDesktop.value).toBe(true)
  })

  it('isTouch resolves true when the matchMedia probe reports coarse pointer', () => {
    installMatchMedia(1024, { coarse: true })
    const bp = useBreakpoint()
    expect(bp.isTouch.value).toBe(true)
  })

  it('isTouch resolves false on fine-pointer devices', () => {
    installMatchMedia(1024, { coarse: false })
    const bp = useBreakpoint()
    expect(bp.isTouch.value).toBe(false)
  })

  it('width tracks window.innerWidth reactively', async () => {
    installMatchMedia(1024)
    const bp = useBreakpoint()
    expect(bp.width.value).toBe(1024)
    setViewportWidth(600)
    await nextTick()
    expect(bp.width.value).toBe(600)
  })
})
