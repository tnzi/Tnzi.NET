/**
 * Feedback and floating surfaces added for pages built on the controls: a
 * toast with an action ("Undo"), `TContextMenu` (a menu at the pointer),
 * `TPopover` (a non-menu panel beside a trigger) and `TDockedDialog` (a
 * non-modal window in a corner that folds and grows).
 */
import { afterEach, describe, expect, it, vi } from 'vitest'
import { createApp, h, nextTick, ref, type App } from 'vue'

const { default: TToastHost } = await import('../../src/components/controls/TToastHost.vue')
const { createToaster, TOAST_ACTION_DURATION_MS, TOAST_DURATION_MS, TOAST_MAX_ACTIONS } = await import('../../src/components/controls/toast')
const { default: TContextMenu } = await import('../../src/components/overlay/TContextMenu.vue')
const { default: TPopover } = await import('../../src/components/overlay/TPopover.vue')
const { default: TDockedDialog } = await import('../../src/components/overlay/TDockedDialog.vue')
const { default: TMenuItem } = await import('../../src/components/overlay/TMenuItem.vue')

let apps: App[] = []

function mount(render: () => unknown): HTMLElement {
  const host = document.createElement('div')
  document.body.appendChild(host)
  const app = createApp({ render })
  app.mount(host)
  apps.push(app)
  return host
}

afterEach(() => {
  for (const app of apps) app.unmount()
  apps = []
  vi.useRealTimers()
  document.body.innerHTML = ''
})

function key(target: Element, name: string): KeyboardEvent {
  const event = new KeyboardEvent('keydown', { key: name, bubbles: true, cancelable: true })
  target.dispatchEvent(event)
  return event
}

const frame = () => new Promise<void>((resolve) => requestAnimationFrame(() => resolve()))
const flush = async () => {
  await nextTick()
  await nextTick()
}

function documentEscapes(): { count: () => number; stop: () => void } {
  let count = 0
  const listener = (event: KeyboardEvent) => {
    if (event.key === 'Escape') count++
  }
  document.addEventListener('keydown', listener)
  return { count: () => count, stop: () => document.removeEventListener('keydown', listener) }
}

describe('toast with an action', () => {
  it('stays long enough to reach the button unless told otherwise', () => {
    const toaster = createToaster()
    toaster.toast.info('Sent', { actions: [{ label: 'Undo', run: () => undefined }] })
    toaster.toast.info('Saved')
    toaster.toast.info('Sent', { actions: [{ label: 'Undo', run: () => undefined }], duration: 2000 })
    expect(toaster.items.value.map((item) => item.duration)).toEqual([TOAST_ACTION_DURATION_MS, TOAST_DURATION_MS, 2000])
    expect(TOAST_ACTION_DURATION_MS).toBeGreaterThan(TOAST_DURATION_MS)
  })

  it('draws the action as a button that runs it and takes the note away, focus going back', async () => {
    const toaster = createToaster()
    const runs: string[] = []
    const outside = document.createElement('button')
    document.body.appendChild(outside)
    mount(() => h(TToastHost, { toaster }))
    await flush()
    toaster.toast.success('Message sent', { actions: [{ label: 'Undo', run: () => runs.push('undo') }] })
    await flush()
    const action = document.querySelector<HTMLButtonElement>('[data-toast-action]')!
    expect(action.textContent?.trim()).toBe('Undo')
    expect(action.getAttribute('type')).toBe('button')
    outside.focus()
    action.focus()
    action.dispatchEvent(new FocusEvent('focusin', { bubbles: true, relatedTarget: outside }))
    action.click()
    await flush()
    expect(runs).toEqual(['undo'])
    expect(toaster.items.value).toHaveLength(0)
    expect(document.activeElement).toBe(outside)
  })

  it('a note without an action has no action button', async () => {
    const toaster = createToaster()
    mount(() => h(TToastHost, { toaster }))
    await flush()
    toaster.toast.info('Saved')
    await flush()
    expect(document.querySelector('[data-toast-action]')).toBeNull()
  })
})

