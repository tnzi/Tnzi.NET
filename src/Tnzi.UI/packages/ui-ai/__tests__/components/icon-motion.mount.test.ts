/**
 * Nav icon motions, mounted: a nav item's `iconMotion` reaches the rendered
 * icon in the expanded sidebar and in the rail, the row is the motion host,
 * and the shell's own entries (new chat, built-in views) carry their motions.
 *
 * The keyframes themselves are CSS; happy-dom does not run them. Timing and
 * the touch / reduced-motion rules are pinned in `headless/icon-motion.test.ts`.
 */
import { afterEach, describe, expect, it } from 'vitest';
import { createApp, defineComponent, h, nextTick, type App } from 'vue';

const { default: TSidebarNav } = await import('../../src/components/layout/TSidebarNav.vue');
const { default: TChatRail } = await import('../../src/components/layout/TChatRail.vue');
const { default: TChatApp } = await import('../../src/components/chat/TChatApp.vue');

let app: App | null = null;
let host: HTMLElement | null = null;

function mount(render: () => ReturnType<typeof h>): HTMLElement {
  host = document.createElement('div');
  document.body.appendChild(host);
  app = createApp(defineComponent({ setup: () => render }));
  app.mount(host);
  return host;
}

async function settle(): Promise<void> {
  for (let i = 0; i < 4; i += 1) {
    await Promise.resolve();
    await nextTick();
  }
}

afterEach(() => {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
  document.body.innerHTML = '';
});

const groups = [
  {
    id: 'main',
    items: [
      { id: 'scheduled', label: 'Scheduled', icon: 'lucide:clock', iconMotion: 'ring' as const },
      { id: 'still', label: 'Still', icon: 'lucide:folder' },
    ],
  },
];

describe('nav icon motion', () => {
  it('the expanded nav puts the motion on the icon and makes the row its host', () => {
    const el = mount(() => h(TSidebarNav, { groups }));
    const [ringing, still] = Array.from(el.querySelectorAll('.t-sidebar-nav__item'));
    expect(ringing.hasAttribute('data-icon-motion-host')).toBe(true);
    expect(ringing.querySelector('svg')?.getAttribute('data-icon-motion')).toBe('ring');
    // Unset means a still icon: no attribute for the stylesheet to match.
    expect(still.querySelector('svg')?.hasAttribute('data-icon-motion')).toBe(false);
  });

  it('the rail does the same', () => {
    const el = mount(() => h(TChatRail, { groups }));
    const button = el.querySelector('.t-chat-rail__btn[aria-label="Scheduled"]')!;
    expect(button.hasAttribute('data-icon-motion-host')).toBe(true);
    expect(button.querySelector('svg')?.getAttribute('data-icon-motion')).toBe('ring');
  });

  it("the shell's own entries carry their motions, and new chat's can be turned off", async () => {
    const motionOf = (el: HTMLElement, label: string) =>
      Array.from(el.querySelectorAll('.t-sidebar-nav__item'))
        .find((row) => row.textContent?.trim() === label)
        ?.querySelector('svg')
        ?.getAttribute('data-icon-motion') ?? null;

    let el = mount(() =>
      h(TChatApp, { sidebarStorageKey: null, autoApplyTheme: false, builtinViews: ['scheduled', 'agents'] }),
    );
    await settle();
    expect(motionOf(el, 'New chat')).toBe('tilt');
    expect(motionOf(el, 'Scheduled')).toBe('ring');
    expect(motionOf(el, 'Agents')).toBe('hop');

    app?.unmount();
    host?.remove();
    el = mount(() => h(TChatApp, { sidebarStorageKey: null, autoApplyTheme: false, newChatIconMotion: null }));
    await settle();
    expect(motionOf(el, 'New chat')).toBeNull();
  });
});
