/**
 * useLatestInView - is the end of the latest message on screen?
 *
 * Drives a chat's "scroll to the latest" button. The question it answers is
 * about the conversation, not the scroller: whether the end of the last
 * message is below the visible part of the view. Two things make "scrolled to
 * the bottom of the scroller" the wrong question:
 *
 * - the scroller can hold room that is not conversation: the reserve under a
 *   just-sent message (`useTurnAnchor`), which lets it sit at the top of the
 *   view. The end of the scroll area is then well below the last message, and
 *   a button measured against it stays up with the whole reply in view;
 * - the visible part ends above the scroller's edge when something covers it
 *   (a sticky composer), so `viewportBottom` says where it ends.
 *
 * The answer is measured again whenever it can change: on scroll, and on any
 * change of size of the scroller, its content or the latest message (a tool
 * step or code block opened or closed, an image loading, the window resizing,
 * the reserve growing or shrinking). Listening to scroll events and DOM
 * insertions alone missed all of those: a step collapsed with nothing scrolled
 * or inserted left the button up.
 */
import { onScopeDispose, readonly, ref, watch, type DeepReadonly, type Ref } from 'vue';

export interface UseLatestInViewOptions {
  /** The scrolling element. Its first child is observed as the content. */
  container: Ref<HTMLElement | null>;
  /** The latest message's element (looked up on every measure). */
  latest: () => HTMLElement | null;
  /**
   * Where the visible part of the view ends, as a viewport y (the top of a
   * sticky composer). Default: the scroller's bottom edge.
   */
  viewportBottom?: () => number | null | undefined;
  /** Pixels of the message that may be hidden and still count as in view. Default: 4 */
  threshold?: number;
  /** Space left under the message's end when scrolling to it. Default: 16 */
  scrollMargin?: number;
}

export interface UseLatestInViewReturn {
  /** The end of the latest message is below the visible part of the view. */
  hiddenBelow: DeepReadonly<Ref<boolean>>;
  /** Measure now (the observers do this on their own). */
  measure: () => void;
  /** Scroll just far enough to bring the end of the latest message into view. */
  scrollToLatest: (behavior?: ScrollBehavior) => void;
}

/** How far (px) the end of the latest message lies below the visible part of the view; 0 when in view. */
export function hiddenDistance(latestBottom: number, visibleBottom: number): number {
  return Math.max(0, latestBottom - visibleBottom);
}

export function useLatestInView(options: UseLatestInViewOptions): UseLatestInViewReturn {
  const { container, latest } = options;
  const threshold = options.threshold ?? 4;
  const scrollMargin = options.scrollMargin ?? 16;

  const hiddenBelow = ref(false);
  let resizeObserver: ResizeObserver | null = null;
  let mutationObserver: MutationObserver | null = null;
  let observedLatest: HTMLElement | null = null;
  let attached: HTMLElement | null = null;
  let frame: number | null = null;

  function visibleBottomOf(el: HTMLElement): number {
    return options.viewportBottom?.() ?? el.getBoundingClientRect().bottom;
  }

  function distance(): number {
    const el = container.value;
    const target = latest();
    if (!el || !target) return 0;
    return hiddenDistance(target.getBoundingClientRect().bottom, visibleBottomOf(el));
  }

  function followLatest(target: HTMLElement | null): void {
    if (target === observedLatest || !resizeObserver) return;
    if (observedLatest) resizeObserver.unobserve(observedLatest);
    observedLatest = target;
    if (target) resizeObserver.observe(target);
  }

  function measure(): void {
    followLatest(latest());
    hiddenBelow.value = distance() > threshold;
  }

  function schedule(): void {
    if (frame != null) return;
    if (typeof requestAnimationFrame === 'undefined') {
      measure();
      return;
    }
    frame = requestAnimationFrame(() => {
      frame = null;
      measure();
    });
  }

  function scrollToLatest(behavior: ScrollBehavior = 'smooth'): void {
    const el = container.value;
    const below = distance();
    if (!el || below <= 0) return;
    el.scrollTo({ top: el.scrollTop + below + scrollMargin, behavior });
  }

  function detach(): void {
    attached?.removeEventListener('scroll', schedule);
    attached = null;
    resizeObserver?.disconnect();
    resizeObserver = null;
    mutationObserver?.disconnect();
    mutationObserver = null;
    observedLatest = null;
    if (frame != null && typeof cancelAnimationFrame !== 'undefined') cancelAnimationFrame(frame);
    frame = null;
  }

  function attach(el: HTMLElement): void {
    attached = el;
    el.addEventListener('scroll', schedule, { passive: true });
    if (typeof ResizeObserver !== 'undefined') {
      resizeObserver = new ResizeObserver(schedule);
      resizeObserver.observe(el);
      if (el.firstElementChild) resizeObserver.observe(el.firstElementChild);
    }
    // A new latest message (or the content re-rendered) is picked up here;
    // its size changes then come through the ResizeObserver.
    if (typeof MutationObserver !== 'undefined') {
      mutationObserver = new MutationObserver(schedule);
      mutationObserver.observe(el, { childList: true, subtree: true });
    }
    measure();
  }

  watch(
    container,
    (el) => {
      if (el === attached) return;
      detach();
      if (el) attach(el);
      else hiddenBelow.value = false;
    },
    { flush: 'post', immediate: true },
  );
  onScopeDispose(detach);

  return { hiddenBelow: readonly(hiddenBelow), measure, scrollToLatest };
}
