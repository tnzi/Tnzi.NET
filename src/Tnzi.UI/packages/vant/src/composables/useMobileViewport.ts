/**
 * @tnzi/vant/hooks/useMobileViewport
 *
 * Viewport helper for mobile-first SPA pages.
 * Provides width, height, orientation, and safe-area detection.
 */

import { computed, onMounted, onUnmounted, ref } from 'vue';

export interface MobileViewportOptions {
  /** Width breakpoint for mobile detection (default: 768) */
  breakpoint?: number;
}

export function useMobileViewport(optionsOrBreakpoint: number | MobileViewportOptions = 768) {
  const breakpoint = typeof optionsOrBreakpoint === 'number'
    ? optionsOrBreakpoint
    : (optionsOrBreakpoint.breakpoint ?? 768);

  const width = ref(typeof window !== 'undefined' ? window.innerWidth : breakpoint);
  const height = ref(typeof window !== 'undefined' ? window.innerHeight : 0);

  const update = () => {
    width.value = window.innerWidth;
    height.value = window.innerHeight;
  };

  onMounted(() => {
    update();
    window.addEventListener('resize', update);
  });

  onUnmounted(() => {
    window.removeEventListener('resize', update);
  });

  return {
    width,
    height,
    isMobile: computed(() => width.value <= breakpoint),
    isLandscape: computed(() => width.value > height.value),
    isPortrait: computed(() => width.value <= height.value),
    /** Whether the device has safe-area insets (e.g., iPhone notch) */
    hasSafeArea: computed(() => {
      if (typeof window === 'undefined' || typeof CSS === 'undefined') return false;
      return CSS.supports('padding-top: env(safe-area-inset-top)');
    }),
  };
}

