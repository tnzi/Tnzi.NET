import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  createTooltip,
  isTruncated,
  normalizeTooltip,
  TOOLTIP_CLASS,
  TOOLTIP_DELAY_MS,
  TOOLTIP_SKIP_DELAY_MS,
  TOOLTIP_SIDE_ATTRIBUTE,
  TOOLTIP_LONG_PRESS_MS,
  TOOLTIP_TOUCH_SHOW_MS,
  tooltipSide,
  type TooltipController,
} from '../../src/headless/tooltip';

function pointer(target: Element, type: string, pointerType = 'mouse', at: { x: number; y: number } = { x: 0, y: 0 }): void {
  const event = new Event(type) as PointerEvent;
  Object.defineProperty(event, 'pointerType', { value: pointerType });
  Object.defineProperty(event, 'clientX', { value: at.x });
  Object.defineProperty(event, 'clientY', { value: at.y });
  target.dispatchEvent(event);
}

/* The latest input decides whether focus shows the tooltip (keyboard yes, click no). */
function lastInput(kind: 'keyboard' | 'pointer'): void {
  if (kind === 'keyboard') document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab' }));
  else document.dispatchEvent(new Event('pointerdown'));
}

const bubble = (): HTMLElement | null => document.querySelector(`.${TOOLTIP_CLASS}`);
const showing = (): boolean => bubble()?.hasAttribute('data-open') ?? false;

let clock = Date.parse('2026-01-01T00:00:00Z');
const controllers: TooltipController[] = [];

function button(label: string | null = 'Settings'): HTMLButtonElement {
  const el = document.createElement('button');
  if (label) el.setAttribute('aria-label', label);
  document.body.appendChild(el);
  return el;
}

function tooltip(el: HTMLElement, value: Parameters<typeof createTooltip>[1]): TooltipController {
  const controller = createTooltip(el, value);
  controllers.push(controller);
  return controller;
}

beforeEach(() => {
  vi.useFakeTimers();
  // Each test starts well after the previous tooltip closed, so no skip window carries over.
  clock += 60_000;
  vi.setSystemTime(clock);
});
afterEach(() => {
  controllers.splice(0).forEach((c) => c.destroy());
  vi.useRealTimers();
  document.body.innerHTML = '';
});

describe('normalizeTooltip', () => {
  it('takes a string or options, and treats empty values as none', () => {
    expect(normalizeTooltip('Settings')).toEqual({ text: 'Settings' });
    expect(normalizeTooltip({ text: 'Search', side: 'bottom' })).toEqual({ text: 'Search', side: 'bottom' });
    expect(normalizeTooltip('')).toBeNull();
    expect(normalizeTooltip('   ')).toBeNull();
    expect(normalizeTooltip(false)).toBeNull();
    expect(normalizeTooltip(null)).toBeNull();
    expect(normalizeTooltip(undefined)).toBeNull();
  });
});

