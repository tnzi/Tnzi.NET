/**
 * useAutoScroll - follow the bottom of a chat scroller while the reader is there
 *
 * While the view is at the bottom it is *following*: content that grows (a
 * streaming answer, a tool step arriving, the composer growing, the scroller
 * shrinking under a phone keyboard) keeps the end in view. The scroll is
 * written in the same frame as the growth (ResizeObserver / MutationObserver
 * callbacks run before paint), so nothing animates and nothing jitters.
 *
 * Following stops at once when the reader shows they want to read elsewhere,
 * judged from what they do, not from where the view is: a wheel or touch
 * gesture upward, an upward key (PageUp, ArrowUp, Home, Shift+Space), the view
 * moving up by any other means (dragging the scrollbar), or opening a
 * disclosure inside the content (a tool step, `<summary>`), so the thing
 * opened stays where it was clicked. A gesture taken by a nested scroller that
 * can still move (a long code block) is that scroller's, not this one's, and
 * a view moving up because the content above it shrank is not the reader's.
 * Following resumes when the reader comes back down to within `threshold` of
 * the bottom, or on `scrollToBottom()`.
 *
 * `followTurn(find)` is the conversational variant used after a message is
 * sent: it follows as usual, but no further than the point where `find()`'s
 * element (the sent message) reaches the top of the view. The reader sees the
 * question with what came before it, the answer streams in beneath it, and
 * once the answer fills the view it stops moving, so a long answer is read
 * from its start. If the reader scrolls past that point to the bottom
 * themselves, the limit is lifted and the bottom is followed again.
 *
 * Automatic scrolling never moves the view up.
 *
 * `follow: false` keeps everything except the following: the container still
 * opens at the bottom and `isAtBottom` still tracks new content, but growing
 * content never moves the view.
 *
 * Follows the element bound to `containerRef` wherever it comes and goes: a
 * container rendered after mount (behind a `v-if`, such as a thread that only
 * appears once there are messages) or re-created later is attached when it
 * appears, opened at the bottom, and the previous one released.
 */

import { ref, readonly, watch, onScopeDispose, type Ref, type DeepReadonly } from 'vue';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/**
 * How a chat thread scrolls when a message is sent and its answer streams.
 *
 * - `'chat'`: the sent message comes into view with the end of the previous
 *   answer above it, the view follows the answer until the message reaches
 *   the top, then stays (`followTurn`).
 * - `'follow'`: the view follows the bottom while output streams, the way a
 *   terminal or a coding agent's log does (`scrollToBottom`).
 */
export type ChatScrollMode = 'chat' | 'follow';

export interface UseAutoScrollOptions {
  /**
   * Distance from the bottom (px) within which the view counts as at the
   * bottom: `isAtBottom` is true and following resumes. Default: 50
   */
  threshold?: number;
  /** Scroll to new content while following. Default: true */
  follow?: boolean;
  /**
   * Selector for parts of the scroller that are not conversation (a sticky
   * composer inside it): keys pressed and disclosures opened there do not
   * stop following.
   */
  notContent?: string;
  /**
   * Where a container that appears opens, in px from its top; `null` opens it
   * at the latest turn as usual. Asked each time a container is attached. A
   * position the content is not yet tall enough for is applied again as the
   * content grows, until it is reached or the reader scrolls; the view does
   * not follow from there until the reader comes back down to the bottom.
   * For a conversation returned to from another page, to stay where it was.
   */
  openAt?: () => number | null;
}

export interface FollowTurnOptions {
  /** Room kept above the element when following stops at it, in px. Default: 16 */
  topOffset?: number | (() => number);
}

export interface UseAutoScrollReturn {
  /** Template ref to bind to the scrollable container element. */
  containerRef: Ref<HTMLElement | null>;
  /** Whether the container is currently scrolled to (within `threshold` of) the bottom. */
  isAtBottom: DeepReadonly<Ref<boolean>>;
  /** Whether growing content currently moves the view. */
  following: DeepReadonly<Ref<boolean>>;
  /** Scroll to the bottom and follow it from there (lifts a `followTurn` limit). */
  scrollToBottom: (behavior?: ScrollBehavior) => void;
  /**
   * Follow, but no further than the point where `find()`'s element reaches
   * the top of the view. Call it once the element is rendered.
   */
  followTurn: (find: () => HTMLElement | null, options?: FollowTurnOptions) => void;
  /** Stop following until the reader comes back to the bottom. */
  stopFollowing: () => void;
}

