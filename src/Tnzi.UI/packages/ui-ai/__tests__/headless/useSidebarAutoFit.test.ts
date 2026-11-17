import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { effectScope, nextTick, ref, type EffectScope } from 'vue';
import { useSidebarState } from '../../src/headless/useSidebarState';
import {
  conversationRoom,
  decideSidebarFit,
  useSidebarAutoFit,
  SIDEBAR_AUTO_FIT_MARGIN,
  SIDEBAR_AUTO_FIT_MIN_WIDTH,
} from '../../src/headless/useSidebarAutoFit';

const KEY = 'test-auto-fit';
const MIN = SIDEBAR_AUTO_FIT_MIN_WIDTH;
const ENOUGH = MIN + SIDEBAR_AUTO_FIT_MARGIN;

let scope: EffectScope | null = null;

function setWidth(px: number): void {
  Object.defineProperty(window, 'innerWidth', { configurable: true, value: px });
}

beforeEach(() => {
  localStorage.clear();
  setWidth(1280);
});
afterEach(() => {
  scope?.stop();
  scope = null;
});

/** A real sidebar state with auto-fit on a room the test sets directly. */
function setup(stored?: 'expanded' | 'icon' | 'hidden', initialRoom = 900) {
  if (stored) localStorage.setItem(KEY, stored);
  const room = ref(initialRoom);
  const enabled = ref(true);
  scope = effectScope();
  const result = scope.run(() => {
    const sidebar = useSidebarState({ storageKey: KEY });
    const fit = useSidebarAutoFit(sidebar, { room, enabled });
    return { sidebar, fit };
  })!;
  return { ...result, room, enabled };
}

async function settle(): Promise<void> {
  await nextTick();
  await nextTick();
}

describe('conversationRoom', () => {
  it('is the shell less the expanded sidebar, the side panel and the artifact pane', () => {
    expect(conversationRoom({ containerWidth: 1280, sidebarWidth: 300 })).toBe(980);
    expect(conversationRoom({ containerWidth: 1280, sidebarWidth: 300, asideWidth: 420 })).toBe(560);
    expect(conversationRoom({ containerWidth: 1600, sidebarWidth: 300, asideWidth: 420, artifactWidth: 520 })).toBe(360);
  });

  it('counts the side panel at most at its 70% cap, as it is drawn', () => {
    // Main area 500px: the panel is drawn at most 350px wide whatever its stored width.
    expect(conversationRoom({ containerWidth: 800, sidebarWidth: 300, asideWidth: 900 })).toBe(150);
  });
});

describe('decideSidebarFit', () => {
  it('folds an expanded sidebar below the minimum, unfolds an auto-folded one at minimum + margin', () => {
    expect(decideSidebarFit({ room: MIN - 1, mode: 'expanded', autoCollapsed: false, held: false })).toBe('collapse');
    expect(decideSidebarFit({ room: MIN, mode: 'expanded', autoCollapsed: false, held: false })).toBeNull();
    expect(decideSidebarFit({ room: ENOUGH - 1, mode: 'icon', autoCollapsed: true, held: false })).toBeNull();
    expect(decideSidebarFit({ room: ENOUGH, mode: 'icon', autoCollapsed: true, held: false })).toBe('expand');
  });

  it("never unfolds the user's rail and never touches a hidden sidebar", () => {
    expect(decideSidebarFit({ room: 5000, mode: 'icon', autoCollapsed: false, held: false })).toBeNull();
    expect(decideSidebarFit({ room: 10, mode: 'hidden', autoCollapsed: false, held: false })).toBeNull();
  });

  it('a hold keeps a short sidebar expanded', () => {
    expect(decideSidebarFit({ room: 10, mode: 'expanded', autoCollapsed: false, held: true })).toBeNull();
  });

  it('takes its own minimum and margin', () => {
    expect(decideSidebarFit({ room: 700, mode: 'expanded', autoCollapsed: false, held: false, minWidth: 720 })).toBe('collapse');
    expect(decideSidebarFit({ room: 730, mode: 'icon', autoCollapsed: true, held: false, minWidth: 720, margin: 10 })).toBe('expand');
  });
});

