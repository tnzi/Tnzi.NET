import { computed, type ComputedRef, type Ref } from 'vue'
import { useBreakpoints as vueuseBreakpoints, useWindowSize, breakpointsTailwind } from '@vueuse/core'

/**
 * Responsive breakpoints based on Tailwind's defaults.
 *
 * Breakpoints (Tailwind):
 *   sm  → 640px
 *   md  → 768px
 *   lg  → 1024px
 *   xl  → 1280px
 *   2xl → 1536px
 *
 * Returns both coarse triad (isMobile/isTablet/isDesktop) and fine-grained
 * ranges (isXs/isSm/isMd/isLg) so mobile-first layouts can choose card vs
 * full-screen modal, single-vs-multi-column grids, etc.
 */
/**
 * The narrowed shape of `vueuseBreakpoints(breakpointsTailwind)` - vueuse
 * infers the key union (`'sm' | 'md' | ...`) from the input record, and we
 * surface that specific shape rather than the wider `Record<string, ...>`
 * default. Without this narrowing, `ReturnType<typeof vueuseBreakpoints>`
 * (no type arg) resolves to the wider default and triggers a TS2322
 * incompatibility when we assign `bp` to it.
 */
type TailwindBreakpoints = ReturnType<typeof vueuseBreakpoints<keyof typeof breakpointsTailwind>>

export interface UseBreakpointsReturn {
  /** Raw VueUse breakpoint API (`smaller`/`greater`/`between`/`isGreater`…). */
  raw: TailwindBreakpoints
  /** Viewport width in px (reactive). 0 in non-browser envs. */
  width: ComputedRef<number>
  /** Viewport height in px (reactive). 0 in non-browser envs. */
  height: ComputedRef<number>
  // Coarse triad - backward compatible with original `useBreakpoints()`.
  /** `true` when viewport < 768px (Tailwind md-down). */
  isMobile: ComputedRef<boolean>
  /** `true` when viewport is between 768px and 1024px. */
  isTablet: ComputedRef<boolean>
  /** `true` when viewport ≥ 1024px (Tailwind lg+). */
  isDesktop: ComputedRef<boolean>
  // Fine-grained ranges - moved here from `@tnzi/ui-admin` in 0.2.71+.
  /** `true` when viewport < 640px. */
  isXs: Readonly<Ref<boolean>>
  /** `true` when viewport < 768px. */
  isSm: Readonly<Ref<boolean>>
  /** `true` when viewport < 1024px. */
  isMd: Readonly<Ref<boolean>>
  /** `true` when viewport < 1280px. */
  isLg: Readonly<Ref<boolean>>
  // …AndUp helpers - backward compatible.
  /** `true` when viewport ≥ 640px. */
  smAndUp: Readonly<Ref<boolean>>
  /** `true` when viewport ≥ 768px. */
  mdAndUp: Readonly<Ref<boolean>>
  /** `true` when viewport ≥ 1024px. */
  lgAndUp: Readonly<Ref<boolean>>
  /** `true` when viewport ≥ 1280px. */
  xlAndUp: Readonly<Ref<boolean>>
  /**
   * `true` when the primary pointer is coarse (touchscreen). Use to gate
   * hover-only affordances and supplement with explicit tap targets.
   *
   * SSR-safe: returns `false` in non-browser environments.
   */
  isTouch: ComputedRef<boolean>
}

let cachedTouch: boolean | null = null
function probeTouch(): boolean {
  if (cachedTouch !== null) return cachedTouch
  if (typeof window === 'undefined') {
    cachedTouch = false
    return false
  }
  try {
    if (typeof window.matchMedia === 'function') {
      const mql = window.matchMedia('(pointer: coarse)')
      if (typeof mql.matches === 'boolean') {
        cachedTouch = mql.matches
        return cachedTouch
      }
    }
  } catch {
    // ignore - fall through to ontouchstart probe
  }
  cachedTouch = 'ontouchstart' in window
  return cachedTouch
}

/**
 * Reset the cached `isTouch` probe. Test-only helper for unit tests that
 * monkey-patch `window.matchMedia` between mounts.
 */
export function __resetTouchProbeForTests(): void {
  cachedTouch = null
}

/**
 * Reactive responsive breakpoint helper. Returns both the coarse
 * mobile/tablet/desktop triad and fine-grained xs/sm/md/lg ranges, plus
 * the live viewport `width`/`height` and a `isTouch` probe.
 *
 * Backward compatible with earlier versions that returned only
 * `{ raw, isMobile, isTablet, isDesktop, smAndUp, mdAndUp, lgAndUp, xlAndUp }`.
 */
export function useBreakpoints(): UseBreakpointsReturn {
  const bp = vueuseBreakpoints(breakpointsTailwind)
  const { width: rawWidth, height: rawHeight } = useWindowSize({
    initialWidth: 1024,
    initialHeight: 768,
    listenOrientation: true,
  })

  return {
    raw: bp,
    width: computed(() => rawWidth.value),
    height: computed(() => rawHeight.value),
    isMobile: computed(() => bp.smaller('md').value),
    isTablet: computed(() => bp.between('md', 'lg').value),
    isDesktop: computed(() => bp.greaterOrEqual('lg').value),
    isXs: bp.smaller('sm'),
    isSm: bp.smaller('md'),
    isMd: bp.smaller('lg'),
    isLg: bp.smaller('xl'),
    smAndUp: bp.greaterOrEqual('sm'),
    mdAndUp: bp.greaterOrEqual('md'),
    lgAndUp: bp.greaterOrEqual('lg'),
    xlAndUp: bp.greaterOrEqual('xl'),
    isTouch: computed(() => probeTouch()),
  }
}

/**
 * Subset of `useBreakpoints()` matching the original `@tnzi/ui-admin`
 * `useBreakpoint()` (singular) shape. Provided for backward compatibility
 * after the helper was sunk from ui-admin into @tnzi/ui in 0.2.71+.
 *
 * Prefer `useBreakpoints()` in new code - it offers the same fields plus
 * the original triad and `…AndUp` helpers.
 */
export interface UseBreakpointReturn {
  width: ComputedRef<number>
  height: ComputedRef<number>
  isXs: Readonly<Ref<boolean>>
  isSm: Readonly<Ref<boolean>>
  isMd: Readonly<Ref<boolean>>
  isLg: Readonly<Ref<boolean>>
  isDesktop: Readonly<Ref<boolean>>
  isTouch: ComputedRef<boolean>
}

export function useBreakpoint(): UseBreakpointReturn {
  const all = useBreakpoints()
  return {
    width: all.width,
    height: all.height,
    isXs: all.isXs,
    isSm: all.isSm,
    isMd: all.isMd,
    isLg: all.isLg,
    isDesktop: all.isDesktop as unknown as Readonly<Ref<boolean>>,
    isTouch: all.isTouch,
  }
}
