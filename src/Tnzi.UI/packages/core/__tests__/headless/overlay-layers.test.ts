import { describe, it, expect, afterEach, vi } from 'vitest';
import { computed } from 'vue';
import {
  createOverlayLayerStack,
  resetOverlayLayerStack,
  useOverlayLayerStack,
} from '../../src/headless/overlay-layers';

const layer = (name: string, payload: unknown = name) => ({ id: Symbol(name), payload });

afterEach(() => {
  resetOverlayLayerStack();
});

describe('overlay layer stack', () => {
  it('puts the most recently opened layer on top', () => {
    const stack = createOverlayLayerStack();
    const settings = layer('settings');
    const confirm = layer('confirm');
    stack.open(settings);
    stack.open(confirm);

    expect(stack.top()).toBe(confirm);
    expect(stack.isTop(confirm.id)).toBe(true);
    expect(stack.isTop(settings.id)).toBe(false);
    expect(stack.isOpen(settings.id)).toBe(true);
  });

  it('hands the top back to the layer below when the top one closes', () => {
    const stack = createOverlayLayerStack();
    const settings = layer('settings');
    const confirm = layer('confirm');
    stack.open(settings);
    stack.open(confirm);
    stack.close(confirm.id);

    expect(stack.top()).toBe(settings);
    expect(stack.isOpen(confirm.id)).toBe(false);
  });

  it('keeps the top layer on top when a layer below it closes', () => {
    const stack = createOverlayLayerStack();
    const settings = layer('settings');
    const confirm = layer('confirm');
    stack.open(settings);
    stack.open(confirm);
    stack.close(settings.id);

    expect(stack.top()).toBe(confirm);
    expect(stack.layers.value).toEqual([confirm]);
  });

  it('moves a layer that opens again to the top, keeping one place for it', () => {
    const stack = createOverlayLayerStack();
    const a = layer('a');
    const b = layer('b');
    stack.open(a);
    stack.open(b);
    stack.open(a);

    expect(stack.layers.value.map((entry) => entry.payload)).toEqual(['b', 'a']);
    expect(stack.isTop(a.id)).toBe(true);
  });

  it('ignores closing a layer that is not open', () => {
    const stack = createOverlayLayerStack();
    const a = layer('a');
    stack.open(a);
    const before = stack.layers.value;
    stack.close(Symbol('unknown'));

    expect(stack.layers.value).toBe(before);
  });

  it('is empty to begin with', () => {
    const stack = createOverlayLayerStack();
    expect(stack.top()).toBeUndefined();
    expect(stack.isTop(Symbol('any'))).toBe(false);
  });

  // The UI package's `isTop` is a computed over `layers`.
  it('is reactive: a computed over it follows layers opening and closing', () => {
    const stack = createOverlayLayerStack();
    const a = layer('a');
    const b = layer('b');
    const aOnTop = computed(() => stack.isTop(a.id));
    stack.open(a);
    expect(aOnTop.value).toBe(true);
    stack.open(b);
    expect(aOnTop.value).toBe(false);
    stack.close(b.id);
    expect(aOnTop.value).toBe(true);
  });
});

describe('the page stack', () => {
  it('is one stack for every caller', () => {
    const a = layer('a');
    useOverlayLayerStack().open(a);
    expect(useOverlayLayerStack().isTop(a.id)).toBe(true);
  });

  // A UI package copy, or a core bundle that inlined this module, must see the
  // same stack: two stacks would put two overlays "on top" at once.
  it('is shared by a second copy of the module (parked on globalThis)', async () => {
    const a = layer('a');
    useOverlayLayerStack().open(a);

    vi.resetModules();
    const copy = await import('../../src/headless/overlay-layers');
    expect(copy.useOverlayLayerStack().isTop(a.id)).toBe(true);
  });

  it('starts empty again after a reset (tests, SSR isolation)', () => {
    useOverlayLayerStack().open(layer('a'));
    resetOverlayLayerStack();
    expect(useOverlayLayerStack().top()).toBeUndefined();
  });
});
