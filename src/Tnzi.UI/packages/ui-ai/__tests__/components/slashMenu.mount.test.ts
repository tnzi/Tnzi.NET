/**
 * `TChatApp`'s `/` menu (`slashCommands`), mounted for real on the landing page and in a
 * conversation: it opens above the composer when a slash starts the box, filters as the name is
 * typed, the composer's box is its combobox (`aria-expanded`, `aria-controls`,
 * `aria-activedescendant` naming the highlighted option), the arrows and Enter go to the menu
 * instead of the composer (Enter picks, it does not send), a click picks without blurring the box,
 * Escape closes it, and it says why the list is empty. Without `slashCommands` there is no menu.
 */
import { afterEach, describe, expect, it, vi } from 'vitest';
import { createApp, defineComponent, h, nextTick, ref, type App } from 'vue';
import type { ChatMessage } from '../../src/headless/useChat';
import type { SlashMenuItem } from '../../src/headless/slash-menu';

const { default: TChatApp } = await import('../../src/components/chat/TChatApp.vue');

let app: App | null = null;
let host: HTMLElement | null = null;

afterEach(() => {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
  document.body.innerHTML = '';
});

async function settle(): Promise<void> {
  for (let i = 0; i < 6; i += 1) {
    await Promise.resolve();
    await nextTick();
  }
}

const COMMANDS: SlashMenuItem[] = [
  { command: 'compact', name: 'compact', description: 'Summarise the conversation', sourceLabel: 'Built in' },
  { command: 'team:review', name: 'review', description: 'Review the changes', argumentHint: '<branch>', icon: 'lucide:users' },
];

const messages: ChatMessage[] = [
  { id: 'u1', role: 'user', content: 'hi', createdAt: '' },
  { id: 'a1', role: 'assistant', content: 'hello', createdAt: '' },
];

function mount(extra: Record<string, unknown> = {}) {
  const text = ref('')
  const onSend = vi.fn()
  const onQuery = vi.fn()
  host = document.createElement('div')
  document.body.appendChild(host)
  const Root = defineComponent({
    setup: () => () =>
      h(TChatApp, {
        sidebarStorageKey: null,
        autoApplyTheme: false,
        slashCommands: COMMANDS,
        inputText: text.value,
        'onUpdate:inputText': (value: string) => (text.value = value),
        onSend,
        onSlashQuery: onQuery,
        ...extra,
      }),
  })
  app = createApp(Root)
  app.mount(host)
  return { el: host, text, onSend, onQuery }
}

const box = (el: HTMLElement) => el.querySelector('textarea')!

async function type(el: HTMLElement, value: string) {
  const input = box(el)
  input.value = value
  input.dispatchEvent(new Event('input'))
  await settle()
}

function key(el: HTMLElement, name: string, init: KeyboardEventInit = {}) {
  const event = new KeyboardEvent('keydown', { key: name, bubbles: true, cancelable: true, ...init })
  box(el).dispatchEvent(event)
  return event
}

