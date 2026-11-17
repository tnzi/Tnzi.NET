/**
 * `TPersonalizationSettings` mounted against a fake controller: the three
 * short fields and the free-text box are plain `.t-input` elements that write
 * the controller's draft, and Save / Reset follow `dirty` and `saving`.
 */
import { afterEach, describe, expect, it, vi } from 'vitest';
import { computed, createApp, defineComponent, h, nextTick, ref, type App } from 'vue';
import TPersonalizationSettings from '../../src/components/settings/TPersonalizationSettings.vue';
import { createAiI18n } from '../../src/i18n';
import { en } from '../../src/locales/en';

function fakeController(over: { dirty?: boolean; saving?: boolean; error?: string } = {}) {
  const draft = ref({ displayName: '', role: '', preferredLanguage: '', content: '' });
  return {
    draft,
    dirty: computed(() => over.dirty ?? false),
    saving: ref(over.saving ?? false),
    error: ref(over.error ?? null),
    load: vi.fn(async () => {}),
    save: vi.fn(async () => true),
    reset: vi.fn(),
  };
}

let app: App | null = null;
let host: HTMLElement | null = null;

async function mount(ctrl: ReturnType<typeof fakeController>): Promise<HTMLElement> {
  host = document.createElement('div');
  document.body.appendChild(host);
  const Root = defineComponent({
    setup() {
      createAiI18n(en);
      return () => h(TPersonalizationSettings, { controller: ctrl as never });
    },
  });
  app = createApp(Root);
  app.mount(host);
  await nextTick();
  return host;
}

function button(root: HTMLElement, label: string): HTMLButtonElement {
  const found = Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.trim() === label);
  if (!found) throw new Error(`no button "${label}" in: ${root.textContent}`);
  return found;
}

afterEach(() => {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
});

describe('TPersonalizationSettings', () => {
  it('loads on mount', async () => {
    const ctrl = fakeController();
    await mount(ctrl);
    expect(ctrl.load).toHaveBeenCalled();
  });

  it('each field is a named .t-input that writes its draft key, within its length', async () => {
    const ctrl = fakeController();
    const root = await mount(ctrl);
    const cases = [
      [en.personalizationSettings.name, 'displayName', 64],
      [en.personalizationSettings.role, 'role', 120],
      [en.personalizationSettings.language, 'preferredLanguage', 40],
    ] as const;
    for (const [label, key, max] of cases) {
      const input = root.querySelector<HTMLInputElement>(`input[aria-label="${label}"]`)!;
      expect(input.classList.contains('t-input')).toBe(true);
      expect(input.maxLength).toBe(max);
      input.value = `value of ${key}`;
      input.dispatchEvent(new Event('input'));
      expect(ctrl.draft.value[key]).toBe(`value of ${key}`);
    }
  });

  it('the free-text box is a textarea that writes the content', async () => {
    const ctrl = fakeController();
    const root = await mount(ctrl);
    const box = root.querySelector('textarea')!;
    expect(box.classList.contains('t-input')).toBe(true);
    expect(box.getAttribute('aria-label')).toBe(en.personalizationSettings.content);
    box.value = 'I like short answers.\nUse metric units.';
    box.dispatchEvent(new Event('input'));
    expect(ctrl.draft.value.content).toBe('I like short answers.\nUse metric units.');
  });

  it('Save and Reset are disabled while nothing changed', async () => {
    const root = await mount(fakeController());
    expect(button(root, en.personalizationSettings.save).disabled).toBe(true);
    expect(button(root, en.personalizationSettings.reset).disabled).toBe(true);
  });

  it('with changes, Save (primary) saves and Reset resets', async () => {
    const ctrl = fakeController({ dirty: true });
    const root = await mount(ctrl);
    const save = button(root, en.personalizationSettings.save);
    expect(save.classList.contains('t-button--primary')).toBe(true);
    save.click();
    expect(ctrl.save).toHaveBeenCalled();
    button(root, en.personalizationSettings.reset).click();
    expect(ctrl.reset).toHaveBeenCalled();
  });

  it('while saving, Save is busy and Reset is locked', async () => {
    const root = await mount(fakeController({ dirty: true, saving: true }));
    expect(button(root, en.personalizationSettings.save).getAttribute('aria-busy')).toBe('true');
    expect(button(root, en.personalizationSettings.reset).disabled).toBe(true);
  });

  it('a failed save is announced', async () => {
    const root = await mount(fakeController({ error: 'Could not save.' }));
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('Could not save.');
  });
});