describe('toast handle: two actions, updates, progress', () => {
  it('a note that counts down with Undo and Send now, then sends with a spinner, then says it is sent', async () => {
    vi.useFakeTimers()
    const toaster = createToaster()
    mount(() => h(TToastHost, { toaster }))
    await flush()
    const events: string[] = []
    const note = toaster.toast.info('Sending in 5 s', {
      duration: 0,
      actions: [
        { label: 'Undo', run: () => events.push('undo') },
        {
          label: 'Send now',
          keepOpen: true,
          run: () => {
            events.push('send')
            note.update({ text: 'Sending...', busy: true, actions: [] })
          },
        },
      ],
    })
    await flush()
    const el = () => document.querySelector<HTMLElement>('[data-toast]')!
    const buttons = () => Array.from(document.querySelectorAll<HTMLButtonElement>('[data-toast-action]')).map((b) => b.textContent?.trim())
    expect(buttons()).toEqual(['Undo', 'Send now'])

    note.update({ text: 'Sending in 4 s', quiet: true })
    await flush()
    const text = el().querySelector('[data-toast-text]')!
    expect(text.textContent).toBe('Sending in 4 s')
    expect(text.getAttribute('aria-live')).toBe('off')

    const sendNow = document.querySelectorAll<HTMLButtonElement>('[data-toast-action]')[1]!
    sendNow.focus()
    sendNow.click()
    await flush()
    expect(events).toEqual(['send'])
    expect(note.shown).toBe(true)
    expect(el().getAttribute('aria-busy')).toBe('true')
    expect(el().querySelector('.t-toast__spin')).not.toBeNull()
    expect(buttons()).toEqual([])
    expect(text.isConnected ? text.getAttribute('aria-live') : el().querySelector('[data-toast-text]')!.getAttribute('aria-live')).toBeNull()
    /* The action's own button went away: focus stays in the note, on its close button. */
    expect(document.activeElement).toBe(el().querySelector('[data-toast-close]'))

    note.update({ text: 'Sent', tone: 'success', busy: false, duration: 3000 })
    await flush()
    expect(el().dataset.toast).toBe('success')
    /* Held while focus is in it; it runs out once focus moves on. */
    vi.advanceTimersByTime(3000)
    expect(note.shown).toBe(true)
    const outside = document.createElement('button')
    document.body.appendChild(outside)
    el().querySelector('[data-toast-close]')!.dispatchEvent(new FocusEvent('focusout', { bubbles: true, relatedTarget: outside }))
    outside.focus()
    vi.advanceTimersByTime(3000)
    await flush()
    expect(note.shown).toBe(false)
    note.update({ text: 'ignored' })
    expect(toaster.items.value).toEqual([])
  })

  it('takes at most two actions', () => {
    const toaster = createToaster()
    const action = { label: 'A', run: () => undefined }
    expect(() => toaster.toast.info('x', { actions: [action, action, action] })).toThrow(RangeError)
    const note = toaster.toast.info('x', { actions: [action] })
    expect(() => note.update({ actions: [action, action, action] })).toThrow(RangeError)
    expect(TOAST_MAX_ACTIONS).toBe(2)
  })

  it('a new duration waits while the note is held by the pointer, and dismiss takes the handle or the id', () => {
    vi.useFakeTimers()
    const toaster = createToaster()
    const note = toaster.toast.info('Working', { duration: 0 })
    toaster.pause(note.id)
    note.update({ duration: 1000 })
    vi.advanceTimersByTime(2000)
    expect(note.shown).toBe(true)
    toaster.resume(note.id)
    vi.advanceTimersByTime(1000)
    expect(note.shown).toBe(false)
    const other = toaster.toast.info('Other', { duration: 0 })
    toaster.toast.dismiss(other)
    expect(other.shown).toBe(false)
  })
})

