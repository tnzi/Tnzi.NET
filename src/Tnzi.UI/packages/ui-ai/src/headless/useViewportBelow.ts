import { computed, getCurrentScope, onScopeDispose, ref, toValue, type ComputedRef, type MaybeRefOrGetter } from 'vue'

/**
 * Whether the viewport is narrower than `breakpoint` CSS pixels, kept current
 * on resize. The test is `innerWidth < breakpoint`, the same one
 * `useSidebarState` uses for its mobile mode, so a surface laid out by this and
 * the sidebar's drawer switch at the same width.
 *
 * Call it inside a component or another effect scope: the resize listener is
 * released when that scope ends. Without `window` (SSR) it reports wide.
 */
export function useViewportBelow(breakpoint: MaybeRefOrGetter<number>): ComputedRef<boolean> {
  const hasWindow = typeof window !== 'undefined'
  const width = ref(hasWindow ? window.innerWidth : Number.POSITIVE_INFINITY)

  if (hasWindow) {
    const onResize = (): void => {
      width.value = window.innerWidth
    }
    window.addEventListener('resize', onResize, { passive: true })
    if (getCurrentScope()) onScopeDispose(() => window.removeEventListener('resize', onResize))
  }

  return computed(() => width.value < toValue(breakpoint))
}
