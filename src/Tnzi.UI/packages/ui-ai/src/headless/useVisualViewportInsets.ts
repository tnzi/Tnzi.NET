/**
 * useVisualViewportInsets - how much of a full-window overlay the user cannot
 * see, so its content can keep to the part they can.
 *
 * With the on-screen keyboard open, iOS keeps the layout viewport at full
 * height: an overlay with `position: fixed; inset: 0` still reaches the bottom
 * of the screen, under the keyboard, and the page may be scrolled so that its
 * top is above the screen. `position: fixed` and `dvh` do not see either; only
 * the visual viewport does.
 *
 * While `enabled`, this compares `target`'s box with the visual viewport and
 * writes the parts outside it to `target` as `--tnzi-ai-viewport-inset-top`
 * (above the visible area) and `--tnzi-ai-viewport-inset-bottom` (below it:
 * the keyboard), in px, never negative. The element pads itself with them, so
 * it keeps covering the whole window (a backdrop stays a backdrop) while what
 * it lays out fits the visible part. They follow the visual viewport's
 * `resize` and `scroll` (keyboard opening and closing, the page scrolled under
 * it); `onChange` runs after each write that changed a value.
 *
 * Unlike `usePinnedViewport` (an app shell that owns the page), this touches
 * nothing but `target`: no document styles, so any number of overlays can use
 * it at once and none leaves anything behind. The variables are removed when
 * disabled, when `target` goes away, and when the scope ends.
 *
 * Without `visualViewport` (old browsers, tests) nothing is written and the
 * element's CSS fallbacks apply.
 */
import { onScopeDispose, watch, type MaybeRefOrGetter, type Ref, toValue } from 'vue';

export const VIEWPORT_INSET_TOP_VAR = '--tnzi-ai-viewport-inset-top';
export const VIEWPORT_INSET_BOTTOM_VAR = '--tnzi-ai-viewport-inset-bottom';

export interface ViewportInsets {
  top: number;
  bottom: number;
}

/** What of `box` (layout viewport coordinates) lies above and below the visual viewport. */
export function visualViewportInsets(
  box: { top: number; bottom: number },
  viewport: { offsetTop: number; height: number },
): ViewportInsets {
  return {
    top: Math.max(0, Math.round(viewport.offsetTop - box.top)),
    bottom: Math.max(0, Math.round(box.bottom - (viewport.offsetTop + viewport.height))),
  };
}

export interface UseVisualViewportInsetsOptions {
  /** Runs after the insets changed (the keyboard opened or closed, the page moved under it). */
  onChange?: (insets: ViewportInsets) => void;
}

export function useVisualViewportInsets(
  target: Ref<HTMLElement | null>,
  enabled: MaybeRefOrGetter<boolean>,
  options: UseVisualViewportInsetsOptions = {},
): void {
  let stop: (() => void) | null = null;

  function start(el: HTMLElement, vv: VisualViewport): () => void {
    let last: ViewportInsets | null = null;
    const apply = (): void => {
      const insets = visualViewportInsets(el.getBoundingClientRect(), vv);
      if (last && last.top === insets.top && last.bottom === insets.bottom) return;
      last = insets;
      el.style.setProperty(VIEWPORT_INSET_TOP_VAR, `${insets.top}px`);
      el.style.setProperty(VIEWPORT_INSET_BOTTOM_VAR, `${insets.bottom}px`);
      options.onChange?.(insets);
    };
    apply();
    vv.addEventListener('resize', apply);
    vv.addEventListener('scroll', apply);

    return () => {
      vv.removeEventListener('resize', apply);
      vv.removeEventListener('scroll', apply);
      el.style.removeProperty(VIEWPORT_INSET_TOP_VAR);
      el.style.removeProperty(VIEWPORT_INSET_BOTTOM_VAR);
    };
  }

  watch(
    [target, () => toValue(enabled)],
    ([el, on]) => {
      stop?.();
      stop = null;
      const vv = typeof window === 'undefined' ? undefined : window.visualViewport ?? undefined;
      if (el && on && vv) stop = start(el, vv);
    },
    { flush: 'post', immediate: true },
  );

  onScopeDispose(() => {
    stop?.();
    stop = null;
  });
}