describe('TContextMenu', () => {
  function mountContext() {
    const menu = ref<InstanceType<typeof TContextMenu> | null>(null)
    const picked: string[] = []
    const root = mount(() => [
      h('div', { class: 'row', tabindex: 0, onContextmenu: (e: MouseEvent) => menu.value?.openAt(e) }, 'report.pdf'),
      h(TContextMenu, { ref: menu, label: 'report.pdf' }, {
        default: ({ close }: { close: () => void }) => [
          h(TMenuItem, { onClick: () => (picked.push('rename'), close()) }, () => 'Rename'),
          h(TMenuItem, { onClick: () => (picked.push('delete'), close()) }, () => 'Delete'),
        ],
      }),
    ])
    const row = root.querySelector<HTMLElement>('.row')!
    const items = () => Array.from(document.querySelectorAll<HTMLElement>('[role="menuitem"]'))
    const menuEl = () => document.querySelector<HTMLElement>('[data-context-menu]')
    return { row, items, menuEl, picked, menu }
  }

  it('opens at the pointer, instead of the browser menu', async () => {
    const { row, menuEl } = mountContext()
    const event = new MouseEvent('contextmenu', { bubbles: true, cancelable: true, clientX: 120, clientY: 80 })
    row.dispatchEvent(event)
    await flush()
    expect(event.defaultPrevented).toBe(true)
    const el = menuEl()!
    expect(el.getAttribute('role')).toBe('menu')
    expect(el.style.left).toBe('120px')
    expect(el.style.top).toBe('80px')
  })

  it('from the keyboard (no pointer position) opens under the focused element', async () => {
    const { row, menuEl } = mountContext()
    row.getBoundingClientRect = () => ({ left: 40, top: 10, right: 200, bottom: 30, width: 160, height: 20, x: 40, y: 10, toJSON: () => ({}) })
    row.focus()
    row.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true }))
    await flush()
    expect(menuEl()!.style.left).toBe('40px')
    expect(menuEl()!.style.top).toBe('30px')
  })

  it('focuses the first entry; arrows move; Escape closes it, goes no further and gives focus back', async () => {
    const escapes = documentEscapes()
    const { row, items } = mountContext()
    row.focus()
    row.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true, clientX: 10, clientY: 10 }))
    await flush()
    await frame()
    const [rename, remove] = items()
    expect(document.activeElement).toBe(rename)
    key(rename!, 'ArrowDown')
    expect(document.activeElement).toBe(remove)
    key(remove!, 'Escape')
    await flush()
    escapes.stop()
    expect(items()).toHaveLength(0)
    expect(escapes.count()).toBe(0)
    expect(document.activeElement).toBe(row)
  })

  it('a press outside closes it; a press inside does not', async () => {
    const { row, items, menuEl } = mountContext()
    row.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true, clientX: 10, clientY: 10 }))
    await flush()
    menuEl()!.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }))
    await flush()
    expect(items()).toHaveLength(2)
    document.body.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }))
    await flush()
    expect(items()).toHaveLength(0)
  })

  it('choosing an entry runs it and closes', async () => {
    const { menu, items, picked } = mountContext()
    menu.value!.openAt({ x: 5, y: 5 })
    await flush()
    items()[1]!.click()
    await flush()
    expect(picked).toEqual(['delete'])
    expect(items()).toHaveLength(0)
  })
})

