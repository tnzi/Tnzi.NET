import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { createApp, defineComponent, h, withDirectives } from 'vue';
import {
  createIconMotion,
  vIconMotion,
  isIconMotion,
  ICON_MOTIONS,
  ICON_MOTION_MAX_MS,
  ICON_MOTION_HOST_ATTR,
} from '../../src/headless/icon-motion';

const PLAY = 'data-icon-motion-play';

function row(motion: string | null = 'ring'): { host: HTMLElement; icon: HTMLElement } {
  const host = document.createElement('button');
  const icon = document.createElement('span');
  if (motion !== null) icon.setAttribute('data-icon-motion', motion);
  host.appendChild(icon);
  return { host, icon };
}

function pointer(target: HTMLElement, type: 'pointerenter' | 'pointerleave', pointerType = 'mouse'): void {
  const event = new Event(type) as PointerEvent;
  Object.defineProperty(event, 'pointerType', { value: pointerType });
  target.dispatchEvent(event);
}

function animationEnd(target: HTMLElement, name: string, type = 'animationend'): void {
  const event = new Event(type, { bubbles: true }) as AnimationEvent;
  Object.defineProperty(event, 'animationName', { value: name });
  target.dispatchEvent(event);
}

const motionOk = { prefersReducedMotion: () => false };

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
});

afterEach(() => {
  vi.useRealTimers();
});

describe('isIconMotion', () => {
  it('accepts the built-in names only', () => {
    for (const m of ICON_MOTIONS) expect(isIconMotion(m)).toBe(true);
    expect(isIconMotion('wobble')).toBe(false);
    expect(isIconMotion(undefined)).toBe(false);
  });
});

describe('createIconMotion', () => {
  it('marks the host, and plays once per pointer entry', () => {
    const { host, icon } = row();
    createIconMotion(host, motionOk);
    expect(host.hasAttribute(ICON_MOTION_HOST_ATTR)).toBe(true);

    pointer(host, 'pointerenter');
    expect(host.hasAttribute(PLAY)).toBe(true);

    animationEnd(icon, 't-icon-motion-ring');
    expect(host.hasAttribute(PLAY)).toBe(false);

    // Still hovering: nothing replays until the pointer leaves and comes back.
    vi.advanceTimersByTime(ICON_MOTION_MAX_MS * 2);
    expect(host.hasAttribute(PLAY)).toBe(false);

    pointer(host, 'pointerleave');
    pointer(host, 'pointerenter');
    expect(host.hasAttribute(PLAY)).toBe(true);
  });

  it('re-entering mid-motion does not restart it', () => {
    const { host } = row();
    createIconMotion(host, motionOk);
    pointer(host, 'pointerenter');
    const setSpy = vi.spyOn(host, 'setAttribute');
    pointer(host, 'pointerleave');
    pointer(host, 'pointerenter');
    expect(setSpy).not.toHaveBeenCalledWith(PLAY, '');
  });

  it('ignores other animations ending inside the row', () => {
    const { host, icon } = row();
    createIconMotion(host, motionOk);
    pointer(host, 'pointerenter');
    animationEnd(icon, 'spin-loader');
    expect(host.hasAttribute(PLAY)).toBe(true);
    animationEnd(icon, 't-icon-motion-ring', 'animationcancel');
    expect(host.hasAttribute(PLAY)).toBe(false);
  });

  it('clears the trigger by the time limit even if animationend never arrives', () => {
    const { host } = row();
    createIconMotion(host, motionOk);
    pointer(host, 'pointerenter');
    vi.advanceTimersByTime(ICON_MOTION_MAX_MS - 1);
    expect(host.hasAttribute(PLAY)).toBe(true);
    vi.advanceTimersByTime(1);
    expect(host.hasAttribute(PLAY)).toBe(false);
  });

  it('never plays for touch', () => {
    const { host } = row();
    createIconMotion(host, motionOk);
    pointer(host, 'pointerenter', 'touch');
    expect(host.hasAttribute(PLAY)).toBe(false);
  });

  it('never plays under reduced motion', () => {
    const { host } = row();
    createIconMotion(host, { prefersReducedMotion: () => true });
    pointer(host, 'pointerenter');
    expect(host.hasAttribute(PLAY)).toBe(false);
  });

  it('does nothing when the icon names no motion, or an unknown one', () => {
    for (const motion of [null, 'wobble']) {
      const { host } = row(motion);
      createIconMotion(host, motionOk);
      pointer(host, 'pointerenter');
      expect(host.hasAttribute(PLAY)).toBe(false);
    }
  });

  it('destroy removes listeners and a pending trigger', () => {
    const { host } = row();
    const motion = createIconMotion(host, motionOk);
    pointer(host, 'pointerenter');
    motion.destroy();
    expect(host.hasAttribute(PLAY)).toBe(false);
    pointer(host, 'pointerleave');
    pointer(host, 'pointerenter');
    expect(host.hasAttribute(PLAY)).toBe(false);
  });
});

describe('vIconMotion', () => {
  it('binds on mount and unbinds on unmount', () => {
    vi.stubGlobal('matchMedia', () => ({ matches: false }));
    const Row = defineComponent({
      setup: () => () =>
        withDirectives(h('button', { class: 'row' }, [h('span', { 'data-icon-motion': 'hop' })]), [[vIconMotion]]),
    });
    const root = document.createElement('div');
    document.body.appendChild(root);
    const app = createApp(Row);
    app.mount(root);
    const host = root.querySelector('.row') as HTMLElement;

    pointer(host, 'pointerenter');
    expect(host.hasAttribute(PLAY)).toBe(true);

    app.unmount();
    root.remove();
    vi.unstubAllGlobals();
  });
});
