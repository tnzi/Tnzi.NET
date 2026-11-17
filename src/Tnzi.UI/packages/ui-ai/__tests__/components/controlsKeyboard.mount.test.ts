/**
 * The keyboard of the controls a keyboard drives: `TSelect` (as a native
 * select), `TMultiSelect`, `TDropdownMenu` and `TSegmented`. Escape in each of
 * them is taken and goes no further, so a dialog around the control stays
 * open: checked here with a listener on the document, where the page's overlay
 * stack listens.
 */
import { afterEach, describe, expect, it } from 'vitest'
import { createApp, h, nextTick, ref, type App } from 'vue'

const { TSelect, TMultiSelect } = await import('../../src/components/controls/index')
const { default: TDropdownMenu } = await import('../../src/components/overlay/TDropdownMenu.vue')
const { default: TMenuItem } = await import('../../src/components/overlay/TMenuItem.vue')
const { default: TMenuSeparator } = await import('../../src/components/overlay/TMenuSeparator.vue')
const { default: TSegmented } = await import('../../src/components/layout/TSegmented.vue')

let app: App | null = null
let host: HTMLElement | null = null

function mount(render: () => unknown): HTMLElement {
  host = document.createElement('div')
  document.body.appendChild(host)
  app = createApp({ render })
  app.mount(host)
  return host
}

afterEach(() => {
  app?.unmount()
  host?.remove()
  app = null
  host = null
  document.body.innerHTML = ''
})

function key(target: Element, name: string, init: KeyboardEventInit = {}): KeyboardEvent {
  const event = new KeyboardEvent('keydown', { key: name, bubbles: true, cancelable: true, ...init })
  target.dispatchEvent(event)
  return event
}

/** Records the Escapes that reached the document, where the overlay stack listens. */
function documentEscapes(): { count: () => number; stop: () => void } {
  let count = 0
  const listener = (event: KeyboardEvent) => {
    if (event.key === 'Escape') count++
  }
  document.addEventListener('keydown', listener)
  return { count: () => count, stop: () => document.removeEventListener('keydown', listener) }
}

const frame = () => new Promise<void>((resolve) => requestAnimationFrame(() => resolve()))

const flush = async () => {
  await nextTick()
  await nextTick()
}