describe('TChatApp slash menu', () => {
  it('opens on a slash, filters, and the box is its combobox', async () => {
    const { el, onQuery } = mount()
    await settle()
    expect(box(el).getAttribute('role')).toBe('combobox')
    expect(box(el).getAttribute('aria-expanded')).toBe('false')
    expect(el.querySelector('[data-slash-menu]')).toBeNull()

    await type(el, '/')
    const menu = el.querySelector('[data-slash-menu]')!
    expect(menu.closest('.t-thread-composer__suggestions, .t-landing')).not.toBeNull()
    const listbox = menu.querySelector('[role="listbox"]')!
    expect(listbox.getAttribute('aria-label')).toBe('Skills and commands')
    expect([...listbox.querySelectorAll('[role="option"]')].map((o) => o.getAttribute('data-command'))).toEqual(['compact', 'team:review'])
    expect(box(el).getAttribute('aria-expanded')).toBe('true')
    expect(box(el).getAttribute('aria-controls')).toBe(listbox.id)
    expect(box(el).getAttribute('aria-activedescendant')).toBe(`${listbox.id}-0`)
    expect(onQuery).toHaveBeenLastCalledWith('')

    await type(el, '/rev')
    const options = el.querySelectorAll('[role="option"]')
    expect(options).toHaveLength(1)
    expect(options[0]!.textContent).toContain('/review')
    expect(options[0]!.textContent).toContain('<branch>')
    expect(options[0]!.textContent).toContain('/team:review')
    expect(onQuery).toHaveBeenLastCalledWith('rev')
  })

  it('the arrows move, and Enter picks instead of sending', async () => {
    const { el, text, onSend } = mount()
    await type(el, '/')
    expect(key(el, 'ArrowDown').defaultPrevented).toBe(true)
    await settle()
    const listbox = el.querySelector('[role="listbox"]')!
    expect(box(el).getAttribute('aria-activedescendant')).toBe(`${listbox.id}-1`)
    expect(el.querySelectorAll('[role="option"]')[1]!.getAttribute('aria-selected')).toBe('true')

    expect(key(el, 'Enter').defaultPrevented).toBe(true)
    await settle()
    expect(text.value).toBe('/team:review ')
    expect(onSend).not.toHaveBeenCalled()
    expect(el.querySelector('[data-slash-menu]')).toBeNull()
    expect(box(el).getAttribute('aria-expanded')).toBe('false')
  })

  it('a click picks, and the mouse down does not take focus from the box', async () => {
    const { el, text } = mount()
    await type(el, '/')
    const option = el.querySelectorAll<HTMLElement>('[role="option"]')[0]!
    const down = new MouseEvent('mousedown', { bubbles: true, cancelable: true })
    option.dispatchEvent(down)
    expect(down.defaultPrevented).toBe(true)
    option.click()
    await settle()
    expect(text.value).toBe('/compact ')
  })

  it('Escape closes it until the text changes; Enter then sends as usual', async () => {
    const { el, onSend } = mount()
    await type(el, '/co')
    expect(key(el, 'Escape').defaultPrevented).toBe(true)
    await settle()
    expect(el.querySelector('[data-slash-menu]')).toBeNull()
    await type(el, '/com')
    expect(el.querySelector('[data-slash-menu]')).not.toBeNull()

    await type(el, '/zzz')
    expect(el.querySelector('[role="status"]')!.textContent).toBe('No skill or command matches')
    expect(key(el, 'Enter').defaultPrevented).toBe(true)
    await settle()
    expect(onSend).toHaveBeenCalledWith('/zzz', [])
  })

  it('says it is reading, or that it could not, while the list is empty, with the app note', async () => {
    const { el } = mount({ slashCommands: [], slashCommandsState: 'loading', slashCommandsNote: 'Only the built-in commands are listed.' })
    await type(el, '/')
    expect(el.querySelector('[data-slash-menu] [role="status"]')!.textContent).toBe('Reading…')
    expect(el.querySelector('.t-slash-menu__note')!.textContent).toBe('Only the built-in commands are listed.')
    app?.unmount()

    const failed = mount({ slashCommands: [], slashCommandsState: 'failed' })
    await type(failed.el, '/')
    expect(failed.el.querySelector('[data-slash-menu] [role="status"]')!.textContent).toContain("Couldn't read")
  })

  it('works in a conversation too', async () => {
    const { el, text } = mount({ messages, activeThreadId: 'A' })
    await type(el, '/')
    expect(el.querySelector('.t-thread-composer__suggestions [data-slash-menu]')).not.toBeNull()
    key(el, 'Tab')
    await settle()
    expect(text.value).toBe('/compact ')
  })

  it('without slashCommands there is no menu and no combobox role', async () => {
    const { el, onQuery } = mount({ slashCommands: null })
    await type(el, '/')
    expect(el.querySelector('[data-slash-menu]')).toBeNull()
    expect(box(el).getAttribute('role')).toBeNull()
    expect(onQuery).toHaveBeenCalledWith('')
  })
})