describe('tooltip', () => {
  it('opens after the hover delay, not the browser title delay, with the label as text', () => {
    const el = button();
    tooltip(el, 'Settings');
    pointer(el, 'pointerenter');
    vi.advanceTimersByTime(TOOLTIP_DELAY_MS - 1);
    expect(showing()).toBe(false);
    vi.advanceTimersByTime(1);
    expect(showing()).toBe(true);
    expect(bubble()!.textContent).toBe('Settings');
    expect(TOOLTIP_DELAY_MS).toBeLessThanOrEqual(400);
  });

  it('is decoration: aria-hidden, and the control keeps its own accessible name', () => {
    const el = button('Expand sidebar');
    tooltip(el, 'Something else');
    pointer(el, 'pointerenter');
    vi.advanceTimersByTime(TOOLTIP_DELAY_MS);
    expect(bubble()!.getAttribute('aria-hidden')).toBe('true');
    expect(el.getAttribute('aria-label')).toBe('Expand sidebar');
  });

  it('names a control that has no name at all, and takes that name back when it goes', () => {
    const el = button(null);
    const controller = tooltip(el, 'Search');
    expect(el.getAttribute('aria-label')).toBe('Search');
    controller.destroy();
    expect(el.hasAttribute('aria-label')).toBe(false);

    const labelled = button(null);
    labelled.textContent = 'Search';
    tooltip(labelled, 'Find');
    expect(labelled.hasAttribute('aria-label')).toBe(false);
  });

  it('is drawn on <body>, fixed, so a clipping ancestor cannot cut it off, and under the control by default', () => {
    const rail = document.createElement('div');
    rail.style.overflow = 'hidden';
    document.body.appendChild(rail);
    const el = document.createElement('button');
    el.setAttribute('aria-label', 'Home');
    rail.appendChild(el);
    tooltip(el, 'Home');
    pointer(el, 'pointerenter');
    vi.advanceTimersByTime(TOOLTIP_DELAY_MS);
    expect(bubble()!.parentElement).toBe(document.body);
    expect(bubble()!.dataset.side).toBe('bottom');
  });

  it('goes beside the control when asked (a rail button)', () => {
    const el = button();
    tooltip(el, { text: 'Home', side: 'right' });
    pointer(el, 'pointerenter');
    vi.advanceTimersByTime(TOOLTIP_DELAY_MS);
    expect(bubble()!.dataset.side).toBe('right');
  });

  it('takes its side from the nearest data-tooltip-side when it names none; its own side wins', () => {
    const sidebar = document.createElement('aside');
    sidebar.setAttribute(TOOLTIP_SIDE_ATTRIBUTE, 'left');
    const group = document.createElement('div');
    group.setAttribute(TOOLTIP_SIDE_ATTRIBUTE, 'top');
    sidebar.appendChild(group);
    document.body.appendChild(sidebar);
    const middle = () => ({ top: 300, left: 500, right: 540, bottom: 340, width: 40, height: 40, x: 500, y: 300, toJSON: () => ({}) });
    const inSidebar = document.createElement('button');
    const inGroup = document.createElement('button');
    const own = document.createElement('button');
    for (const el of [inSidebar, inGroup, own]) el.getBoundingClientRect = middle;
    sidebar.append(inSidebar, own);
    group.appendChild(inGroup);

    tooltip(inSidebar, 'Files');
    pointer(inSidebar, 'pointerenter');
    vi.advanceTimersByTime(TOOLTIP_DELAY_MS);
    expect(bubble()!.dataset.side).toBe('left');
    pointer(inSidebar, 'pointerleave');

    tooltip(inGroup, 'Search');
    pointer(inGroup, 'pointerenter');
    vi.advanceTimersByTime(TOOLTIP_DELAY_MS);
    expect(bubble()!.dataset.side).toBe('top');
    pointer(inGroup, 'pointerleave');

    tooltip(own, { text: 'Home', side: 'bottom' });
    pointer(own, 'pointerenter');
    vi.advanceTimersByTime(TOOLTIP_DELAY_MS);
    expect(bubble()!.dataset.side).toBe('bottom');
  });

  it('ignores a data-tooltip-side that is not a side', () => {
    const box = document.createElement('div');
    box.setAttribute(TOOLTIP_SIDE_ATTRIBUTE, 'sideways');
    const el = document.createElement('button');
    box.appendChild(el);
    document.body.appendChild(box);
    expect(tooltipSide(el)).toBe('bottom');
    expect(tooltipSide(el, 'right')).toBe('right');
  });

  it('leaving before the delay shows nothing; leaving after hides it', () => {
    const el = button();
    tooltip(el, 'Settings');
    pointer(el, 'pointerenter');
    vi.advanceTimersByTime(100);
    pointer(el, 'pointerleave');
    vi.advanceTimersByTime(1000);
    expect(showing()).toBe(false);

    vi.setSystemTime((clock += 60_000));
    pointer(el, 'pointerenter');
    vi.advanceTimersByTime(TOOLTIP_DELAY_MS);
    expect(showing()).toBe(true);
    pointer(el, 'pointerleave');
    expect(showing()).toBe(false);
  });

  it('a touch never shows it', () => {
    const el = button();
    tooltip(el, 'Settings');
    pointer(el, 'pointerenter', 'touch');
    vi.advanceTimersByTime(1000);
    expect(showing()).toBe(false);
    // The focus a tap gives does not show it either.
    lastInput('pointer');
    el.dispatchEvent(new Event('focus'));
    expect(showing()).toBe(false);
  });

  it('keyboard focus shows it at once, blur hides it', () => {
    const el = button();
    tooltip(el, 'Settings');
    lastInput('keyboard');
    el.dispatchEvent(new Event('focus'));
    expect(showing()).toBe(true);
    el.dispatchEvent(new Event('blur'));
    expect(showing()).toBe(false);
  });

  it('the focus a click gives does not show it', () => {
    const el = button();
    tooltip(el, 'Settings');
    lastInput('pointer');
    el.dispatchEvent(new Event('focus'));
    expect(showing()).toBe(false);
  });

  it('a press hides it, and it stays away until the pointer leaves and comes back', () => {
    const el = button();
    tooltip(el, 'Settings');
    pointer(el, 'pointerenter');
    vi.advanceTimersByTime(TOOLTIP_DELAY_MS);
    pointer(el, 'pointerdown');
    expect(showing()).toBe(false);
    pointer(el, 'pointerenter');
    vi.advanceTimersByTime(1000);
    expect(showing()).toBe(false);
    pointer(el, 'pointerleave');
    vi.setSystemTime((clock += 60_000));
    pointer(el, 'pointerenter');
    vi.advanceTimersByTime(TOOLTIP_DELAY_MS);
    expect(showing()).toBe(true);
  });

  it('Escape dismisses it without moving focus', () => {
    const el = button();
    tooltip(el, 'Settings');
    lastInput('keyboard');
    el.focus();
    el.dispatchEvent(new Event('focus'));
    expect(showing()).toBe(true);
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    expect(showing()).toBe(false);
  });

  it('moving straight to the next control skips the delay', () => {
    const first = button('One');
    const second = button('Two');
    tooltip(first, 'One');
    tooltip(second, 'Two');
    pointer(first, 'pointerenter');
    vi.advanceTimersByTime(TOOLTIP_DELAY_MS);
    pointer(first, 'pointerleave');
    vi.advanceTimersByTime(TOOLTIP_SKIP_DELAY_MS - 50);
    pointer(second, 'pointerenter');
    expect(showing()).toBe(true);
    expect(bubble()!.textContent).toBe('Two');
    // One bubble for the page.
    expect(document.querySelectorAll(`.${TOOLTIP_CLASS}`)).toHaveLength(1);
  });

  it('follows new text while showing, and hides when disabled or emptied', () => {
    const el = button();
    const controller = tooltip(el, 'Settings');
    controller.show();
    controller.update('Preferences');
    expect(bubble()!.textContent).toBe('Preferences');
    controller.update({ text: 'Preferences', disabled: true });
    expect(showing()).toBe(false);
    controller.update('');
    pointer(el, 'pointerenter');
    vi.advanceTimersByTime(1000);
    expect(showing()).toBe(false);
  });

  it('destroy hides it and stops listening', () => {
    const el = button();
    const controller = tooltip(el, 'Settings');
    controller.show();
    expect(controller.shown).toBe(true);
    controller.destroy();
    expect(showing()).toBe(false);
    pointer(el, 'pointerenter');
    vi.advanceTimersByTime(1000);
    expect(showing()).toBe(false);
  });
});

