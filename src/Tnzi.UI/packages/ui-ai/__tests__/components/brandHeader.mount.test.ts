/**
 * `TChatApp`'s sidebar brand header.
 *
 * - The logo tile is the collapse control: one native button named by its
 *   action ("Collapse sidebar", "Close sidebar" in the narrow drawer), with the
 *   logo inside it decorative. There is no second, always-visible collapse
 *   button after the name.
 * - On keyboard focus, and on hover where the primary pointer can hover, the
 *   panel icon takes the logo's place in the same box (the swap is CSS, so it
 *   is pinned against the stylesheet: happy-dom matches neither `:hover` nor
 *   `:focus-visible`). The hover half sits inside `@media (hover: hover)`
 *   because touch browsers keep `:hover` on the last tapped spot, and the top
 *   bar's open button is where the toggle lands. The collapsed rail does the
 *   same for its expand button. Neither animates under reduced motion.
 * - `#brand-actions` puts the product's own controls at the right end of the
 *   header, after the name; the rail does not show them.
 * - A `#brand` override receives the toggle as slot props, so replacing the
 *   whole header does not lose collapse.
 */
import { afterEach, describe, expect, it } from 'vitest';
import { createApp, h, nextTick, type App } from 'vue';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';

const { default: TChatApp } = await import('../../src/components/chat/TChatApp.vue');

let app: App | null = null;
let host: HTMLElement | null = null;

async function settle(): Promise<void> {
  for (let i = 0; i < 4; i += 1) {
    await Promise.resolve();
    await nextTick();
  }
}

async function mountShell(props: Record<string, unknown> = {}, slots: Record<string, (p: any) => unknown> = {}): Promise<HTMLElement> {
  host = document.createElement('div');
  document.body.appendChild(host);
  app = createApp({
    render: () => h(TChatApp, { sidebarStorageKey: null, autoApplyTheme: false, brandName: 'Acme', ...props }, slots),
  });
  app.mount(host);
  await settle();
  return host;
}

afterEach(() => {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
  document.body.innerHTML = '';
});

const toggle = (el: HTMLElement) => el.querySelector('.t-chat-app__brand-toggle') as HTMLButtonElement | null;

describe('brand header toggle', () => {
  it('the logo tile is the one collapse button, named by its action', async () => {
    const el = await mountShell();
    const button = toggle(el);

    expect(button).not.toBeNull();
    expect(button!.tagName).toBe('BUTTON');
    expect(button!.type).toBe('button');
    expect(button!.getAttribute('aria-label')).toBe('Collapse sidebar');
    expect(button!.getAttribute('title')).toBe('Collapse sidebar');
    // The logo is inside the button and decorative.
    const mark = button!.querySelector('.t-chat-app__brand-mark');
    expect(mark).not.toBeNull();
    expect(mark!.getAttribute('aria-hidden')).toBe('true');
    // No separate collapse button any more: the header has exactly one button.
    expect(el.querySelector('.t-chat-app__brand-collapse')).toBeNull();
    expect(el.querySelectorAll('.t-chat-app__brand button')).toHaveLength(1);
  });

  it('takes keyboard focus and collapses the sidebar into the rail', async () => {
    const el = await mountShell();
    const button = toggle(el)!;

    button.focus();
    expect(document.activeElement).toBe(button);
    button.click();
    await settle();

    expect(el.querySelector('.t-collapsible-sidebar--icon')).not.toBeNull();
    const rail = el.querySelector('.t-chat-rail__brand') as HTMLButtonElement;
    expect(rail.getAttribute('aria-label')).toBe('Expand sidebar');
    expect(rail.querySelector('.t-chat-rail__brand-logo')?.getAttribute('aria-hidden')).toBe('true');

    rail.click();
    await settle();
    expect(el.querySelector('.t-collapsible-sidebar--expanded')).not.toBeNull();
    expect(toggle(el)).not.toBeNull();
  });

  it('in the narrow drawer it is named and acts as close', async () => {
    const el = await mountShell({ isMobile: true, initialSidebarMode: 'expanded' });
    const button = toggle(el)!;
    expect(button.getAttribute('aria-label')).toBe('Close sidebar');

    button.click();
    await settle();
    expect(el.querySelector('.t-collapsible-sidebar--hidden')).not.toBeNull();
  });
});

describe('#brand-actions', () => {
  it('renders at the right end of the header, after the name', async () => {
    let narrow: unknown;
    const el = await mountShell({}, {
      'brand-actions': (props) => {
        narrow = props.narrow;
        return h('div', { class: 'own-mode', role: 'radiogroup', 'aria-label': 'Mode' });
      },
    });
    const brand = el.querySelector('.t-chat-app__brand')!;
    const actions = brand.querySelector('.t-chat-app__brand-actions');

    expect(actions?.querySelector('.own-mode')).not.toBeNull();
    expect(brand.lastElementChild).toBe(actions);
    expect(actions!.previousElementSibling?.classList.contains('t-chat-app__brand-name')).toBe(true);
    expect(narrow).toBe(false);
  });

  it('leaves no wrapper behind without the slot', async () => {
    const el = await mountShell();
    expect(el.querySelector('.t-chat-app__brand-actions')).toBeNull();
  });

  it('is not shown in the collapsed rail', async () => {
    const el = await mountShell({ initialSidebarMode: 'icon' }, {
      'brand-actions': () => h('div', { class: 'own-mode' }),
    });
    expect(el.querySelector('.t-collapsible-sidebar--icon')).not.toBeNull();
    expect(el.querySelector('.own-mode')).toBeNull();
  });
});