describe('TPopover', () => {
  function mountPopover() {
    const root = mount(() => [
      h(TPopover, { label: 'Merge status', class: 'details' }, {
        trigger: ({ toggle, attrs }: { toggle: () => void; attrs: Record<string, unknown> }) =>
          h('button', { type: 'button', class: 'trigger', ...attrs, onClick: toggle }, 'Checks passed'),
        default: () => [h('p', 'All 12 checks passed.'), h('a', { href: '#log', class: 'link' }, 'Open the log')],
      }),
      h('button', { type: 'button', class: 'after' }, 'Next'),
    ])
    const trigger = root.querySelector<HTMLButtonElement>('.trigger')!
    const panel = () => document.querySelector<HTMLElement>('[data-popover]')
    return { root, trigger, panel }
  }

  it('the trigger says it opens a dialog, whether it is open and which panel', async () => {
    const { trigger, panel } = mountPopover()
    expect(trigger.getAttribute('aria-haspopup')).toBe('dialog')
    expect(trigger.getAttribute('aria-expanded')).toBe('false')
    trigger.click()
    await flush()
    expect(trigger.getAttribute('aria-expanded')).toBe('true')
    const el = panel()!
    expect(trigger.getAttribute('aria-controls')).toBe(el.id)
    expect(el.getAttribute('role')).toBe('dialog')
    expect(el.hasAttribute('aria-modal')).toBe(false)
    expect(el.getAttribute('aria-label')).toBe('Merge status')
    expect(el.className).toContain('details')
  })

  it('moves focus into the panel once placed; Escape closes it, goes no further and focus goes back to the trigger', async () => {
    const escapes = documentEscapes()
    const { trigger, panel } = mountPopover()
    trigger.focus()
    trigger.click()
    await flush()
    await frame()
    expect(document.activeElement).toBe(panel())
    key(panel()!, 'Escape')
    await flush()
    escapes.stop()
    expect(panel()).toBeNull()
    expect(escapes.count()).toBe(0)
    expect(document.activeElement).toBe(trigger)
  })

  it('stays in the Tab order after the trigger, and closes when focus leaves it', async () => {
    const { root, trigger, panel } = mountPopover()
    trigger.click()
    await flush()
    expect(trigger.parentElement!.nextElementSibling).toBe(panel())
    const link = panel()!.querySelector<HTMLElement>('.link')!
    link.dispatchEvent(new FocusEvent('focusout', { bubbles: true, relatedTarget: root.querySelector('.after') }))
    await flush()
    expect(panel()).toBeNull()
  })

  it('a press outside closes it; the trigger toggles it', async () => {
    const { trigger, panel } = mountPopover()
    trigger.click()
    await flush()
    panel()!.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }))
    await flush()
    expect(panel()).not.toBeNull()
    document.body.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }))
    await flush()
    expect(panel()).toBeNull()
    trigger.click()
    await flush()
    trigger.click()
    await flush()
    expect(panel()).toBeNull()
  })
})