describe('tooltip on a touch screen', () => {
  function clickReaches(el: HTMLElement): boolean {
    let reached = false;
    const listener = () => (reached = true);
    el.addEventListener('click', listener);
    el.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
    el.removeEventListener('click', listener);
    return reached;
  }

  it('a long press shows it, the finger lifting keeps it for a while, and that press does not act', () => {
    const el = button();
    tooltip(el, 'Settings');
    pointer(el, 'pointerdown', 'touch');
    vi.advanceTimersByTime(TOOLTIP_LONG_PRESS_MS - 1);
    expect(showing()).toBe(false);
    vi.advanceTimersByTime(1);
    expect(showing()).toBe(true);
    const menu = new Event('contextmenu', { cancelable: true });
    el.dispatchEvent(menu);
    expect(menu.defaultPrevented).toBe(true);
    pointer(el, 'pointerup', 'touch');
    pointer(el, 'pointerleave', 'touch');
    expect(showing()).toBe(true);
    expect(clickReaches(el)).toBe(false);
    vi.advanceTimersByTime(TOOLTIP_TOUCH_SHOW_MS);
    expect(showing()).toBe(false);
    expect(clickReaches(el)).toBe(true);
  });

  it('a tap shows nothing and acts; a finger that moves is a scroll, not a long press', () => {
    const el = button();
    tooltip(el, 'Settings');
    pointer(el, 'pointerdown', 'touch');
    vi.advanceTimersByTime(100);
    pointer(el, 'pointerup', 'touch');
    vi.advanceTimersByTime(TOOLTIP_LONG_PRESS_MS);
    expect(showing()).toBe(false);
    expect(clickReaches(el)).toBe(true);

    pointer(el, 'pointerdown', 'touch', { x: 10, y: 10 });
    pointer(el, 'pointermove', 'touch', { x: 10, y: 40 });
    vi.advanceTimersByTime(TOOLTIP_LONG_PRESS_MS);
    expect(showing()).toBe(false);
  });

  it('touch: none never opens it by touch; a mouse press still does not start a long press', () => {
    const quiet = button();
    tooltip(quiet, { text: 'Drag', touch: 'none' });
    pointer(quiet, 'pointerdown', 'touch');
    vi.advanceTimersByTime(TOOLTIP_LONG_PRESS_MS);
    expect(showing()).toBe(false);

    const mouse = button('Mouse');
    tooltip(mouse, 'Mouse');
    pointer(mouse, 'pointerdown', 'mouse');
    vi.advanceTimersByTime(TOOLTIP_LONG_PRESS_MS);
    expect(showing()).toBe(false);
  });
});