describe('TSelect keyboard', () => {
  const OPTIONS = [
    { value: 'apple', label: 'Apple' },
    { value: 'banana', label: 'Banana', disabled: true },
    { value: 'cherry', label: 'Cherry' },
    { value: 'citrus', label: 'Citrus' },
  ]

  function mountSelect(initial = 'apple') {
    const value = ref(initial)
    let changes = 0
    const root = mount(() =>
      h('div', { onChange: () => changes++ }, [
        h(TSelect, { modelValue: value.value, options: OPTIONS, 'onUpdate:modelValue': (v: string) => (value.value = v), 'aria-label': 'Fruit' }),
      ]),
    )
    const field = root.querySelector<HTMLButtonElement>('button[role="combobox"]')!
    return { value, field, changes: () => changes }
  }

  const activeLabel = (field: HTMLElement) => document.getElementById(field.getAttribute('aria-activedescendant')!)?.textContent?.trim()

  it('puts the attributes on the field and shows the chosen label', () => {
    const { field } = mountSelect()
    expect(field.getAttribute('aria-label')).toBe('Fruit')
    expect(field.textContent?.trim()).toBe('Apple')
    expect(field.dataset.value).toBe('apple')
  })

  it('opens on ArrowDown at the chosen option and keeps focus on the field', async () => {
    const { field } = mountSelect('cherry')
    field.focus()
    key(field, 'ArrowDown')
    await flush()
    expect(field.getAttribute('aria-expanded')).toBe('true')
    expect(activeLabel(field)).toBe('Cherry')
    expect(document.activeElement).toBe(field)
  })

  it('moves past disabled options and wraps, Home and End go to the ends', async () => {
    const { field } = mountSelect()
    key(field, 'Enter')
    await flush()
    key(field, 'ArrowDown')
    await flush()
    expect(activeLabel(field)).toBe('Cherry')
    key(field, 'End')
    await flush()
    expect(activeLabel(field)).toBe('Citrus')
    key(field, 'ArrowDown')
    await flush()
    expect(activeLabel(field)).toBe('Apple')
    key(field, 'ArrowUp')
    await flush()
    expect(activeLabel(field)).toBe('Citrus')
    key(field, 'Home')
    await flush()
    expect(activeLabel(field)).toBe('Apple')
  })

  it('jumps to what is typed', async () => {
    const { field } = mountSelect()
    key(field, ' ')
    await flush()
    key(field, 'c')
    await flush()
    expect(activeLabel(field)).toBe('Cherry')
    key(field, 'i')
    await flush()
    expect(activeLabel(field)).toBe('Citrus')
  })

  it('typing while closed opens it at the first match', async () => {
    const { field } = mountSelect()
    key(field, 'c')
    await flush()
    expect(field.getAttribute('aria-expanded')).toBe('true')
    expect(activeLabel(field)).toBe('Cherry')
  })

  it('picks with Enter: emits, fires a native change, closes and keeps focus', async () => {
    const { field, value, changes } = mountSelect()
    field.focus()
    key(field, 'ArrowDown')
    await flush()
    key(field, 'ArrowDown')
    key(field, 'Enter')
    await flush()
    expect(value.value).toBe('cherry')
    expect(changes()).toBe(1)
    expect(field.getAttribute('aria-expanded')).toBe('false')
    expect(document.activeElement).toBe(field)
  })

  it('Escape closes the list and goes no further', async () => {
    const escapes = documentEscapes()
    const { field, value } = mountSelect()
    key(field, 'ArrowDown')
    await flush()
    const event = key(field, 'Escape')
    await flush()
    escapes.stop()
    expect(field.getAttribute('aria-expanded')).toBe('false')
    expect(event.defaultPrevented).toBe(true)
    expect(escapes.count()).toBe(0)
    expect(value.value).toBe('apple')
  })

  it('Tab closes the list and lets focus move on', async () => {
    const { field } = mountSelect()
    key(field, 'ArrowDown')
    await flush()
    const event = key(field, 'Tab')
    await flush()
    expect(field.getAttribute('aria-expanded')).toBe('false')
    expect(event.defaultPrevented).toBe(false)
  })

  it('ignores keys while an IME composes', async () => {
    const { field } = mountSelect()
    key(field, 'ArrowDown', { isComposing: true })
    await flush()
    expect(field.getAttribute('aria-expanded')).toBe('false')
  })

  it('shows the placeholder for a value that is none of the options', () => {
    const value = ref('')
    const root = mount(() => h(TSelect, { modelValue: value.value, options: OPTIONS, placeholder: 'Pick one' }))
    const field = root.querySelector('button')!
    expect(field.textContent?.trim()).toBe('Pick one')
    expect(field.className).toContain('is-placeholder')
  })
})

