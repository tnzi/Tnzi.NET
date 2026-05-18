import { computed, type Ref, type ComputedRef } from 'vue'
import { useBreakpoints, useWindowSize, breakpointsTailwind } from '@vueuse/core'

/**
 * `useBreakpoint` — responsive breakpoint helper for admin shell components.
 *
 * Centralises the Tailwind breakpoint scale used across `@tnzi/ui-admin`:
 *   xs  <  640
 *   sm  640 – 767
 *   md  768 – 1023
 *   lg  1024 – 1279
 *   xl  1280 – 1535
 *   2xl ≥ 1536
 *
 * Mirrors `useAdminAppStore`'s `isMobile/isTablet/isDesktop` triad but adds
 * finer-grained ranges and a touch-pointer probe so components can adapt
 * hover-only affordances when the device is a touchscreen.
 *
 * Why a dedicated headless instead of reusing the store?
 *  - The store binding is Pinia-scoped — components that mount outside a
 *    Pinia app (e.g. isolated test mounts) can still consume this helper.
 *  - The store only exposes a coarse triad; mobile-first design needs the
 *    distinct breakpoints (375 vs 640 vs 768) to choose card vs full-screen
 *    modal, single-vs-multi-column grids, etc.
 *  - The helper is SSR-safe — happy-dom / node environments without
 *    `window.matchMedia` get sensible default-`false` values.
 */
export interface UseBreakpointReturn {
  /** Viewport width in px (reactive). 0 in non-browser envs. */
  width: ComputedRef<number>
  /** Viewport height in px (reactive). 0 in non-browser envs. */
  height: ComputedRef<number>
  /** Width < 640px (Tailwind sm-down, phones in portrait). */
  isXs: Readonly<Ref<boolean>>
  /** Width < 768px (Tailwind md-down, large phones / phablets). */
  isSm: Readonly<Ref<boolean>>
  /** Width < 1024px (Tailwind lg-down, tablets). */
  isMd: Readonly<Ref<boolean>>
  /** Width < 1280px (Tailwind xl-down, small laptops). */
  isLg: Readonly<Ref<boolean>>
  /** Width ≥ 1024px (Tailwind lg+, desktop and up). */
  isDesktop: Readonly<Ref<boolean>>
  /** True when the pointer is coarse (touchscreen). Hover-only affordances
   *  should be supplemented with tap targets when this is true. */
  isTouch: ComputedRef<boolean>
}

let cachedTouch: boolean | null = null
function probeTouch(): boolean {
  if (cachedTouch !== null) return cachedTouch
  if (typeof window === 'undefined') {
    cachedTouch = false
    return false
  }
  // `matchMedia('(pointer: coarse)')` is the canonical way to detect a
  // primary touchscreen pointer. Old Safari may return undefined; fall back
  // to ontouchstart for those edge cases.
  try {
    if (typeof window.matchMedia === 'function') {
      const mql = window.matchMedia('(pointer: coarse)')
      if (typeof mql.matches === 'boolean') {
        cachedTouch = mql.matches
        return cachedTouch
      }
    }
  } catch {
    // ignore — fall through to ontouchstart probe
  }
  cachedTouch = 'ontouchstart' in window
  return cachedTouch
}

export function useBreakpoint(): UseBreakpointReturn {
  const bp = useBreakpoints(breakpointsTailwind)
  const { width: rawWidth, height: rawHeight } = useWindowSize({
    initialWidth: 1024,
    initialHeight: 768,
    listenOrientation: true,
  })

  return {
    width: computed(() => rawWidth.value),
    height: computed(() => rawHeight.value),
    isXs: bp.smaller('sm'),
    isSm: bp.smaller('md'),
    isMd: bp.smaller('lg'),
    isLg: bp.smaller('xl'),
    isDesktop: bp.greaterOrEqual('lg'),
    isTouch: computed(() => probeTouch()),
  }
}

/**
 * Test-only helper. Resets the cached touch probe so unit tests that
 * monkey-patch `window.matchMedia` can re-evaluate the result.
 */
export function __resetTouchProbeForTests(): void {
  cachedTouch = null
}
