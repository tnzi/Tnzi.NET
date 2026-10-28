/**
 * useTurnAnchor - bring a just-sent message to the top of a chat scroller and
 * let the reply grow beneath it.
 *
 * The reading model of claude.ai: when a message is sent, it is scrolled to
 * near the top of the view and the answer streams downward under it; the view
 * does not chase the end of the answer, so a long one is read from its start.
 * A message near the end of a short thread cannot be scrolled to the top on
 * its own (there is nothing below it to scroll into), so room is reserved
 * after the content: `reserve` is the height, in px, of a spacer the caller
 * renders after the last message. It is exactly what lets the anchored
 * message reach the top, and it shrinks as the reply grows into it, so the
 * total height stays put and nothing moves while the reply streams. Once the
 * reply is taller than the view, the reserve is 0.
 *
 * The scroll happens once, when `anchor` is called. After that the position
 * is the user's: scrolling during the reply is never undone. `release` drops
 * the reserve (opening another thread, starting a new one).
 *
 * The anchored element is looked up through a function each time it is
 * measured, so a re-rendered message is followed. Measuring happens whenever
 * the scroller or its content changes size (ResizeObserver).
 */
import { nextTick, onScopeDispose, readonly, ref, watch, type DeepReadonly, type Ref } from 'vue';

export interface UseTurnAnchorOptions {
  /** The scrolling element. Its first child is observed as the content. */
  container: Ref<HTMLElement | null>;
  /** Room kept above the anchored message, in px. Default: 16 */
  topOffset?: number | (() => number);
}

export interface UseTurnAnchorReturn {
  /** Height (px) of the spacer to render after the content while anchored. */
  reserve: DeepReadonly<Ref<number>>;
  /** A message is anchored (render the spacer). */
  anchored: DeepReadonly<Ref<boolean>>;
  /** Anchor `find()`'s element: reserve room, then scroll it to the top. */
  anchor: (find: () => HTMLElement | null, behavior?: ScrollBehavior) => Promise<void>;
  /** Forget the anchor and drop the reserve. */
  release: () => void;
  /** Re-measure now (the observers normally do this). */
  measure: () => void;
}

export interface TurnAnchorMetrics {
  /** The anchored element's top, in the scroller's content coordinates. */
  anchorTop: number;
  topOffset: number;
  scrollHeight: number;
  clientHeight: number;
  /** The reserve currently rendered (it is part of `scrollHeight`). */
  reserve: number;
}

/**
 * The reserve that lets the anchored element scroll to `topOffset` from the
 * top: the furthest the scroller can go is `scrollHeight - clientHeight`, and
 * it has to reach `anchorTop - topOffset`. The current reserve is part of
 * `scrollHeight`, so the answer is the current one plus the shortfall (or
 * minus the surplus), never below 0. Applying it is a fixed point: measuring
 * again returns the same value until the content changes.
 */
export function reserveForAnchor(m: TurnAnchorMetrics): number {
  const needed = m.anchorTop - m.topOffset;
  const reachable = m.scrollHeight - m.clientHeight;
  return Math.max(0, Math.round(m.reserve + needed - reachable));
}

export function useTurnAnchor(options: UseTurnAnchorOptions): UseTurnAnchorReturn {
  const { container } = options;
  const offsetOf = (): number => {
    const value = typeof options.topOffset === 'function' ? options.topOffset() : options.topOffset;
    return value ?? 16;
  };

  const reserve = ref(0);
  const anchored = ref(false);
  let find: (() => HTMLElement | null) | null = null;
  let observer: ResizeObserver | null = null;
  let observedContent: Element | null = null;

  function anchorTopOf(el: HTMLElement, target: HTMLElement): number {
    return target.getBoundingClientRect().top - el.getBoundingClientRect().top + el.scrollTop;
  }

  function measure(): void {
    const el = container.value;
    const target = find?.() ?? null;
    if (!el || !target || !anchored.value) return;
    reserve.value = reserveForAnchor({
      anchorTop: anchorTopOf(el, target),
      topOffset: offsetOf(),
      scrollHeight: el.scrollHeight,
      clientHeight: el.clientHeight,
      reserve: reserve.value,
    });
  }

  function observe(el: HTMLElement | null): void {
    observer?.disconnect();
    observer = null;
    observedContent = null;
    if (!el || typeof ResizeObserver === 'undefined') return;
    observer = new ResizeObserver(() => measure());
    observer.observe(el);
    observedContent = el.firstElementChild;
    if (observedContent) observer.observe(observedContent);
  }

  watch(container, (el) => observe(el), { flush: 'post', immediate: true });

  async function anchor(target: () => HTMLElement | null, behavior: ScrollBehavior = 'smooth'): Promise<void> {
    find = target;
    anchored.value = true;
    // The content may have been re-created (the first message of a new
    // conversation mounts the scroller), so the observed child is refreshed.
    const el = container.value;
    if (el && el.firstElementChild !== observedContent) observe(el);
    await nextTick();
    measure();
    await nextTick();
    const scroller = container.value;
    const message = find?.() ?? null;
    if (!scroller || !message || find !== target) return;
    scroller.scrollTo({ top: Math.max(0, anchorTopOf(scroller, message) - offsetOf()), behavior });
  }

  function release(): void {
    find = null;
    anchored.value = false;
    reserve.value = 0;
  }

  onScopeDispose(() => {
    observer?.disconnect();
    observer = null;
  });

  return { reserve: readonly(reserve), anchored: readonly(anchored), anchor, release, measure };
}
