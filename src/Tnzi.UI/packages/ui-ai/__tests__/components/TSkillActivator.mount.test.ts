/**
 * `TSkillActivator` enforces `required` instead of only drawing the mark: a
 * required parameter left empty, or holding only whitespace, keeps Activate
 * disabled and emits nothing. A value with real text is passed on as typed.
 */
import { afterEach, describe, expect, it, vi } from 'vitest';
import { createApp, h, nextTick, type App } from 'vue';

const { default: TSkillActivator } = await import('../../src/components/skill/TSkillActivator.vue');

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

const skill = { slug: 'weather', name: 'Weather' };
const params = [
  { key: 'city', label: 'City', type: 'string' as const, required: true },
  { key: 'note', label: 'Note', type: 'string' as const },
];

function activateButton(root: HTMLElement): HTMLButtonElement {
  const buttons = root.querySelectorAll('button');
  return buttons[buttons.length - 1]!;
}

async function type(input: HTMLInputElement, value: string): Promise<void> {
  input.value = value;
  input.dispatchEvent(new Event('input'));
  await settle();
}

describe('TSkillActivator required parameters', () => {
  it('holds Activate while a required parameter is empty or only whitespace', async () => {
    const onActivate = vi.fn();
    const root = mount(() => h(TSkillActivator, { skill, params, onActivate }));
    await settle();

    expect(activateButton(root).disabled).toBe(true);
    activateButton(root).click();

    await type(root.querySelector('input')!, ' \t ');
    expect(activateButton(root).disabled).toBe(true);
    activateButton(root).click();
    await settle();

    expect(onActivate).not.toHaveBeenCalled();
  });

  it('activates once the required parameter has text, passing the value untrimmed', async () => {
    const onActivate = vi.fn();
    const root = mount(() => h(TSkillActivator, { skill, params, onActivate }));
    await settle();

    await type(root.querySelector('input')!, ' Paris ');
    expect(activateButton(root).disabled).toBe(false);
    activateButton(root).click();
    await settle();

    expect(onActivate).toHaveBeenCalledExactlyOnceWith('weather', { city: ' Paris ', note: '' });
  });

  it('a required parameter with a default value can be activated as is', async () => {
    const onActivate = vi.fn();
    const root = mount(() =>
      h(TSkillActivator, { skill, params: [{ ...params[0]!, defaultValue: 'Oslo' }], onActivate }),
    );
    await settle();

    activateButton(root).click();
    await settle();

    expect(onActivate).toHaveBeenCalledExactlyOnceWith('weather', { city: 'Oslo' });
  });
});
