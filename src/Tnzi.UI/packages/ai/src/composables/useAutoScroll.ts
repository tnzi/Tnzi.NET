/**
 * useAutoScroll — Stick-to-bottom behavior for chat containers
 *
 * Automatically scrolls to bottom when new content appears, unless the user
 * has scrolled up. Re-attaches when user scrolls back to bottom.
 */

import { ref, readonly, onMounted, onUnmounted, type Ref, type DeepReadonly } from 'vue';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export interface UseAutoScrollOptions {
  /** Distance from bottom (in px) to consider "at bottom". Default: 50 */
  threshold?: number;
}

export interface UseAutoScrollReturn {
  /** Template ref to bind to the scrollable container element. */
  containerRef: Ref<HTMLElement | null>;
  /** Whether the container is currently scrolled to the bottom. */
  isAtBottom: DeepReadonly<Ref<boolean>>;
  /** Programmatically scroll to the bottom. */
  scrollToBottom: (behavior?: ScrollBehavior) => void;
}

// ---------------------------------------------------------------------------
// Composable
// ---------------------------------------------------------------------------

export function useAutoScroll(options: UseAutoScrollOptions = {}): UseAutoScrollReturn {
  const { threshold = 50 } = options;

  const containerRef = ref<HTMLElement | null>(null);
  const isAtBottom = ref(true);

  let observer: MutationObserver | null = null;
  let rafId: number | null = null;

  function checkIsAtBottom(): boolean {
    const el = containerRef.value;
    if (!el) return true;
    const { scrollTop, scrollHeight, clientHeight } = el;
    return scrollHeight - scrollTop - clientHeight <= threshold;
  }

  function handleScroll(): void {
    isAtBottom.value = checkIsAtBottom();
  }

  function scrollToBottom(behavior: ScrollBehavior = 'smooth'): void {
    const el = containerRef.value;
    if (!el) return;
    el.scrollTo({ top: el.scrollHeight, behavior });
    isAtBottom.value = true;
  }

  function handleMutation(): void {
    if (!isAtBottom.value) return;
    // Coalesce multiple mutations into one scroll via rAF
    if (rafId != null) return;
    rafId = requestAnimationFrame(() => {
      rafId = null;
      scrollToBottom('smooth');
    });
  }

  function setup(): void {
    const el = containerRef.value;
    if (!el) return;

    el.addEventListener('scroll', handleScroll, { passive: true });

    observer = new MutationObserver(handleMutation);
    observer.observe(el, { childList: true, subtree: true });
  }

  function cleanup(): void {
    const el = containerRef.value;
    if (el) {
      el.removeEventListener('scroll', handleScroll);
    }
    if (observer) {
      observer.disconnect();
      observer = null;
    }
    if (rafId != null) {
      cancelAnimationFrame(rafId);
      rafId = null;
    }
  }

  onMounted(setup);
  onUnmounted(cleanup);

  return {
    containerRef,
    isAtBottom: readonly(isAtBottom),
    scrollToBottom,
  };
}