describe('TMultiSelect keyboard', () => {
  const OPTIONS = Array.from({ length: 8 }, (_, i) => ({ value: `p${i}`, label: `Project ${i}` }))

  function mountMulti(initial: string[] = ['p1']) {
    const value = ref<string[]>(initial)
    /** The `change` events a form around the control hears, by what fired them. */
    const changes: Element[] = []
    const root = mount(() =>
      h('form', { onChange: (event: Event) => changes.push(event.target as Element) }, [
        h(TMultiSelect, {
          modelValue: value.value,
          options: OPTIONS,
          label: 'Projects',
          removeLabel: 'Remove {name}',
          'onUpdate:modelValue': (v: string[]) => (value.value = v),
        }),
      ]),
    )
    const toggle = root.querySelector<HTMLButtonElement>('[data-multi-toggle]')!
    return { root, value, toggle, changes }
  }

  it('every change of the value is one bubbling change: a checkbox fires its own, a removed chip fires one on the field', async () => {
    const { root, value, toggle, changes } = mountMulti()
    toggle.click()
    await flush()
    root.querySelectorAll<HTMLInputElement>('input[type="checkbox"]')[3]!.click()
    await flush()
    expect(value.value).toEqual(['p1', 'p3'])
    expect(changes).toHaveLength(1)
    expect(changes[0]!.matches('input[type="checkbox"]')).toBe(true)

    root.querySelector<HTMLButtonElement>('[aria-label="Remove Project 1"]')!.click()
    await flush()
    expect(value.value).toEqual(['p3'])
    expect(changes).toHaveLength(2)
    expect(changes[1]).toBe(root.querySelector('[data-multi-field]'))
  })

  it('opening, searching (the search box\'s own change included) and closing fire no change', async () => {
    const { root, toggle, changes } = mountMulti()
    toggle.click()
    await flush()
    const search = root.querySelector<HTMLInputElement>('[data-multi-search]')!
    search.value = 'project 7'
    search.dispatchEvent(new Event('input', { bubbles: true }))
    search.dispatchEvent(new Event('change', { bubbles: true }))
    await flush()
    key(search, 'Escape')
    await flush()
    expect(changes).toEqual([])
  })

  it('opens from its toggle, with the search focused when there are many options', async () => {
    const { root, toggle } = mountMulti()
    toggle.click()
    await flush()
    expect(toggle.getAttribute('aria-expanded')).toBe('true')
    expect(document.activeElement).toBe(root.querySelector('[data-multi-search]'))
  })

  it('ticks and unticks through the checkboxes, and removes through a chip', async () => {
    const { root, value, toggle } = mountMulti()
    toggle.click()
    await flush()
    const boxes = root.querySelectorAll<HTMLInputElement>('input[type="checkbox"]')
    boxes[3]!.click()
    await flush()
    expect(value.value).toEqual(['p1', 'p3'])
    root.querySelector<HTMLButtonElement>('[aria-label="Remove Project 1"]')!.click()
    await flush()
    expect(value.value).toEqual(['p3'])
  })

  it('filters by the search', async () => {
    const { root, toggle } = mountMulti()
    toggle.click()
    await flush()
    const search = root.querySelector<HTMLInputElement>('[data-multi-search]')!
    search.value = 'project 7'
    search.dispatchEvent(new Event('input'))
    await flush()
    expect(root.querySelectorAll('input[type="checkbox"]').length).toBe(1)
  })

  it('Escape closes the list, gives focus back to the toggle and goes no further', async () => {
    const escapes = documentEscapes()
    const { root, toggle } = mountMulti()
    toggle.click()
    await flush()
    key(root.querySelector('[data-multi-search]')!, 'Escape')
    await flush()
    escapes.stop()
    expect(toggle.getAttribute('aria-expanded')).toBe('false')
    expect(document.activeElement).toBe(toggle)
    expect(escapes.count()).toBe(0)
  })

  it('a press outside closes it', async () => {
    const { toggle } = mountMulti()
    toggle.click()
    await flush()
    document.body.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }))
    await flush()
    expect(toggle.getAttribute('aria-expanded')).toBe('false')
  })
})