describe('useSidebarAutoFit', () => {
  it('narrow folds it, wide again unfolds it, and neither is written to the preference', async () => {
    const { sidebar, room } = setup('expanded');
    room.value = MIN - 40;
    await settle();
    expect(sidebar.mode.value).toBe('icon');
    expect(sidebar.autoCollapsed.value).toBe(true);
    expect(localStorage.getItem(KEY)).toBe('expanded');

    room.value = ENOUGH + 10;
    await settle();
    expect(sidebar.mode.value).toBe('expanded');
    expect(sidebar.autoCollapsed.value).toBe(false);
    expect(localStorage.getItem(KEY)).toBe('expanded');
  });

  it('a narrow first load starts on the rail without changing the preference', async () => {
    const { sidebar } = setup('expanded', 300);
    expect(sidebar.mode.value).toBe('icon');
    expect(localStorage.getItem(KEY)).toBe('expanded');
  });

  it('a sidebar the user collapsed stays collapsed however wide, across a reload', async () => {
    const first = setup();
    first.sidebar.setMode('icon');
    first.room.value = 3000;
    await settle();
    expect(first.sidebar.mode.value).toBe('icon');
    scope?.stop();

    const second = setup(undefined, 3000);
    expect(localStorage.getItem(KEY)).toBe('icon');
    expect(second.sidebar.mode.value).toBe('icon');
  });

  it('a user expanding while short keeps it expanded until the room has recovered once', async () => {
    const { sidebar, room } = setup('expanded', 300);
    expect(sidebar.mode.value).toBe('icon');
    sidebar.setMode('expanded');
    await settle();
    expect(sidebar.mode.value).toBe('expanded');
    room.value = 250;
    await settle();
    expect(sidebar.mode.value).toBe('expanded');

    // Room recovers, then a new squeeze folds it as usual.
    room.value = ENOUGH;
    await settle();
    room.value = 300;
    await settle();
    expect(sidebar.mode.value).toBe('icon');
  });

  it('settles at the boundary: sweeping across it and jittering on it changes the mode once', async () => {
    const { sidebar, room } = setup('expanded', 900);
    const changes: string[] = [];
    const stop = (await import('vue')).watch(sidebar.mode, (mode) => changes.push(mode), { flush: 'sync' });

    for (let width = 900; width >= MIN - 60; width -= 10) {
      room.value = width;
      await settle();
    }
    for (const width of [MIN - 5, MIN + 5, MIN - 5, MIN + 30, MIN - 2, MIN + 60, MIN + 10]) {
      room.value = width;
      await settle();
    }
    stop();
    expect(changes).toEqual(['icon']);
  });

  it('turning it off unfolds what it folded and folds nothing more', async () => {
    const { sidebar, room, enabled } = setup('expanded', 300);
    expect(sidebar.mode.value).toBe('icon');
    enabled.value = false;
    await settle();
    expect(sidebar.mode.value).toBe('expanded');
    room.value = 200;
    await settle();
    expect(sidebar.mode.value).toBe('expanded');
  });

  it('does nothing below the mobile breakpoint', async () => {
    setWidth(390);
    const { sidebar, room } = setup('expanded', 100);
    expect(sidebar.mode.value).toBe('hidden');
    sidebar.setMode('expanded');
    room.value = 50;
    await settle();
    expect(sidebar.mode.value).toBe('expanded');
    expect(sidebar.autoCollapsed.value).toBe(false);
  });

  it('back from the phone layout to a desktop that is still short, it folds (the restore is not a user choice)', async () => {
    const { sidebar, room } = setup('expanded', 900);
    setWidth(390);
    window.dispatchEvent(new Event('resize'));
    await settle();
    expect(sidebar.mode.value).toBe('hidden');
    // 800px window: the desktop mode comes back, but there is too little room for it.
    setWidth(800);
    room.value = 500;
    window.dispatchEvent(new Event('resize'));
    await settle();
    expect(sidebar.mode.value).toBe('icon');
    expect(sidebar.autoCollapsed.value).toBe(true);
    expect(localStorage.getItem(KEY)).toBe('expanded');
  });

  it('a rail it folded is restored expanded after a trip through the phone layout', async () => {
    const { sidebar, room } = setup('expanded', 300);
    expect(sidebar.mode.value).toBe('icon');
    setWidth(390);
    window.dispatchEvent(new Event('resize'));
    await settle();
    expect(sidebar.mode.value).toBe('hidden');
    setWidth(1280);
    room.value = 900;
    window.dispatchEvent(new Event('resize'));
    await settle();
    expect(sidebar.mode.value).toBe('expanded');
    expect(localStorage.getItem(KEY)).toBe('expanded');
  });
});
