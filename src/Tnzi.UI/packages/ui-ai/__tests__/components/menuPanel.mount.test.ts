/**
 * `TMenuPanel`: the menu that does not float. A page that is itself a menu (a
 * tray window) puts `TMenuItem`s in it and gets the entries `TPopoverMenu`
 * draws, because `TPopoverMenu` draws its menu with this same panel.
 */
import { afterEach, describe, expect, it } from 'vitest'
import { createApp, h, nextTick, type App } from 'vue'

const { default: TMenuPanel } = await import('../../src/components/overlay/TMenuPanel.vue')
const { default: TPopoverMenu } = await import('../../src/components/overlay/TPopoverMenu.vue')
const { default: TDropdownMenu } = await import('../../src/components/overlay/TDropdownMenu.vue')
const { default: TMenuItem } = await import('../../src/components/overlay/TMenuItem.vue')
const { default: TMenuSeparator } = await import('../../src/components/overlay/TMenuSeparator.vue')
const { default: TBadge } = await import('../../src/components/controls/TBadge.vue')

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

function key(target: Element, name: string): KeyboardEvent {
  const event = new KeyboardEvent('keydown', { key: name, bubbles: true, cancelable: true })
  target.dispatchEvent(event)
  return event
}

const entries = () => [
  h(TMenuItem, { icon: 'lucide:pencil' }, () => 'Open'),
  h(TMenuItem, { disabled: true }, () => 'Pause'),
  h(TMenuSeparator),
  h(TMenuItem, { danger: true }, () => 'Quit'),
]

describe('TMenuPanel', () => {
  it('is a menu in place, with a header line and the entries', () => {
    const el = mount(() => h(TMenuPanel, { label: 'Acme' }, entries))
    const panel = el.querySelector('.t-menu-panel')!
    expect(panel.getAttribute('role')).toBe('menu')
    expect(panel.querySelector('.t-popover-menu__head')?.textContent).toBe('Acme')
    expect(panel.querySelectorAll('[role="menuitem"]')).toHaveLength(3)
    expect(getComputedStyle(panel).position).not.toBe('fixed')
  })

  it('moves between the enabled entries with the arrow keys, Home and End', async () => {
    const el = mount(() => h(TMenuPanel, null, entries))
    const panel = el.querySelector('.t-menu-panel')!
    const [open, , quit] = Array.from(el.querySelectorAll<HTMLElement>('[role="menuitem"]'))

    expect(key(panel, 'ArrowDown').defaultPrevented).toBe(true)
    expect(document.activeElement).toBe(open)
    key(open!, 'ArrowDown')
    expect(document.activeElement).toBe(quit)
    key(quit!, 'ArrowDown')
    expect(document.activeElement).toBe(open)
    key(open!, 'End')
    expect(document.activeElement).toBe(quit)
    key(quit!, 'Home')
    expect(document.activeElement).toBe(open)
    expect(key(open!, 'a').defaultPrevented).toBe(false)
    await nextTick()
  })

  it('flat drops the frame; keyboard false leaves the keys alone', () => {
    const el = mount(() => h(TMenuPanel, { flat: true, keyboard: false }, entries))
    const panel = el.querySelector('.t-menu-panel')!
    expect(panel.classList.contains('t-menu-panel--flat')).toBe(true)
    expect(key(panel, 'ArrowDown').defaultPrevented).toBe(false)
  })

  // One panel for both, so the floating menu and the one in place match.
  it('is the panel TPopoverMenu draws, which leaves the keys to its opener', () => {
    const el = mount(() => h('div', { style: 'position: relative' }, [h(TPopoverMenu, { modelValue: true, label: 'More' }, entries)]))
    const menu = el.querySelector('.t-popover-menu')!
    expect(menu.classList.contains('t-menu-panel')).toBe(true)
    expect(menu.getAttribute('role')).toBe('menu')
    expect(menu.querySelector('.t-popover-menu__head')?.textContent).toBe('More')
    expect(key(menu, 'ArrowDown').defaultPrevented).toBe(false)
  })

  // A tray window hides itself on Escape; the panel does not take it.
  it('leaves Escape and Tab to the page', () => {
    let escapes = 0
    const count = (e: KeyboardEvent) => {
      if (e.key === 'Escape') escapes += 1
    }
    document.addEventListener('keydown', count)
    const el = mount(() => h(TMenuPanel, null, entries))
    const first = el.querySelector<HTMLButtonElement>('[role="menuitem"]')!
    first.focus()
    expect(key(first, 'Escape').defaultPrevented).toBe(false)
    expect(escapes).toBe(1)
    expect(key(first, 'Tab').defaultPrevented).toBe(false)
    document.removeEventListener('keydown', count)
  })

  // The opener handles the keys; were the panel to handle them too, one press
  // would move focus by two entries.
  it('in a TDropdownMenu, an arrow moves focus by one entry', async () => {
    const el = mount(() =>
      h(TDropdownMenu, null, {
        trigger: ({ toggle }: { toggle: () => void }) => h('button', { class: 'trigger', onClick: toggle }, 'Open'),
        default: () => [h(TMenuItem, null, () => 'One'), h(TMenuItem, null, () => 'Two'), h(TMenuItem, null, () => 'Three')],
      }),
    )
    el.querySelector<HTMLButtonElement>('.trigger')!.click()
    await nextTick()
    await nextTick()
    const menu = el.querySelector<HTMLElement>('.t-popover-menu')!
    const [one, two] = Array.from(menu.querySelectorAll<HTMLButtonElement>('[role="menuitem"]'))
    one!.focus()
    key(one!, 'ArrowDown')
    expect(document.activeElement).toBe(two)
  })
})