describe('TDropdownMenu keyboard', () => {
  function mountMenu() {
    const picked: string[] = []
    const root = mount(() =>
      h(TDropdownMenu, null, {
        trigger: ({ toggle, attrs }: { toggle: () => void; attrs: Record<string, unknown> }) =>
          h('button', { type: 'button', class: 'trigger', ...attrs, onClick: toggle }, 'More'),
        default: ({ close }: { close: () => void }) => [
          h(TMenuItem, { icon: 'lucide:pencil', onClick: () => (picked.push('rename'), close()) }, () => 'Rename'),
          h(TMenuItem, { disabled: true }, () => 'Move'),
          h(TMenuSeparator),
          h(TMenuItem, { danger: true, onClick: () => (picked.push('delete'), close()) }, () => 'Delete'),
        ],
      }),
    )
    const trigger = root.querySelector<HTMLButtonElement>('.trigger')!
    const items = () => Array.from(document.querySelectorAll<HTMLElement>('[role="menuitem"]'))
    return { root, trigger, items, picked }
  }

  it('the trigger says it opens a menu and whether it is open', async () => {
    const { trigger } = mountMenu()
    expect(trigger.getAttribute('aria-haspopup')).toBe('menu')
    expect(trigger.getAttribute('aria-expanded')).toBe('false')
    trigger.click()
    await flush()
    expect(trigger.getAttribute('aria-expanded')).toBe('true')
  })

  it('focuses the first entry only once the menu is placed (a browser will not focus a hidden one)', async () => {
    const { trigger } = mountMenu()
    const hiddenAtFocus: boolean[] = []
    const focus = HTMLElement.prototype.focus
    HTMLElement.prototype.focus = function (this: HTMLElement, options?: FocusOptions) {
      if (this.getAttribute('role') === 'menuitem') hiddenAtFocus.push(!!this.closest('.t-popover-menu--placing'))
      return focus.call(this, options)
    }
    try {
      trigger.click()
      await flush()
      await frame()
    } finally {
      HTMLElement.prototype.focus = focus
    }
    expect(hiddenAtFocus).toEqual([false])
  })

  it('opening moves focus to the first entry; Up and Down skip disabled entries and wrap', async () => {
    const { trigger, items } = mountMenu()
    trigger.click()
    await flush()
    await frame()
    const [rename, , remove] = items()
    expect(document.activeElement).toBe(rename)
    key(rename!, 'ArrowDown')
    expect(document.activeElement).toBe(remove)
    key(remove!, 'ArrowDown')
    expect(document.activeElement).toBe(rename)
    key(rename!, 'ArrowUp')
    expect(document.activeElement).toBe(remove)
    key(remove!, 'Home')
    expect(document.activeElement).toBe(rename)
    key(rename!, 'End')
    expect(document.activeElement).toBe(remove)
  })

  it('Escape closes it, gives focus back to the trigger and goes no further', async () => {
    const escapes = documentEscapes()
    const { trigger, items } = mountMenu()
    trigger.click()
    await flush()
    key(items()[0]!, 'Escape')
    await flush()
    escapes.stop()
    expect(items()).toHaveLength(0)
    expect(document.activeElement).toBe(trigger)
    expect(escapes.count()).toBe(0)
  })

  it('Tab closes it without holding focus', async () => {
    const { trigger, items } = mountMenu()
    trigger.click()
    await flush()
    const event = key(items()[0]!, 'Tab')
    await flush()
    expect(items()).toHaveLength(0)
    expect(event.defaultPrevented).toBe(false)
  })

  it('an entry runs and closes the menu through the slot', async () => {
    const { trigger, items, picked } = mountMenu()
    trigger.click()
    await flush()
    items()[2]!.click()
    await flush()
    expect(picked).toEqual(['delete'])
    expect(items()).toHaveLength(0)
  })
})

describe('TSegmented', () => {
  const OPTIONS = [
    { value: 'all', label: 'All' },
    { value: 'waiting', label: 'Waiting', badge: 2, badgeLabel: '2 need you', attrs: { 'data-tab': 'waiting', role: 'tab' } },
    { value: 'done', label: 'Done', badge: 0 },
  ] as const

  it('moves the choice with the arrow keys, wrapping', async () => {
    const value = ref<string>('all')
    const root = mount(() =>
      h(TSegmented, { modelValue: value.value, options: OPTIONS, label: 'Filter', 'onUpdate:modelValue': (v: string) => (value.value = v) }),
    )
    const [first] = root.querySelectorAll<HTMLButtonElement>('[role="radio"]')
    key(first!, 'ArrowRight')
    await flush()
    expect(value.value).toBe('waiting')
    key(first!, 'ArrowLeft')
    await flush()
    expect(value.value).toBe('all')
    key(first!, 'ArrowLeft')
    await flush()
    expect(value.value).toBe('done')
  })

  it('shows a badge only above zero, with its words for a screen reader', () => {
    const root = mount(() => h(TSegmented, { modelValue: 'all', options: OPTIONS, label: 'Filter' }))
    const badges = root.querySelectorAll('[data-segment-badge]')
    expect(badges).toHaveLength(1)
    expect(badges[0]!.textContent).toContain('2')
    expect(badges[0]!.querySelector('.sr-only')!.textContent).toBe('2 need you')
  })

  it('binds an option\'s attributes without letting them change its role', () => {
    const root = mount(() => h(TSegmented, { modelValue: 'all', options: OPTIONS, label: 'Filter' }))
    const waiting = root.querySelector<HTMLButtonElement>('[data-tab="waiting"]')!
    expect(waiting).not.toBeNull()
    expect(waiting.getAttribute('role')).toBe('radio')
  })
})
