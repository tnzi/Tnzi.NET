/**
 * useAutoScroll - Stick-to-bottom behavior for chat containers
 *
 * Automatically scrolls to bottom when new content appears, unless the user
 * has scrolled up. Re-attaches when user scrolls back to bottom.
 *
 * `follow: false` keeps everything except the following: the container still
 * opens at the bottom and `isAtBottom` still tracks new content (so a "scroll
 * to the latest" button knows when to show), but growing content never moves
 * the view. That is the reading model where a sent message is brought to the
 * top and the reply grows beneath it (`useTurnAnchor`).
 *
 * Follows the element bound to `containerRef` wherever it comes and goes: a
 * container rendered after mount (behind a `v-if`, such as a thread that only
 * appears once there are messages) or re-created later is attached when it
 * appears, opened at the bottom, and the previous one released.
 */

import { ref, readonly, watch, onUnmounted, type Ref, type DeepReadonly } from 'vue';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export interface UseAutoScrollOptions {
  /** Distance from bottom (in px) to consider "at bottom". Default: 50 */
  threshold?: number;
  /** Scroll to new content while at the bottom. Default: true */
  follow?: boolean;
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
  const { threshold = 50, follow = true } = options;

  const containerRef = ref<HTMLElement | null>(null);
  const isAtBottom = ref(true);

  let observer: MutationObserver | null = null;
  let rafId: number | null = null;
  /* The element is captured when it is attached rather than re-read from
     `containerRef` during cleanup: Vue nulls template refs before
     onUnmounted runs, so reading the ref there would skip
     removeEventListener entirely and leak the scroll handler. */
  let attachedEl: HTMLElement | null = null;

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
    if (follow) {
      if (!isAtBottom.value) return;
      // Coalesce multiple mutations into one scroll via rAF
      if (rafId != null) return;
      rafId = requestAnimationFrame(() => {
        rafId = null;
        scrollToBottom('smooth');
      });
      return;
    }
    // Not following: content growing below the view only changes whether the
    // view is still at the bottom, measured once per frame.
    if (rafId != null) return;
    rafId = requestAnimationFrame(() => {
      rafId = null;
      isAtBottom.value = checkIsAtBottom();
    });
  }

  function attach(el: HTMLElement): void {
    attachedEl = el;
    el.addEventListener('scroll', handleScroll, { passive: true });

    observer = new MutationObserver(handleMutation);
    observer.observe(el, { childList: true, subtree: true });

    // A container that appears with content already in it (a thread opened
    // from history) starts at the latest turn, not the oldest.
    el.scrollTop = el.scrollHeight;
    isAtBottom.value = true;
  }

  function cleanup(): void {
    if (attachedEl) {
      attachedEl.removeEventListener('scroll', handleScroll);
      attachedEl = null;
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

  // 'post': template refs are filled during patch, so the element is in the
  // DOM (and already holds its children) by the time this runs.
  watch(
    containerRef,
    (el) => {
      if (el === attachedEl) return;
      cleanup();
      if (el) attach(el);
    },
    { flush: 'post', immediate: true },
  );
  onUnmounted(cleanup);

  return {
    containerRef,
    isAtBottom: readonly(isAtBottom),
    scrollToBottom,
  };
}