describe('TMenuPanel windowKeys', () => {
  function windowKey(name: string): KeyboardEvent {
    const event = new KeyboardEvent('keydown', { key: name, bubbles: true, cancelable: true })
    document.body.dispatchEvent(event)
    return event
  }

  it('with focus on nothing, Down goes to the first entry and Up to the last', () => {
    const el = mount(() => h(TMenuPanel, { windowKeys: true }, entries))
    const [open, , quit] = Array.from(el.querySelectorAll<HTMLElement>('[role="menuitem"]'))
    ;(document.activeElement as HTMLElement | null)?.blur()
    expect(windowKey('ArrowDown').defaultPrevented).toBe(true)
    expect(document.activeElement).toBe(open)
    open!.blur()
    windowKey('ArrowUp')
    expect(document.activeElement).toBe(quit)
    // From inside, one press moves one entry (the panel's own handler).
    key(quit!, 'ArrowDown')
    expect(document.activeElement).toBe(open)
  })

  it('leaves Escape, Tab and keys pressed in a field elsewhere on the page alone', () => {
    const field = document.createElement('input')
    document.body.appendChild(field)
    mount(() => h(TMenuPanel, { windowKeys: true }, entries))
    ;(document.activeElement as HTMLElement | null)?.blur()
    expect(windowKey('Escape').defaultPrevented).toBe(false)
    expect(windowKey('Tab').defaultPrevented).toBe(false)
    field.focus()
    expect(key(field, 'ArrowDown').defaultPrevented).toBe(false)
    expect(document.activeElement).toBe(field)
  })

  it('off by default, and stops listening when unmounted', () => {
    mount(() => h(TMenuPanel, null, entries))
    ;(document.activeElement as HTMLElement | null)?.blur()
    expect(windowKey('ArrowDown').defaultPrevented).toBe(false)
    app!.unmount()
    app = null
    const again = mount(() => h(TMenuPanel, { windowKeys: true }, entries))
    app!.unmount()
    app = null
    again.remove()
    expect(windowKey('ArrowDown').defaultPrevented).toBe(false)
  })
})

describe('TMenuItem ends', () => {
  it('#leading takes the icon box: an image, or a badged icon', () => {
    const el = mount(() =>
      h(TMenuPanel, null, () => [
        h(TMenuItem, { icon: 'lucide:pencil' }, { leading: () => h('img', { src: 'data:,', alt: '' }), default: () => 'App' }),
        h(TMenuItem, null, { leading: () => h(TBadge, { icon: 'lucide:pencil', count: 3 }), default: () => 'Inbox' }),
      ]),
    )
    const [app1, inbox] = Array.from(el.querySelectorAll('[role="menuitem"]'))
    expect(app1!.querySelector('.t-menu-item__leading img')).not.toBeNull()
    // The slot replaces `icon`, it does not add to it.
    expect(Array.from(app1!.children).map((c) => c.className.toString())).toEqual(['t-menu-item__leading', 't-menu-item__label'])
    expect(inbox!.querySelector('.t-menu-item__leading .t-badge [data-badge-mark]')?.textContent).toBe('3')
  })

  it('shortcut, #trailing and an expanding chevron at the end, in that order before the check', () => {
    const el = mount(() =>
      h(TMenuPanel, null, () => [
        h(TMenuItem, { shortcut: 'Ctrl+Q', expanded: false, checked: true }, { trailing: () => h('span', { class: 'dot' }), default: () => 'Quit' }),
      ]),
    )
    const item = el.querySelector('[role="menuitem"]')!
    const ends = Array.from(item.children).map((c) => c.className.toString())
    const at = (cls: string) => ends.findIndex((c) => c.includes(cls))
    expect(item.querySelector('.t-menu-item__shortcut')?.textContent).toBe('Ctrl+Q')
    expect(at('t-menu-item__shortcut')).toBeLessThan(at('t-menu-item__trailing'))
    expect(at('t-menu-item__trailing')).toBeLessThan(at('t-menu-item__chevron'))
    expect(at('t-menu-item__chevron')).toBeLessThan(at('t-popover-menu__item-check'))
    expect(item.getAttribute('aria-expanded')).toBe('false')
    expect(item.querySelector('.t-menu-item__chevron--open')).toBeNull()
  })

  it('expanded turns the chevron and sets aria-expanded; no chevron or attribute when unset', async () => {
    const el = mount(() =>
      h(TMenuPanel, null, () => [h(TMenuItem, { expanded: true }, () => 'More'), h(TMenuItem, null, () => 'Plain')]),
    )
    const [more, plain] = Array.from(el.querySelectorAll('[role="menuitem"]'))
    expect(more!.getAttribute('aria-expanded')).toBe('true')
    expect(more!.querySelector('.t-menu-item__chevron--open')).not.toBeNull()
    expect(plain!.hasAttribute('aria-expanded')).toBe(false)
    expect(plain!.querySelector('.t-menu-item__chevron')).toBeNull()
    await nextTick()
  })
})