describe('whenTruncated', () => {
  function sized(el: HTMLElement, scrollWidth: number, clientWidth: number): void {
    Object.defineProperty(el, 'scrollWidth', { configurable: true, value: scrollWidth });
    Object.defineProperty(el, 'clientWidth', { configurable: true, value: clientWidth });
  }
  function labelled(text: string): { el: HTMLButtonElement; label: HTMLSpanElement } {
    const el = button(null);
    const label = document.createElement('span');
    label.className = 'label';
    label.textContent = text;
    el.appendChild(label);
    return { el, label };
  }

  it('isTruncated: only an element whose text is wider than its box', () => {
    const { label } = labelled('someone@example.com');
    sized(label, 200, 120);
    expect(isTruncated(label)).toBe(true);
    sized(label, 120, 120);
    expect(isTruncated(label)).toBe(false);
    expect(isTruncated(null)).toBe(false);
  });

  it('opens with the whole text while the label is cut off', () => {
    const { el, label } = labelled('a-very-long-address@example.com');
    sized(label, 240, 160);
    tooltip(el, { text: 'a-very-long-address@example.com', whenTruncated: '.label' });
    pointer(el, 'pointerenter');
    vi.advanceTimersByTime(TOOLTIP_DELAY_MS);
    expect(showing()).toBe(true);
    expect(bubble()!.textContent).toBe('a-very-long-address@example.com');
  });

  it('stays away while the label fits, and leaves the control its own text as name', () => {
    const { el, label } = labelled('Inbox');
    sized(label, 40, 40);
    tooltip(el, { text: 'Inbox', whenTruncated: '.label' });
    pointer(el, 'pointerenter');
    vi.advanceTimersByTime(TOOLTIP_DELAY_MS);
    expect(showing()).toBe(false);
    expect(el.hasAttribute('aria-label')).toBe(false);
  });
});
