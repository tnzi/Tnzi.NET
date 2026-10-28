/**
 * The page's stack of open overlays (dialogs, sheets, palettes, drawers), in the
 * order they opened. The overlay on top is the one that answers the keyboard: a
 * confirmation opened over a settings dialog closes alone on Escape, and the
 * dialog under it does not pull focus back into itself.
 *
 * This module is only the ordering: which layers are open and which one is on
 * top. It does not touch the DOM. Listening for keys and moving focus belong to
 * the UI package that renders the overlays (`@tnzi/ui`'s `useOverlayLayer` and
 * `useFocusTrap`), which keeps whatever it needs per layer in the entry's
 * `payload`.
 *
 * The stack is page-wide by nature, so it lives on the globalThis registry
 * (`createAdapterSingleton`): every bundle that inlines this module, and every
 * copy of a UI package on the page, sees the same stack. Two stacks would put
 * two overlays "on top" at once, which is the defect the stack exists to remove.
 */
import { shallowRef, type ShallowRef } from 'vue';
import { createAdapterSingleton } from '../adapters/singleton';

/** One open overlay: its identity and what the UI layer that opened it keeps for it. */
export interface OverlayLayerEntry<TPayload = unknown> {
  readonly id: symbol;
  readonly payload: TPayload;
}

export interface OverlayLayerStack {
  /** The open layers, oldest first. Reactive: a `computed` reading it follows layers opening and closing. */
  readonly layers: Readonly<ShallowRef<readonly OverlayLayerEntry[]>>;
  /** Put a layer on top. A layer already open moves to the top (it keeps one place in the stack). */
  open(entry: OverlayLayerEntry): void;
  /** Take a layer out of the stack, wherever it is. Unknown ids are ignored. */
  close(id: symbol): void;
  isOpen(id: symbol): boolean;
  /** Whether the layer is open and the topmost one. */
  isTop(id: symbol): boolean;
  /** The topmost open layer, if any. */
  top(): OverlayLayerEntry | undefined;
}

/** A stack of its own: for tests, or for a surface that must not share the page's stack. */
export function createOverlayLayerStack(): OverlayLayerStack {
  const layers = shallowRef<readonly OverlayLayerEntry[]>([]);
  const top = (): OverlayLayerEntry | undefined => layers.value[layers.value.length - 1];
  return {
    layers,
    open(entry) {
      layers.value = [...layers.value.filter((layer) => layer.id !== entry.id), entry];
    },
    close(id) {
      if (!layers.value.some((layer) => layer.id === id)) return;
      layers.value = layers.value.filter((layer) => layer.id !== id);
    },
    isOpen(id) {
      return layers.value.some((layer) => layer.id === id);
    },
    isTop(id) {
      return top()?.id === id;
    },
    top,
  };
}

const pageStack = createAdapterSingleton<OverlayLayerStack>('overlay-layers', createOverlayLayerStack);

/** The page's one overlay stack. */
export function useOverlayLayerStack(): OverlayLayerStack {
  return pageStack.use();
}

/** Drop the page's stack so the next `useOverlayLayerStack()` starts empty (tests, SSR isolation). */
export function resetOverlayLayerStack(): void {
  pageStack.reset();
}
