import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  createSubmenuIntent,
  inSubmenuGraceArea,
  SUBMENU_CLOSE_DELAY_MS,
  SUBMENU_GRACE_TIMEOUT_MS,
  SUBMENU_OPEN_DELAY_MS,
} from '../../src/headless/submenu-intent';

beforeEach(() => vi.useFakeTimers());
afterEach(() => vi.useRealTimers());

/* A menu 300px wide at the left of the window; its rows are 34px tall. The
   submenu of the row at y 100..134 opens to the right, at x 304. */
const submenu = { top: 93, left: 304, width: 220, height: 120 };

function intent() {
  const onChange = vi.fn();
  const rect = vi.fn(() => submenu);
  return { intent: createSubmenuIntent({ submenuRect: rect, onChange }), onChange };
}

describe('inSubmenuGraceArea', () => {
  const exit = { x: 200, y: 134 };

  it('holds a diagonal path towards the submenu, and the submenu itself', () => {
    expect(inSubmenuGraceArea({ x: 220, y: 140 }, exit, submenu)).toBe(true);
    expect(inSubmenuGraceArea({ x: 290, y: 180 }, exit, submenu)).toBe(true);
    expect(inSubmenuGraceArea({ x: 400, y: 150 }, exit, submenu)).toBe(true);
  });

  it('does not hold a path straight down the list, or away from the submenu', () => {
    expect(inSubmenuGraceArea({ x: 200, y: 170 }, exit, submenu)).toBe(false);
    expect(inSubmenuGraceArea({ x: 150, y: 140 }, exit, submenu)).toBe(false);
    expect(inSubmenuGraceArea({ x: 290, y: 260 }, exit, submenu)).toBe(false);
  });

  it('points the other way when the submenu opened on the left', () => {
    const left = { top: 93, left: 0, width: 180, height: 120 };
    const from = { x: 220, y: 134 };
    expect(inSubmenuGraceArea({ x: 200, y: 150 }, from, left)).toBe(true);
    expect(inSubmenuGraceArea({ x: 240, y: 150 }, from, left)).toBe(false);
  });
});

describe('submenu intent', () => {
  it('resting on an item opens its submenu; sweeping past does not', () => {
    const { intent: menu, onChange } = intent();
    menu.enterItem('language');
    vi.advanceTimersByTime(SUBMENU_OPEN_DELAY_MS - 1);
    menu.enterItem(null);
    vi.advanceTimersByTime(1000);
    expect(onChange).not.toHaveBeenCalled();

    menu.enterItem('language');
    vi.advanceTimersByTime(SUBMENU_OPEN_DELAY_MS);
    expect(menu.openId).toBe('language');
    expect(menu.pinned).toBe(false);
  });

  it('one at a time: resting on another item switches, on a plain row closes', () => {
    const { intent: menu, onChange } = intent();
    menu.enterItem('language');
    vi.advanceTimersByTime(SUBMENU_OPEN_DELAY_MS);
    menu.enterItem('theme');
    vi.advanceTimersByTime(SUBMENU_OPEN_DELAY_MS);
    expect(menu.openId).toBe('theme');
    menu.enterItem(null);
    vi.advanceTimersByTime(SUBMENU_OPEN_DELAY_MS);
    expect(menu.openId).toBeNull();
    expect(onChange.mock.calls.map(([id]) => id)).toEqual(['language', 'theme', null]);
  });

  it('a diagonal into the submenu across other rows keeps it open', () => {
    const { intent: menu, onChange } = intent();
    menu.enterItem('language');
    vi.advanceTimersByTime(SUBMENU_OPEN_DELAY_MS);

    menu.leaveItem('language', { x: 200, y: 134 });
    menu.enterItem('theme');
    menu.move({ x: 230, y: 145 });
    vi.advanceTimersByTime(150);
    menu.move({ x: 280, y: 160 });
    menu.enterItem(null);
    menu.leaveMenus();
    menu.move({ x: 306, y: 165 });
    menu.enterSubmenu();
    vi.advanceTimersByTime(2000);

    expect(menu.openId).toBe('language');
    expect(onChange.mock.calls.map(([id]) => id)).toEqual(['language']);
  });

  it('moving out of the grace area lets the row under the pointer take over', () => {
    const { intent: menu } = intent();
    menu.enterItem('language');
    vi.advanceTimersByTime(SUBMENU_OPEN_DELAY_MS);

    menu.leaveItem('language', { x: 200, y: 134 });
    menu.enterItem('theme');
    menu.move({ x: 200, y: 160 });
    vi.advanceTimersByTime(SUBMENU_OPEN_DELAY_MS);
    expect(menu.openId).toBe('theme');
  });

  it('stopping short of the submenu inside the grace area ends it after the timeout', () => {
    const { intent: menu } = intent();
    menu.enterItem('language');
    vi.advanceTimersByTime(SUBMENU_OPEN_DELAY_MS);

    menu.leaveItem('language', { x: 200, y: 134 });
    menu.enterItem('theme');
    menu.move({ x: 230, y: 145 });
    vi.advanceTimersByTime(SUBMENU_GRACE_TIMEOUT_MS - 1);
    expect(menu.openId).toBe('language');
    vi.advanceTimersByTime(1 + SUBMENU_OPEN_DELAY_MS);
    expect(menu.openId).toBe('theme');
  });

  it('leaving both closes it after the close delay; coming back keeps it', () => {
    const { intent: menu } = intent();
    menu.enterItem('language');
    vi.advanceTimersByTime(SUBMENU_OPEN_DELAY_MS);
    menu.enterSubmenu();

    menu.leaveMenus();
    vi.advanceTimersByTime(SUBMENU_CLOSE_DELAY_MS - 50);
    menu.enterSubmenu();
    vi.advanceTimersByTime(1000);
    expect(menu.openId).toBe('language');

    menu.leaveMenus();
    vi.advanceTimersByTime(SUBMENU_CLOSE_DELAY_MS - 50);
    menu.enterMenus();
    vi.advanceTimersByTime(1000);
    expect(menu.openId).toBe('language');

    menu.leaveMenus();
    vi.advanceTimersByTime(SUBMENU_CLOSE_DELAY_MS);
    expect(menu.openId).toBeNull();
  });

  it('opened by a click or a key, it is pinned: leaving does not close it', () => {
    const { intent: menu } = intent();
    menu.openNow('language');
    expect(menu.pinned).toBe(true);
    menu.leaveMenus();
    vi.advanceTimersByTime(5000);
    expect(menu.openId).toBe('language');

    menu.enterItem('theme');
    vi.advanceTimersByTime(SUBMENU_OPEN_DELAY_MS);
    expect(menu.openId).toBe('theme');
    expect(menu.pinned).toBe(false);
  });

  it('close and destroy stop every pending change', () => {
    const { intent: menu, onChange } = intent();
    menu.enterItem('language');
    menu.destroy();
    vi.advanceTimersByTime(1000);
    expect(onChange).not.toHaveBeenCalled();

    menu.openNow('language');
    menu.enterItem('theme');
    menu.close();
    vi.advanceTimersByTime(1000);
    expect(menu.openId).toBeNull();
  });
});
