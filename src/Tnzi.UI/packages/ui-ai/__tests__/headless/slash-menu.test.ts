import { describe, expect, it, vi } from 'vitest';
import { nextTick, ref } from 'vue';
import { filterSlashItems, slashQuery, textWithCommand, useSlashMenu, type SlashMenuItem } from '../../src/headless/slash-menu';

const ITEMS: SlashMenuItem[] = [
  { command: 'compact', name: 'compact', description: 'Summarise the conversation so far' },
  { command: 'team-shop:release', name: 'release', description: 'Cut a release' },
  { command: 'review', name: 'review', description: 'Review the changes' },
  { command: 'init', name: 'init', description: 'Write a CLAUDE.md for the repository' },
];

describe('slashQuery', () => {
  it('is the name typed after a slash that starts an otherwise empty box', () => {
    expect(slashQuery('/')).toBe('');
    expect(slashQuery('/rev')).toBe('rev');
    expect(slashQuery('/review ')).toBeNull();
    expect(slashQuery('say /review')).toBeNull();
    expect(slashQuery('')).toBeNull();
  });
});

describe('filterSlashItems', () => {
  it('ranks a starting name first, then a word of the command, then anywhere, then the description', () => {
    expect(filterSlashItems(ITEMS, '').map((i) => i.command)).toEqual(ITEMS.map((i) => i.command));
    // 'init' matches by its description ("for the repository"), last
    expect(filterSlashItems(ITEMS, 're').map((i) => i.name)).toEqual(['release', 'review', 'init']);
    expect(filterSlashItems(ITEMS, 'shop').map((i) => i.name)).toEqual(['release']);
    expect(filterSlashItems(ITEMS, 'view').map((i) => i.name)).toEqual(['review']);
    expect(filterSlashItems(ITEMS, 'claude').map((i) => i.name)).toEqual(['init']);
    expect(filterSlashItems(ITEMS, 'zzz')).toEqual([]);
  });

  it('writes the command as the CLI takes it, with a space for its arguments', () => {
    expect(textWithCommand(ITEMS[1]!)).toBe('/team-shop:release ');
  });
});

describe('useSlashMenu', () => {
  function setup(initial = '') {
    const text = ref(initial);
    const picked = vi.fn((item: SlashMenuItem) => {
      text.value = textWithCommand(item);
    });
    const menu = useSlashMenu({ text, items: ref(ITEMS), onPick: picked });
    return { text, picked, menu };
  }

  it('opens while a command is typed, and moves around the list with the arrows', async () => {
    const { text, menu } = setup();
    expect(menu.open.value).toBe(false);
    text.value = '/';
    await nextTick();
    expect(menu.open.value).toBe(true);
    expect(menu.handleKey({ key: 'ArrowUp' })).toBe(true);
    expect(menu.highlighted.value).toBe(3);
    expect(menu.handleKey({ key: 'ArrowDown' })).toBe(true);
    expect(menu.highlighted.value).toBe(0);
    expect(menu.handleKey({ key: 'a' })).toBe(false);
  });

  it('a new query starts at the top', async () => {
    const { text, menu } = setup('/');
    menu.highlighted.value = 2;
    text.value = '/re';
    await nextTick();
    expect(menu.highlighted.value).toBe(0);
  });

  it('Enter and Tab pick the highlighted command; Shift+Enter, IME Enter and Enter with no match are the composer\'s', async () => {
    const { text, menu, picked } = setup('/re');
    await nextTick();
    expect(menu.handleKey({ key: 'Enter', isComposing: true })).toBe(false);
    expect(menu.handleKey({ key: 'Enter', keyCode: 229 })).toBe(false);
    expect(menu.handleKey({ key: 'Enter', shiftKey: true })).toBe(false);
    menu.handleKey({ key: 'ArrowDown' });
    expect(menu.handleKey({ key: 'Tab' })).toBe(true);
    expect(picked).toHaveBeenCalledWith(ITEMS[2]);
    expect(text.value).toBe('/review ');
    await nextTick();
    expect(menu.open.value).toBe(false);

    text.value = '/zzz';
    await nextTick();
    expect(menu.handleKey({ key: 'Enter' })).toBe(false);
    expect(menu.handleKey({ key: 'ArrowDown' })).toBe(false);
  });

  it('Escape closes it until the text changes', async () => {
    const { text, menu } = setup('/c');
    await nextTick();
    expect(menu.handleKey({ key: 'Escape' })).toBe(true);
    expect(menu.open.value).toBe(false);
    expect(menu.handleKey({ key: 'ArrowDown' })).toBe(false);
    text.value = '/co';
    await nextTick();
    expect(menu.open.value).toBe(true);
  });

  it('pick with nothing at the index does nothing', () => {
    const { menu, picked } = setup('/');
    expect(menu.pick(9)).toBe(false);
    expect(picked).not.toHaveBeenCalled();
  });
});
