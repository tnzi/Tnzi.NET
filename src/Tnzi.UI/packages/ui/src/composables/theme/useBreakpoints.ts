import { useBreakpoints as vueuseBreakpoints, breakpointsTailwind } from '@vueuse/core'
import { computed } from 'vue'

/**
 * Responsive breakpoints based on Tailwind's defaults.
 *
 * Breakpoints:
 *   sm  → 640px
 *   md  → 768px
 *   lg  → 1024px
 *   xl  → 1280px
 *   2xl → 1536px
 *
 * Convenience booleans:
 *   isMobile  — true when < md (768px)
 *   isTablet  — true when >= md and < lg
 *   isDesktop — true when >= lg
 */
export function useBreakpoints() {
  const bp = vueuseBreakpoints(breakpointsTailwind)

  return {
    raw: bp,
    isMobile: computed(() => bp.smaller('md').value),
    isTablet: computed(() => bp.between('md', 'lg').value),
    isDesktop: computed(() => bp.greaterOrEqual('lg').value),
    smAndUp: bp.greaterOrEqual('sm'),
    mdAndUp: bp.greaterOrEqual('md'),
    lgAndUp: bp.greaterOrEqual('lg'),
    xlAndUp: bp.greaterOrEqual('xl'),
  }
}