export interface FollowMetrics {
  scrollTop: number;
  scrollHeight: number;
  clientHeight: number;
  /** The furthest the view may go, in scroll coordinates; null = the bottom. */
  limitTop: number | null;
}

/**
 * Where a following view should be scrolled to, or null to leave it: the
 * bottom, or the limit when that comes first. Never above where the view is,
 * so following can only move the view down.
 */
export function followTop(m: FollowMetrics): number | null {
  const bottom = Math.max(0, m.scrollHeight - m.clientHeight);
  const target = m.limitTop == null ? bottom : Math.min(bottom, Math.max(0, m.limitTop));
  return target > m.scrollTop + 0.5 ? target : null;
}

const UP_KEYS = new Set(['ArrowUp', 'PageUp', 'Home']);
const DOWN_KEYS = new Set(['ArrowDown', 'PageDown', 'End']);
const DISCLOSURE = '[aria-expanded], summary';
/** Longest a smooth scroll to the bottom is waited for without a `scrollend`. */
const GLIDE_FALLBACK_MS = 1000;
/** Finger travel (px) between two touch moves that counts as a direction. */
const TOUCH_SLOP = 2;

function isEditable(el: Element | null): boolean {
  if (!el) return false;
  if (el instanceof HTMLInputElement || el instanceof HTMLTextAreaElement || el instanceof HTMLSelectElement) return true;
  return el instanceof HTMLElement && el.isContentEditable;
}

/** An element between `target` and `root` (exclusive) can still scroll up and takes the gesture. */
function nestedCanScrollUp(target: EventTarget | null, root: HTMLElement): boolean {
  let node = target instanceof Element ? target : null;
  while (node && node !== root) {
    if (node instanceof HTMLElement && node.scrollTop > 0 && node.scrollHeight > node.clientHeight + 1) {
      const overflowY = getComputedStyle(node).overflowY;
      if (overflowY === 'auto' || overflowY === 'scroll') return true;
    }
    node = node.parentElement;
  }
  return false;
}

// ---------------------------------------------------------------------------
// Composable
// ---------------------------------------------------------------------------

