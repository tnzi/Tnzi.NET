/**
 * `TModelSelector` loads provider logos only through a consumer-supplied
 * resolver. It used to load them from a third-party host for every user who
 * opened the picker, which told that host each user's address and every
 * provider name the deployment configures, and broke under a strict CSP.
 */
import { afterEach, describe, expect, it } from 'vitest';
import { createApp, h, nextTick, ref, type App } from 'vue';

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

describe('TModelSelector dialog', () => {
  const models = [
    { id: 'gpt', name: 'GPT Large', provider: 'openai' },
    { id: 'sonnet', name: 'Sonnet', provider: 'anthropic' },
  ];

  function controlled(initial = false) {
    const state = ref({ open: initial, value: 'gpt' as string, opens: [] as boolean[] });
    mount(() =>
      h(TModelSelector, {
        models,
        open: state.value.open,
        modelValue: state.value.value,
        'onUpdate:open': (v: boolean) => {
          state.value.opens.push(v);
          state.value.open = v;
        },
        'onUpdate:modelValue': (v: string) => {
          state.value.value = v;
        },
      }),
    );
    return state;
  }

  it('opens from its trigger as a dialog and closes on Escape', async () => {
    const state = controlled();
    await settle();
    expect(document.querySelector('[role="dialog"]')).toBeNull();
    (document.querySelector('.t-model-selector__trigger button') as HTMLButtonElement).click();
    await settle();
    expect(state.value.opens).toEqual([true]);
    const dialog = document.querySelector('[role="dialog"]');
    expect(dialog?.textContent).toContain('Select model');
    dialog!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await settle();
    expect(state.value.opens).toEqual([true, false]);
  });

  it('marks the current model and picks another, closing the dialog', async () => {
    const state = controlled(true);
    await settle();
    const current = document.querySelector('[data-model-id="gpt"]');
    expect(current?.getAttribute('aria-current')).toBe('true');
    (document.querySelector('[data-model-id="sonnet"]') as HTMLButtonElement).click();
    await settle();
    expect(state.value.value).toBe('sonnet');
    expect(state.value.opens).toEqual([false]);
  });

  it('filters by name, provider or id and says when nothing matches', async () => {
    controlled(true);
    await settle();
    const input = document.querySelector('input.t-input') as HTMLInputElement;
    input.value = 'anthropic';
    input.dispatchEvent(new Event('input'));
    await settle();
    expect(document.querySelector('[data-model-id="gpt"]')).toBeNull();
    expect(document.querySelector('[data-model-id="sonnet"]')).not.toBeNull();
    input.value = 'nothing-like-this';
    input.dispatchEvent(new Event('input'));
    await settle();
    expect(document.querySelectorAll('[data-model-id]')).toHaveLength(0);
    expect(document.body.textContent).toContain('No models found');
  });
});
