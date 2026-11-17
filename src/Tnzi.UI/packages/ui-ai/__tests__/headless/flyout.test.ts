import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  createFlyoutController,
  FLYOUT_CLOSE_DELAY_MS,
  FLYOUT_OPEN_DELAY_MS,
} from '../../src/headless/flyout';

beforeEach(() => vi.useFakeTimers());
afterEach(() => vi.useRealTimers());

describe('flyout', () => {
  it('a resting pointer opens it after the open delay; a pointer passing through does not', () => {
    const flyout = createFlyoutController();
    flyout.pointerEnter('mouse');
    vi.advanceTimersByTime(FLYOUT_OPEN_DELAY_MS - 1);
    flyout.pointerLeave();
    vi.advanceTimersByTime(1000);
    expect(flyout.open).toBe(false);

    flyout.pointerEnter('mouse');
    vi.advanceTimersByTime(FLYOUT_OPEN_DELAY_MS);
    expect(flyout.state).toBe('hover');
  });

  it('leaving closes it after the grace period, and coming back within it keeps it open', () => {
    const onChange = vi.fn();
    const flyout = createFlyoutController({ onChange });
    flyout.pointerEnter('mouse');
    vi.advanceTimersByTime(FLYOUT_OPEN_DELAY_MS);

    // Off the button, across the gap, into the panel.
    flyout.pointerLeave();
    vi.advanceTimersByTime(FLYOUT_CLOSE_DELAY_MS - 50);
    expect(flyout.state).toBe('hover');
    flyout.pointerEnter('mouse');
    vi.advanceTimersByTime(1000);
    expect(flyout.state).toBe('hover');
    // It never closed in between (closing and reopening would also end on 'hover').
    expect(onChange.mock.calls.map(([state]) => state)).toEqual(['hover']);

    flyout.pointerLeave();
    vi.advanceTimersByTime(FLYOUT_CLOSE_DELAY_MS);
    expect(flyout.open).toBe(false);
  });

  it('touch never opens it on hover', () => {
    const flyout = createFlyoutController();
    flyout.pointerEnter('touch');
    vi.advanceTimersByTime(1000);
    expect(flyout.open).toBe(false);
  });

  it('a click pins it: leaving no longer closes it, a second click does', () => {
    const flyout = createFlyoutController();
    flyout.click();
    expect(flyout.state).toBe('pinned');
    flyout.pointerLeave();
    vi.advanceTimersByTime(1000);
    expect(flyout.state).toBe('pinned');
    flyout.click();
    expect(flyout.open).toBe(false);
  });

  it('clicking the button of a flyout the hover opened pins it rather than closing it', () => {
    const flyout = createFlyoutController();
    flyout.pointerEnter('mouse');
    vi.advanceTimersByTime(FLYOUT_OPEN_DELAY_MS);
    flyout.click();
    expect(flyout.state).toBe('pinned');
  });

  it('keyboard focus opens it at once; focus that a click gives does not; focus leaving closes it', () => {
    const flyout = createFlyoutController();
    flyout.focusIn(false);
    expect(flyout.open).toBe(false);
    flyout.focusIn(true);
    expect(flyout.state).toBe('focus');
    flyout.focusOut();
    expect(flyout.open).toBe(false);
  });

  it('a hover-opened flyout is not closed by focus leaving, only by the pointer', () => {
    const flyout = createFlyoutController();
    flyout.pointerEnter('mouse');
    vi.advanceTimersByTime(FLYOUT_OPEN_DELAY_MS);
    flyout.focusOut();
    expect(flyout.state).toBe('hover');
  });

  it('close() closes from any state and cancels pending timers', () => {
    const onChange = vi.fn();
    const flyout = createFlyoutController({ onChange });
    flyout.pointerEnter('mouse');
    flyout.close();
    vi.advanceTimersByTime(1000);
    expect(flyout.open).toBe(false);
    expect(onChange).not.toHaveBeenCalled();

    flyout.openPinned();
    flyout.close();
    expect(onChange.mock.calls.map(([state]) => state)).toEqual(['pinned', 'closed']);
  });

  it('takes its own delays', () => {
    const flyout = createFlyoutController({ openDelay: 0, closeDelay: 10 });
    flyout.pointerEnter('mouse');
    vi.advanceTimersByTime(0);
    expect(flyout.open).toBe(true);
    flyout.pointerLeave();
    vi.advanceTimersByTime(10);
    expect(flyout.open).toBe(false);
  });
});
