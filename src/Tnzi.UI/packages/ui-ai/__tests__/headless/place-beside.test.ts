import { describe, expect, it } from 'vitest';
import { placeBeside } from '../../src/headless/place-beside';

const viewport = { width: 1000, height: 800 };
// A 40px rail button at the left edge of the window.
const railButton = { top: 100, left: 8, width: 40, height: 40 };

describe('placeBeside', () => {
  it('puts a tooltip to the right of a rail button, centred on it', () => {
    const at = placeBeside(railButton, { width: 80, height: 24 }, viewport);
    expect(at).toEqual({ side: 'right', left: 8 + 40 + 8, top: 100 + (40 - 24) / 2 });
  });

  it('lines a popover up with the right edge of its button with align end', () => {
    const toolbarButton = { top: 100, left: 600, width: 30, height: 30 };
    const at = placeBeside(toolbarButton, { width: 200, height: 120 }, viewport, { side: 'bottom', align: 'end', gap: 6 });
    expect(at).toEqual({ side: 'bottom', left: 630 - 200, top: 136 });
    const beside = placeBeside(toolbarButton, { width: 200, height: 20 }, viewport, { side: 'right', align: 'end', gap: 6 });
    expect(beside.top).toBe(130 - 20);
  });

  it('lines a flyout up with the top of its button when asked to', () => {
    const at = placeBeside(railButton, { width: 280, height: 300 }, viewport, { align: 'start', gap: 0 });
    expect(at).toEqual({ side: 'right', left: 48, top: 100 });
  });

  it('flips to the other side when the preferred one has no room and the other has', () => {
    const nearRightEdge = { top: 100, left: 940, width: 40, height: 40 };
    const at = placeBeside(nearRightEdge, { width: 120, height: 24 }, viewport);
    expect(at.side).toBe('left');
    expect(at.left).toBe(940 - 8 - 120);
  });

  it('keeps the preferred side when neither fits, and clamps into the window', () => {
    const at = placeBeside(railButton, { width: 2000, height: 24 }, viewport);
    expect(at.side).toBe('right');
    expect(at.left).toBe(8);
  });

  it('keeps a tall panel inside the bottom of the window', () => {
    const low = { top: 700, left: 8, width: 40, height: 40 };
    const at = placeBeside(low, { width: 280, height: 400 }, viewport, { align: 'start' });
    expect(at.top).toBe(800 - 400 - 8);
  });

  it('places above and below, centred horizontally', () => {
    const button = { top: 400, left: 500, width: 40, height: 40 };
    expect(placeBeside(button, { width: 60, height: 20 }, viewport, { side: 'bottom' })).toEqual({
      side: 'bottom',
      top: 448,
      left: 490,
    });
    const nearTop = { top: 4, left: 500, width: 40, height: 40 };
    expect(placeBeside(nearTop, { width: 60, height: 20 }, viewport, { side: 'top' }).side).toBe('bottom');
  });
});
