/**
 * @tnzi/vant/hooks/useMobileViewport
 *
 * Simple viewport helper for mobile-first SPA pages.
 */

import { computed, onMounted, onUnmounted, ref } from 'vue';

export function useMobileViewport(breakpoint = 768) {
  const width = ref(typeof window !== 'undefined' ? window.innerWidth : breakpoint);

  const update = () => {
    width.value = window.innerWidth;
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
    isMobile: computed(() => width.value <= breakpoint),
  };
}

