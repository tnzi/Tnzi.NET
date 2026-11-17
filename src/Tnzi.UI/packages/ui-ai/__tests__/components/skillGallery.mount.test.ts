/**
 * The skill gallery and its cards, drawn with the package's own controls:
 * the search field filters by name and description, a category chip narrows
 * further (pressing it again clears it), and a card's button activates or
 * deactivates without also counting as a click on the card.
 */
import { afterEach, describe, expect, it, vi } from 'vitest';
import { createApp, h, nextTick, type App } from 'vue';

const { default: TSkillGallery } = await import('../../src/components/skill/TSkillGallery.vue');
const { default: TSkillCard } = await import('../../src/components/skill/TSkillCard.vue');

let app: App | null = null;
let host: HTMLElement | null = null;

function mount(render: () => unknown): HTMLElement {
  host = document.createElement('div');
  document.body.appendChild(host);
  app = createApp({ render });
  app.mount(host);
  return host;
}

afterEach(() => {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
  document.body.innerHTML = '';
});

const skills = [
  { id: '1', slug: 'weather', name: 'Weather', description: 'Forecasts by city', category: 'Tools' },
  { id: '2', slug: 'mail', name: 'Mail', description: 'Reads the inbox', category: 'Office', isActive: true },
  { id: '3', slug: 'calendar', name: 'Calendar', description: 'Finds a free slot', category: 'Office', isBuiltIn: true },
];

const names = (root: HTMLElement) =>
  [...root.querySelectorAll('.t-skill-card__name')].map((el) => el.textContent?.trim());

async function type(input: HTMLInputElement, value: string): Promise<void> {
  input.value = value;
  input.dispatchEvent(new Event('input'));
  await nextTick();
}

describe('TSkillGallery', () => {
  it('filters by name and description from a native .t-input', async () => {
    const root = mount(() => h(TSkillGallery, { skills }));
    const input = root.querySelector<HTMLInputElement>('input.t-input')!;
    expect(input.getAttribute('aria-label')).toBe('Search skills...');
    expect(names(root)).toEqual(['Weather', 'Mail', 'Calendar']);

    await type(input, 'inbox');
    expect(names(root)).toEqual(['Mail']);

    await type(input, 'nothing like it');
    expect(names(root)).toEqual([]);
    expect(root.textContent).toContain('No skills found');
  });

  it('narrows by a category chip, which a second press clears', async () => {
    const root = mount(() => h(TSkillGallery, { skills, categories: ['Tools', 'Office'] }));
    const chips = [...root.querySelectorAll<HTMLButtonElement>('.t-skill-gallery__cat')];
    expect(chips.map((c) => c.textContent?.trim())).toEqual(['All', 'Tools', 'Office']);
    expect(chips[0]!.getAttribute('aria-pressed')).toBe('true');

    chips[2]!.click();
    await nextTick();
    expect(names(root)).toEqual(['Mail', 'Calendar']);
    expect(chips[2]!.getAttribute('aria-pressed')).toBe('true');
    expect(chips[0]!.getAttribute('aria-pressed')).toBe('false');

    chips[2]!.click();
    await nextTick();
    expect(names(root)).toEqual(['Weather', 'Mail', 'Calendar']);
  });

  it('scrolls its cards in a thin-scrollbar container, not a naive scrollbar', () => {
    const root = mount(() => h(TSkillGallery, { skills }));
    expect(root.querySelector('.t-skill-gallery__list.t-ai-scroll')).not.toBeNull();
    expect(root.querySelector('[class^="n-"], [class*=" n-"]')).toBeNull();
  });

  it('passes activate, deactivate and skill-click on from its cards', async () => {
    const onActivate = vi.fn();
    const onDeactivate = vi.fn();
    const onSkillClick = vi.fn();
    const root = mount(() => h(TSkillGallery, { skills, onActivate, onDeactivate, onSkillClick }));
    const cards = root.querySelectorAll<HTMLElement>('.t-skill-card');

    cards[0]!.querySelector('button')!.click();
    cards[1]!.querySelector('button')!.click();
    cards[2]!.click();
    await nextTick();

    expect(onActivate).toHaveBeenCalledExactlyOnceWith('weather');
    expect(onDeactivate).toHaveBeenCalledExactlyOnceWith('mail');
    expect(onSkillClick).toHaveBeenCalledExactlyOnceWith(skills[2]);
  });
});

describe('TSkillCard', () => {
  it('shows Activate for an inactive skill and Deactivate for an active one, without a card click', async () => {
    const onActivate = vi.fn();
    const onDeactivate = vi.fn();
    const onClick = vi.fn();
    const root = mount(() => [
      h(TSkillCard, { skill: skills[0]!, onActivate, onDeactivate, onClick }),
      h(TSkillCard, { skill: skills[1]!, onActivate, onDeactivate, onClick }),
    ]);
    const [inactive, active] = [...root.querySelectorAll<HTMLButtonElement>('button.t-button')];
    expect(inactive!.textContent?.trim()).toBe('Activate');
    expect(active!.textContent?.trim()).toBe('Deactivate');

    inactive!.click();
    active!.click();
    await nextTick();

    expect(onActivate).toHaveBeenCalledExactlyOnceWith('weather');
    expect(onDeactivate).toHaveBeenCalledExactlyOnceWith('mail');
    expect(onClick).not.toHaveBeenCalled();
  });

  it('opens from the keyboard: Enter or Space on the card itself', async () => {
    const onClick = vi.fn();
    const root = mount(() => h(TSkillCard, { skill: skills[2]!, onClick }));
    const card = root.querySelector<HTMLElement>('.t-skill-card')!;
    expect(card.tabIndex).toBe(0);
    expect(card.textContent).toContain('Built-in');

    card.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    card.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', bubbles: true }));
    await nextTick();

    expect(onClick).toHaveBeenCalledTimes(2);
    expect(onClick).toHaveBeenCalledWith(skills[2]);
  });
});
