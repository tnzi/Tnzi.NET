import { describe, it, expect, afterEach } from 'vitest';
import { preventPageZoom, withViewportValues } from '../../src/headless/preventPageZoom';

afterEach(() => {
  document.head.querySelectorAll('meta[name="viewport"]').forEach((m) => m.remove());
  document.documentElement.removeAttribute('style');
});

describe('withViewportValues', () => {
  it('sets values, keeps the rest and their order, and appends what is missing', () => {
    expect(withViewportValues('width=device-width, initial-scale=1, maximum-scale=5', { 'maximum-scale': '1', 'user-scalable': 'no' }))
      .toBe('width=device-width, initial-scale=1, maximum-scale=1, user-scalable=no');
    expect(withViewportValues('', { 'user-scalable': 'no' })).toBe('user-scalable=no');
  });
});

describe('preventPageZoom', () => {
  it('locks the viewport, the root touch-action and text size, and puts them back', () => {
    const meta = document.createElement('meta');
    meta.name = 'viewport';
    meta.content = 'width=device-width, initial-scale=1, viewport-fit=cover';
    document.head.appendChild(meta);
    document.documentElement.style.touchAction = 'auto';

    const restore = preventPageZoom();
    expect(meta.content).toBe('width=device-width, initial-scale=1, viewport-fit=cover, maximum-scale=1, user-scalable=no');
    expect(document.documentElement.style.touchAction).toBe('pan-x pan-y');
    expect(document.documentElement.style.getPropertyValue('text-size-adjust')).toBe('100%');

    restore();
    expect(meta.content).toBe('width=device-width, initial-scale=1, viewport-fit=cover');
    expect(document.documentElement.style.touchAction).toBe('auto');
    expect(document.documentElement.style.getPropertyValue('text-size-adjust')).toBe('');
  });

  it('creates a viewport meta when the page has none, and removes it again', () => {
    const restore = preventPageZoom();
    expect(document.querySelector<HTMLMetaElement>('meta[name="viewport"]')?.content).toBe(
      'width=device-width, initial-scale=1, maximum-scale=1, user-scalable=no',
    );
    restore();
    expect(document.querySelector('meta[name="viewport"]')).toBeNull();
  });

  it("cancels Safari's gesture events without hiding them from a component that handles pinch itself", () => {
    const restore = preventPageZoom();
    const viewer = document.createElement('div');
    document.body.appendChild(viewer);
    let seen = 0;
    viewer.addEventListener('gesturechange', () => (seen += 1));

    const event = new Event('gesturechange', { bubbles: true, cancelable: true });
    viewer.dispatchEvent(event);
    expect(seen).toBe(1);
    expect(event.defaultPrevented).toBe(true);

    restore();
    const after = new Event('gesturechange', { bubbles: true, cancelable: true });
    viewer.dispatchEvent(after);
    expect(after.defaultPrevented).toBe(false);
    viewer.remove();
  });
});
