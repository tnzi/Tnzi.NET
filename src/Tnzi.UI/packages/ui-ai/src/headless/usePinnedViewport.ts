/**
 * usePinnedViewport - keep a full-screen app shell exactly over the part of
 * the page the user can see, on phones.
 *
 * A chat shell on a phone has one scroller (the conversation) and a composer
 * that must stay at the bottom edge. Sized with `100dvh` that holds on a
 * desktop browser and breaks on a phone in two ways:
 *
 * - the page itself still scrolls: iOS Safari rubber-bands the document when a
 *   drag starts on something that does not scroll (or a scroller at its end),
 *   and the whole shell, composer included, moves with the finger;
 * - with the keyboard open, iOS keeps the layout viewport at full height and
 *   scrolls the page to reveal the focused field, so the shell slides up, the
 *   top bar leaves the screen, and the page can be dragged around under the
 *   keyboard.
 *
 * While `enabled`, this writes the visual viewport's height and top offset to
 * `target` as `--tnzi-ai-viewport-height` / `--tnzi-ai-viewport-top` (the
 * element positions itself with them: `position: fixed`, that top, that
 * height), and sets `overscroll-behavior: none` on the document so a drag that
 * reaches the end of a scroller does not move the page. The previous
 * document value is put back when disabled or when the scope ends.
 *
 * Without `visualViewport` (old browsers, tests) the variables are not set and
 * the element's CSS fallbacks apply.
 */
import { onScopeDispose, watch, type Ref } from 'vue';

export const VIEWPORT_HEIGHT_VAR = '--tnzi-ai-viewport-height';
export const VIEWPORT_TOP_VAR = '--tnzi-ai-viewport-top';

export function usePinnedViewport(target: Ref<HTMLElement | null>, enabled: Ref<boolean>): void {
  let stop: (() => void) | null = null;

  function start(el: HTMLElement): () => void {
    const root = typeof document === 'undefined' ? null : document.documentElement;
    const previousOverscroll = root?.style.getPropertyValue('overscroll-behavior') ?? '';
    root?.style.setProperty('overscroll-behavior', 'none');

    const vv = typeof window === 'undefined' ? undefined : window.visualViewport ?? undefined;
    const apply = (): void => {
      if (!vv) return;
      el.style.setProperty(VIEWPORT_HEIGHT_VAR, `${Math.round(vv.height)}px`);
      el.style.setProperty(VIEWPORT_TOP_VAR, `${Math.round(vv.offsetTop)}px`);
    };
    apply();
    vv?.addEventListener('resize', apply);
    vv?.addEventListener('scroll', apply);

    return () => {
      vv?.removeEventListener('resize', apply);
      vv?.removeEventListener('scroll', apply);
      el.style.removeProperty(VIEWPORT_HEIGHT_VAR);
      el.style.removeProperty(VIEWPORT_TOP_VAR);
      if (!root) return;
      if (previousOverscroll) root.style.setProperty('overscroll-behavior', previousOverscroll);
      else root.style.removeProperty('overscroll-behavior');
    };
  }

  watch(
    [target, enabled],
    ([el, on]) => {
      stop?.();
      stop = el && on ? start(el) : null;
    },
    { flush: 'post', immediate: true },
  );

  onScopeDispose(() => {
    stop?.();
    stop = null;
  });
}
