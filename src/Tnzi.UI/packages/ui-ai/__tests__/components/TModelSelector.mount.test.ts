/**
 * `TModelSelector` loads provider logos only through a consumer-supplied
 * resolver. It used to load them from a third-party host for every user who
 * opened the picker, which told that host each user's address and every
 * provider name the deployment configures, and broke under a strict CSP.
 */
import { afterEach, describe, expect, it } from 'vitest';
import { createApp, h, nextTick, type App } from 'vue';

const { default: TModelSelector } = await import('../../src/components/agent/TModelSelector.vue');

let app: App | null = null;
let host: HTMLElement | null = null;

function mount(render: () => unknown): HTMLElement {
  host = document.createElement('div');
  document.body.appendChild(host);
  app = createApp({ render });
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

describe('TModelSelector', () => {
  const models = [{ id: 'm1', name: 'Model One', provider: 'internal-llm' }];

  it('loads no images unless the consumer supplies a logo resolver', async () => {
    mount(() => h(TModelSelector, { models, open: true }));
    await settle();
    expect(document.body.textContent).toContain('Model One');
    expect(document.querySelectorAll('img')).toHaveLength(0);
  });

  it('shows the logo the resolver returns', async () => {
    mount(() =>
      h(TModelSelector, {
        models,
        open: true,
        providerLogo: (provider: string) => `/logos/${encodeURIComponent(provider)}.svg`,
      }),
    );
    await settle();
    const img = document.querySelector('img');
    expect(img?.getAttribute('src')).toBe('/logos/internal-llm.svg');
  });
});
