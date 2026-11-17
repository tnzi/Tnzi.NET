/**
 * `v-scroll-hint` on a box that scrolls when its content is long (a card's command, a plan, a
 * stack of cards): while more is hidden below, the box carries `data-more-below`, and its styles
 * fade the last lines out, so a cut-off code block reads as "scroll for more", not as broken.
 */
import type { Directive } from 'vue';

/** Pixels of hidden content below which the box counts as showing everything (rounding). */
const SLACK = 2;

interface Watched {
  update: () => void;
  observer: ResizeObserver | null;
}

const watched = new WeakMap<HTMLElement, Watched>();

/** Whether part of the box's content is out of view below. */
export function hasMoreBelow(box: Pick<HTMLElement, 'scrollHeight' | 'scrollTop' | 'clientHeight'>): boolean {
  return box.scrollHeight - box.scrollTop - box.clientHeight > SLACK;
}

function attach(el: HTMLElement): void {
  const update = () => el.toggleAttribute('data-more-below', hasMoreBelow(el));
  const observer = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(update);
  observer?.observe(el);
  for (const child of Array.from(el.children)) observer?.observe(child);
  el.addEventListener('scroll', update, { passive: true });
  watched.set(el, { update, observer });
  update();
}

export const vScrollHint: Directive<HTMLElement> = {
  mounted: attach,
  // The content changed (a plan rendered, a card reused): measure again.
  updated: (el) => watched.get(el)?.update(),
  unmounted(el) {
    const state = watched.get(el);
    if (!state) return;
    el.removeEventListener('scroll', state.update);
    state.observer?.disconnect();
    watched.delete(el);
  },
};