describe('#brand override', () => {
  it('receives the toggle, its label and the narrow flag', async () => {
    let received: Record<string, unknown> = {};
    const el = await mountShell({}, {
      brand: (props) => {
        received = props;
        return h('button', { class: 'own-toggle', onClick: props.toggleSidebar }, String(props.toggleLabel));
      },
    });

    expect(received.toggleLabel).toBe('Collapse sidebar');
    expect(received.narrow).toBe(false);
    expect(el.querySelector('.t-chat-app__brand')).toBeNull();

    (el.querySelector('.own-toggle') as HTMLButtonElement).click();
    await settle();
    expect(el.querySelector('.t-collapsible-sidebar--icon')).not.toBeNull();
  });
});

describe('hover and focus swap (stylesheet)', () => {
  const pkgRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
  const css = (rel: string) =>
    (/<style[^>]*>([\s\S]*?)<\/style>/.exec(readFileSync(resolve(pkgRoot, rel), 'utf8'))?.[1] ?? '').replace(/\/\*[\s\S]*?\*\//g, '');
  const APP = css('src/components/chat/TChatApp.vue');
  const RAIL = css('src/components/layout/TChatRail.vue');

  function rule(source: string, selector: string): string {
    const escaped = selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&').replace(/,\s*/g, ',\\s*');
    const match = new RegExp(`(?:^|\\})\\s*${escaped}\\s*\\{([^}]*)\\}`).exec(source);
    if (!match) throw new Error(`no rule "${selector}"`);
    return match[1]!;
  }

  /** The body of the `@media (hover: hover)` block, and the sheet without it. */
  function splitHover(source: string): { inside: string; outside: string } {
    const match = /@media\s*\(hover:\s*hover\)\s*\{((?:[^{}]*\{[^}]*\})*)\s*\}/.exec(source);
    if (!match) throw new Error('no @media (hover: hover) block');
    return { inside: match[1]!, outside: source.replace(match[0], '') };
  }

  it('focus-visible hides the logo and shows the panel icon in the header, on any pointer', () => {
    const { outside } = splitHover(APP);
    expect(rule(outside, '.t-chat-app__brand-toggle:focus-visible .t-chat-app__brand-mark')).toMatch(/opacity:\s*0/);
    expect(rule(outside, '.t-chat-app__brand-toggle:focus-visible .t-chat-app__brand-toggle-icon')).toMatch(/opacity:\s*1/);
  });

  it('hover swaps only where the primary pointer can hover (no sticky tap hover on touch)', () => {
    for (const [source, prefix] of [[APP, '.t-chat-app__brand-toggle'], [RAIL, '.t-chat-rail__brand']] as const) {
      const { inside, outside } = splitHover(source);
      expect(outside).not.toContain(`${prefix}:hover`);
      expect(inside).toContain(`${prefix}:hover`);
    }
    const app = splitHover(APP).inside;
    expect(rule(app, '.t-chat-app__brand-toggle:hover .t-chat-app__brand-mark')).toMatch(/opacity:\s*0/);
    expect(rule(app, '.t-chat-app__brand-toggle:hover .t-chat-app__brand-toggle-icon')).toMatch(/opacity:\s*1/);
    const rail = splitHover(RAIL).inside;
    expect(rule(rail, '.t-chat-rail__brand:hover .t-chat-rail__brand-logo')).toMatch(/opacity:\s*0/);
    expect(rule(rail, '.t-chat-rail__brand:hover .t-chat-rail__brand-expand')).toMatch(/opacity:\s*1/);
  });

  it('the panel icon overlays the logo, so the swap shifts nothing', () => {
    const icon = rule(APP, '.t-chat-app__brand-toggle-icon');
    expect(icon).toMatch(/position:\s*absolute/);
    expect(icon).toMatch(/inset:\s*0/);
    expect(icon).toMatch(/opacity:\s*0/);
    expect(rule(APP, '.t-chat-app__brand-toggle')).toMatch(/position:\s*relative/);
  });

  it('the rail swaps on focus-visible on any pointer', () => {
    const { outside } = splitHover(RAIL);
    expect(rule(outside, '.t-chat-rail__brand:focus-visible .t-chat-rail__brand-logo')).toMatch(/opacity:\s*0/);
    expect(rule(outside, '.t-chat-rail__brand:focus-visible .t-chat-rail__brand-expand')).toMatch(/opacity:\s*1/);
  });

  it('neither animates under reduced motion', () => {
    for (const source of [APP, RAIL]) {
      const block = /@media\s*\(prefers-reduced-motion:\s*reduce\)\s*\{([\s\S]*?\})\s*\}/.exec(source)?.[1] ?? '';
      expect(block).toMatch(/transition:\s*none/);
    }
  });

  it('the header actions sit flush right', () => {
    expect(rule(APP, '.t-chat-app__brand-actions')).toMatch(/margin-left:\s*auto/);
  });
});