describe('TDockedDialog', () => {
  function mountDocked(props: Record<string, unknown> = {}) {
    const open = ref(false)
    const minimized = ref(false)
    const maximized = ref(false)
    const root = mount(() => [
      h('button', { type: 'button', class: 'opener', onClick: () => (open.value = true) }, 'Compose'),
      h(
        TDockedDialog,
        {
          modelValue: open.value,
          'onUpdate:modelValue': (value: boolean) => (open.value = value),
          minimized: minimized.value,
          'onUpdate:minimized': (value: boolean) => (minimized.value = value),
          maximized: maximized.value,
          'onUpdate:maximized': (value: boolean) => (maximized.value = value),
          title: 'New message',
          narrow: false,
          ...props,
        },
        { default: () => h('input', { class: 'subject' }) },
      ),
    ])
    const opener = root.querySelector<HTMLButtonElement>('.opener')!
    const panel = () => document.querySelector<HTMLElement>('[data-docked-dialog]')
    return { opener, panel, open, minimized, maximized }
  }

  it('is a non-modal dialog named by its title, focused into its first field', async () => {
    const { opener, panel } = mountDocked()
    opener.focus()
    opener.click()
    await flush()
    const el = panel()!
    expect(el.getAttribute('role')).toBe('dialog')
    expect(el.hasAttribute('aria-modal')).toBe(false)
    expect(document.getElementById(el.getAttribute('aria-labelledby')!)!.textContent).toBe('New message')
    expect(el.dataset.state).toBe('docked')
    expect(document.activeElement).toBe(el.querySelector('.subject'))
  })

  it('folds to its title row keeping what was typed, focus on the restore button; restoring focuses the field again', async () => {
    const { opener, panel, minimized } = mountDocked()
    opener.click()
    await flush()
    const subject = panel()!.querySelector<HTMLInputElement>('.subject')!
    subject.value = 'Hello'
    const fold = panel()!.querySelector<HTMLButtonElement>('[data-docked-minimize]')!
    expect(fold.getAttribute('aria-expanded')).toBe('true')
    fold.click()
    await flush()
    expect(minimized.value).toBe(true)
    expect(panel()!.dataset.state).toBe('minimized')
    expect(panel()!.querySelector<HTMLElement>('.t-docked-dialog__body')!.style.display).toBe('none')
    expect(panel()!.querySelector<HTMLInputElement>('.subject')!.value).toBe('Hello')
    expect(document.activeElement).toBe(fold)
    expect(fold.getAttribute('aria-expanded')).toBe('false')
    fold.click()
    await flush()
    expect(minimized.value).toBe(false)
    expect(document.activeElement).toBe(panel()!.querySelector('.subject'))
  })

  it('a click on the folded title row opens it again', async () => {
    const { opener, panel, minimized } = mountDocked()
    opener.click()
    await flush()
    panel()!.querySelector<HTMLButtonElement>('[data-docked-minimize]')!.click()
    await flush()
    panel()!.querySelector<HTMLElement>('.t-docked-dialog__title')!.click()
    await flush()
    expect(minimized.value).toBe(false)
  })

  it('maximise is a pressed toggle', async () => {
    const { opener, panel, maximized } = mountDocked()
    opener.click()
    await flush()
    const grow = panel()!.querySelector<HTMLButtonElement>('[data-docked-maximize]')!
    expect(grow.getAttribute('aria-pressed')).toBe('false')
    grow.click()
    await flush()
    expect(maximized.value).toBe(true)
    expect(panel()!.dataset.state).toBe('maximized')
    expect(grow.getAttribute('aria-pressed')).toBe('true')
  })

  it('closes through beforeClose and gives focus back to what opened it', async () => {
    const reasons: string[] = []
    let allow = false
    const { opener, panel, open } = mountDocked({
      beforeClose: (reason: string) => {
        reasons.push(reason)
        return allow
      },
    })
    opener.focus()
    opener.click()
    await flush()
    const closeButton = panel()!.querySelector<HTMLButtonElement>('[data-docked-close]')!
    closeButton.click()
    await flush()
    expect(open.value).toBe(true)
    allow = true
    closeButton.focus()
    closeButton.click()
    await flush()
    await flush()
    expect(reasons).toEqual(['close-button', 'close-button'])
    expect(open.value).toBe(false)
    expect(document.activeElement).toBe(opener)
  })

  it('Escape closes it only with closeOnEscape', async () => {
    const first = mountDocked()
    first.opener.click()
    await flush()
    key(first.panel()!.querySelector('.subject')!, 'Escape')
    await flush()
    expect(first.open.value).toBe(true)
    for (const app of apps) app.unmount()
    apps = []
    document.body.innerHTML = ''
    const second = mountDocked({ closeOnEscape: true })
    second.opener.click()
    await flush()
    key(second.panel()!.querySelector('.subject')!, 'Escape')
    await flush()
    expect(second.open.value).toBe(false)
  })

  it('on a phone fills the screen and has no maximise button', async () => {
    const { opener, panel } = mountDocked({ narrow: true })
    opener.click()
    await flush()
    expect(panel()!.dataset.state).toBe('full')
    expect(panel()!.querySelector('[data-docked-maximize]')).toBeNull()
  })
})