export function useAutoScroll(options: UseAutoScrollOptions = {}): UseAutoScrollReturn {
  const { threshold = 50, follow = true, notContent, openAt } = options;

  const containerRef = ref<HTMLElement | null>(null);
  const isAtBottom = ref(true);
  const following = ref(true);

  /* The element is captured when it is attached rather than re-read from
     `containerRef` during cleanup: Vue nulls template refs before unmount
     cleanup runs, so reading the ref there would skip removeEventListener
     entirely and leak the handlers. */
  let attachedEl: HTMLElement | null = null;
  let mutationObserver: MutationObserver | null = null;
  let resizeObserver: ResizeObserver | null = null;
  const observedChildren = new Set<Element>();

  /** The last user intent was upward: being near the bottom does not resume following. */
  let upIntent = false;
  /** scrollTop after the last scroll event, to tell which way the view moved. */
  let lastTop = 0;
  /** scrollTop this composable wrote last, so its own scroll event is recognised. */
  let writtenTop: number | null = null;
  /* A smooth scroll to the bottom is in flight. Growth does not re-aim it: a
     smooth scroll re-issued on every streamed chunk restarts its easing each
     time and crawls (measured: still 385px short after 900ms). It lands where
     it was aimed, and the bottom is caught up with at once when it ends. */
  let gliding = false;
  let glideTimer: ReturnType<typeof setTimeout> | null = null;
  let limitFind: (() => HTMLElement | null) | null = null;
  let limitOffset: FollowTurnOptions['topOffset'];
  let touchY: number | null = null;
  /* Where an `openAt` container is to be, while its content is not yet tall
     enough to get there; dropped on the reader's first scroll input. */
  let pendingOpenTop: number | null = null;

  function distanceFromBottom(el: HTMLElement): number {
    return el.scrollHeight - el.scrollTop - el.clientHeight;
  }

  function limitTopOf(el: HTMLElement): number | null {
    const target = limitFind?.() ?? null;
    if (!target) return null;
    const offset = typeof limitOffset === 'function' ? limitOffset() : (limitOffset ?? 16);
    return target.getBoundingClientRect().top - el.getBoundingClientRect().top + el.scrollTop - offset;
  }

  function write(el: HTMLElement, top: number): void {
    el.scrollTop = top;
    writtenTop = el.scrollTop;
    lastTop = el.scrollTop;
  }

  function pin(): void {
    const el = attachedEl;
    if (!el || !follow || !following.value) return;
    const target = followTop({
      scrollTop: el.scrollTop,
      scrollHeight: el.scrollHeight,
      clientHeight: el.clientHeight,
      limitTop: limitTopOf(el),
    });
    if (target == null || gliding) return;
    write(el, target);
  }

  function measure(): void {
    const el = attachedEl;
    if (el) isAtBottom.value = distanceFromBottom(el) <= threshold;
  }

  function endGlide(): void {
    if (glideTimer != null) clearTimeout(glideTimer);
    glideTimer = null;
    if (!gliding) return;
    gliding = false;
    pin();
    measure();
  }

  function stopFollowing(): void {
    following.value = false;
    upIntent = true;
    endGlide();
  }

  /** Back within reach of the bottom without an upward intent: follow again. */
  function maybeResume(own: boolean): void {
    const el = attachedEl;
    if (!el || upIntent || distanceFromBottom(el) > threshold) return;
    following.value = true;
    // The reader went past the sent message's start to the bottom on their
    // own: they want the newest, so the limit no longer applies.
    if (!own && limitFind) {
      const limitTop = limitTopOf(el);
      if (limitTop == null || el.scrollTop > limitTop + 1) limitFind = null;
    }
  }

  function onContentChange(): void {
    const el = attachedEl;
    if (el && pendingOpenTop != null) {
      write(el, pendingOpenTop);
      if (Math.abs(el.scrollTop - pendingOpenTop) < 1) pendingOpenTop = null;
    }
    pin();
    measure();
  }

  function handleScroll(): void {
    const el = attachedEl;
    if (!el) return;
    const top = el.scrollTop;
    const own = writtenTop != null && Math.abs(top - writtenTop) < 1;
    writtenTop = null;
    if (!own) {
      // Moving up while not at the bottom is the reader's doing. At the bottom
      // it is the content shrinking and the browser clamping the view.
      if (top < lastTop - 1 && distanceFromBottom(el) > 1) stopFollowing();
      else if (top > lastTop + 1) upIntent = false;
    }
    lastTop = top;
    measure();
    maybeResume(own);
  }

  function isOutsideContent(target: EventTarget | null): boolean {
    return !!notContent && target instanceof Element && !!target.closest(notContent);
  }

  function intendUp(el: HTMLElement, target: EventTarget | null): void {
    if (el.scrollTop <= 0 || nestedCanScrollUp(target, el)) return;
    stopFollowing();
  }

  function intendDown(): void {
    upIntent = false;
    maybeResume(false);
  }

  function handleWheel(event: WheelEvent): void {
    const el = attachedEl;
    if (!el) return;
    pendingOpenTop = null;
    if (event.deltaY < 0) intendUp(el, event.target);
    else if (event.deltaY > 0) intendDown();
  }

  function handleTouchStart(event: TouchEvent): void {
    pendingOpenTop = null;
    touchY = event.touches[0]?.clientY ?? null;
  }

  function handleTouchMove(event: TouchEvent): void {
    const el = attachedEl;
    const y = event.touches[0]?.clientY;
    if (!el || y == null) return;
    if (touchY != null) {
      // A finger moving down drags the content down: the view goes up.
      if (y - touchY > TOUCH_SLOP) intendUp(el, event.target);
      else if (touchY - y > TOUCH_SLOP) intendDown();
    }
    touchY = y;
  }

  function handleKeydown(event: KeyboardEvent): void {
    const el = attachedEl;
    if (!el || isEditable(event.target as Element | null) || isOutsideContent(event.target)) return;
    pendingOpenTop = null;
    const disclosure = event.target instanceof Element && event.target.closest(DISCLOSURE);
    if (disclosure && (event.key === 'Enter' || event.key === ' ')) {
      stopFollowing();
      return;
    }
    if (UP_KEYS.has(event.key) || (event.key === ' ' && event.shiftKey)) intendUp(el, event.target);
    else if (DOWN_KEYS.has(event.key) || event.key === ' ') upIntent = false;
  }

  function handlePointerDown(event: PointerEvent): void {
    if (isOutsideContent(event.target)) return;
    pendingOpenTop = null;
    const target = event.target instanceof Element ? event.target : null;
    if (target?.closest(DISCLOSURE)) stopFollowing();
  }

  function observeChildren(el: HTMLElement): void {
    if (!resizeObserver) return;
    const current = new Set(Array.from(el.children));
    for (const child of observedChildren) {
      if (!current.has(child)) {
        resizeObserver.unobserve(child);
        observedChildren.delete(child);
      }
    }
    for (const child of current) {
      if (!observedChildren.has(child)) {
        resizeObserver.observe(child);
        observedChildren.add(child);
      }
    }
  }

  function attach(el: HTMLElement): void {
    attachedEl = el;
    el.addEventListener('scroll', handleScroll, { passive: true });
    el.addEventListener('wheel', handleWheel, { passive: true });
    el.addEventListener('touchstart', handleTouchStart, { passive: true });
    el.addEventListener('touchmove', handleTouchMove, { passive: true });
    el.addEventListener('keydown', handleKeydown);
    el.addEventListener('pointerdown', handlePointerDown);
    el.addEventListener('scrollend', endGlide);

    if (typeof ResizeObserver !== 'undefined') {
      // The scroller itself (window resize, phone keyboard) and each of its
      // children (content growing or shrinking, the composer changing height).
      resizeObserver = new ResizeObserver(onContentChange);
      resizeObserver.observe(el);
      observeChildren(el);
    }
    mutationObserver = new MutationObserver(() => {
      observeChildren(el);
      onContentChange();
    });
    mutationObserver.observe(el, { childList: true, subtree: true, characterData: true });

    // A container that appears with content already in it (a thread opened
    // from history) starts at the latest turn, not the oldest. A turn sent as
    // the container appears (the first message of a new conversation mounts
    // it, and the send is seen before the element is) keeps its limit.
    gliding = false;
    pendingOpenTop = null;
    const openTop = openAt?.() ?? null;
    if (openTop != null) {
      // Back where the reader left it: not following, as after scrolling up.
      upIntent = true;
      following.value = false;
      write(el, openTop);
      if (Math.abs(el.scrollTop - openTop) >= 1) pendingOpenTop = openTop;
      measure();
      return;
    }
    upIntent = false;
    following.value = true;
    const limitTop = limitTopOf(el);
    write(el, limitTop == null ? el.scrollHeight : Math.min(el.scrollHeight, Math.max(0, limitTop)));
    measure();
  }

  function cleanup(): void {
    if (attachedEl) {
      attachedEl.removeEventListener('scroll', handleScroll);
      attachedEl.removeEventListener('wheel', handleWheel);
      attachedEl.removeEventListener('touchstart', handleTouchStart);
      attachedEl.removeEventListener('touchmove', handleTouchMove);
      attachedEl.removeEventListener('keydown', handleKeydown);
      attachedEl.removeEventListener('pointerdown', handlePointerDown);
      attachedEl.removeEventListener('scrollend', endGlide);
      attachedEl = null;
    }
    mutationObserver?.disconnect();
    mutationObserver = null;
    resizeObserver?.disconnect();
    resizeObserver = null;
    observedChildren.clear();
    touchY = null;
    pendingOpenTop = null;
    if (glideTimer != null) clearTimeout(glideTimer);
    glideTimer = null;
    gliding = false;
  }

  function scrollToBottom(behavior: ScrollBehavior = 'smooth'): void {
    limitFind = null;
    pendingOpenTop = null;
    upIntent = false;
    following.value = true;
    const el = containerRef.value;
    if (!el) return;
    if (behavior === 'smooth') {
      gliding = true;
      el.scrollTo({ top: el.scrollHeight, behavior });
      // `scrollend` ends it; this covers a browser without the event.
      if (glideTimer != null) clearTimeout(glideTimer);
      glideTimer = setTimeout(endGlide, GLIDE_FALLBACK_MS);
    } else {
      endGlide();
      el.scrollTo({ top: el.scrollHeight, behavior });
      writtenTop = el.scrollTop;
      lastTop = el.scrollTop;
    }
    isAtBottom.value = true;
  }

  function followTurn(find: () => HTMLElement | null, turnOptions: FollowTurnOptions = {}): void {
    limitFind = find;
    limitOffset = turnOptions.topOffset;
    pendingOpenTop = null;
    upIntent = false;
    if (glideTimer != null) clearTimeout(glideTimer);
    glideTimer = null;
    gliding = false;
    following.value = true;
    pin();
    measure();
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
  onScopeDispose(cleanup);

  return {
    containerRef,
    isAtBottom: readonly(isAtBottom),
    following: readonly(following),
    scrollToBottom,
    followTurn,
    stopFollowing,
  };
}
