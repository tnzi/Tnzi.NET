/**
 * `TContextMeter`, mounted: a ring and the share of the window, the tokens alone without a
 * window, the warning and nearly-full levels, and a keyboard-reachable element whose name is the
 * tooltip's text, in the page's language.
 */
import { afterEach, describe, expect, it } from 'vitest';
import { createApp, defineComponent, h, nextTick, type App } from 'vue';
import TContextMeter from '../../src/components/context/TContextMeter.vue';
import { createAiI18n } from '../../src/i18n';
import { zhCn } from '../../src/locales/zh-cn';

let app: App | null = null;
let host: HTMLElement | null = null;

afterEach(() => {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
});

async function mount(props: Record<string, unknown>, zh = false): Promise<HTMLElement> {
  host = document.createElement('div');
  document.body.appendChild(host);
  const Root = defineComponent({
    setup() {
      if (zh) createAiI18n(zhCn);
      return () => h(TContextMeter, props);
    },
  });
  app = createApp(Root);
  app.mount(host);
  await nextTick();
  return host.querySelector('[data-context-meter]') as HTMLElement;
}

describe('TContextMeter', () => {
  it('shows the share of the window with a ring, and names the tokens against the window', async () => {
    const meter = await mount({ tokens: 24_000, window: 200_000 });
    expect(meter.textContent!.trim()).toBe('12%');
    expect(meter.querySelector('svg')).not.toBeNull();
    expect(meter.getAttribute('role')).toBe('img');
    expect(meter.getAttribute('tabindex')).toBe('0');
    expect(meter.getAttribute('aria-label')).toBe('Context 24K / 200K');
    expect(meter.dataset.level).toBe('ok');
    const fill = meter.querySelectorAll('circle')[1]!;
    const circumference = Number(fill.getAttribute('stroke-dasharray'));
    expect(Number(fill.getAttribute('stroke-dashoffset'))).toBeCloseTo(circumference * 0.88);
  });

  it('without a window shows the tokens and a gauge', async () => {
    const meter = await mount({ tokens: 45_230 });
    expect(meter.textContent!.trim()).toBe('45.2K');
    expect(meter.querySelector('svg circle')).toBeNull();
    expect(meter.getAttribute('aria-label')).toBe('Context 45.2K');
  });

  it('warns past 80% and is nearly full past 95%', async () => {
    expect((await mount({ tokens: 170_000, window: 200_000 })).className).toContain('is-warn');
    app?.unmount();
    const full = await mount({ tokens: 196_000, window: 200_000 });
    expect(full.className).toContain('is-full');
    expect(full.dataset.level).toBe('full');
  });

  it('speaks the page language', async () => {
    const meter = await mount({ tokens: 1000, window: 100_000 }, true);
    expect(meter.getAttribute('aria-label')).toBe('上下文 1K / 100K');
  });
});
