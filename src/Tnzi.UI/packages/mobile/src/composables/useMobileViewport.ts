/**
 * @tnzi/mobile/composables/useMobileViewport
 *
 * Viewport helper for mobile-first SPA pages.
 * Provides width, height, orientation, and safe-area measurement.
 */

import { computed, onMounted, onUnmounted, ref } from 'vue';

export interface MobileViewportOptions {
  /** Width breakpoint for mobile detection (default: 768) */
  breakpoint?: number;
}

export interface SafeAreaInsets {
  top: number;
  bottom: number;
}

const EMPTY_INSETS: SafeAreaInsets = { top: 0, bottom: 0 };

/**
 * Measure the real `env(safe-area-inset-*)` values.
 *
 * Feature detection (`CSS.supports('env(...)')`) is not enough: every modern
 * browser supports the function, desktop Chrome included, and simply resolves it
 * to 0. Only a rendered probe tells us whether the device actually reserves
 * space (notch, home indicator, ...).
 */
function measureSafeAreaInsets(): SafeAreaInsets {
  if (typeof document === 'undefined' || !document.body) return EMPTY_INSETS;

  const probe = document.createElement('div');
  probe.style.cssText = [
    'position:fixed',
    'top:0',
    'left:0',
    'width:0',
    'height:0',
    'visibility:hidden',
    'pointer-events:none',
    'padding-top:env(safe-area-inset-top, 0px)',
    'padding-bottom:env(safe-area-inset-bottom, 0px)',
  ].join(';');

  document.body.appendChild(probe);
  const style = window.getComputedStyle(probe);
  const insets: SafeAreaInsets = {
    top: Number.parseFloat(style.paddingTop) || 0,
    bottom: Number.parseFloat(style.paddingBottom) || 0,
  };
  probe.remove();

  return insets;
}

export function useMobileViewport(optionsOrBreakpoint: number | MobileViewportOptions = 768) {
  const breakpoint = typeof optionsOrBreakpoint === 'number'
    ? optionsOrBreakpoint
    : (optionsOrBreakpoint.breakpoint ?? 768);

  const width = ref(typeof window !== 'undefined' ? window.innerWidth : breakpoint);
  const height = ref(typeof window !== 'undefined' ? window.innerHeight : 0);
  const safeAreaInsets = ref<SafeAreaInsets>(EMPTY_INSETS);

  const update = () => {
    width.value = window.innerWidth;
    height.value = window.innerHeight;
    // Rotating a notched device moves the insets between edges, so remeasure.
    safeAreaInsets.value = measureSafeAreaInsets();
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
    /** Measured `env(safe-area-inset-top / -bottom)` in CSS pixels. */
    safeAreaInsets,
    /** Whether the device actually reserves safe-area space (notch, home indicator). */
    hasSafeArea: computed(() => safeAreaInsets.value.top > 0 || safeAreaInsets.value.bottom > 0),
  };
}
