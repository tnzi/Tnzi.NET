/**
 * The controls added for pages built on the controls alone: `TButton`'s
 * `soft` / `link` variants and `inverse`, `TIconButton`'s three sizes,
 * `TTag size="sm"`, `TSegmented`'s warning dot, `TSplitButton`, `TBadge`
 * and `TInlineField`.
 */
import { afterEach, describe, expect, it } from 'vitest'
import { createApp, h, nextTick, ref, type App } from 'vue'

const { TButton, TIconButton, TTag, TSplitButton, TBadge, TInlineField, TSelect } = await import('../../src/components/controls/index')
const { default: TMenuItem } = await import('../../src/components/overlay/TMenuItem.vue')
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

describe('TButton variants', () => {
  it('draws soft and link as their own variants, still native buttons', () => {
    const root = mount(() => [
      h(TButton, { variant: 'soft' }, () => 'Open'),
      h(TButton, { variant: 'link' }, () => 'View all commits'),
    ])
    const [soft, link] = root.querySelectorAll('button')
    expect(soft!.className).toContain('t-button--soft')
    expect(link!.className).toContain('t-button--link')
    expect(link!.getAttribute('type')).toBe('button')
    expect(link!.textContent).toBe('View all commits')
  })

  it('marks an inverse button, and only when asked', () => {
    const root = mount(() => [h(TButton, { variant: 'ghost', inverse: true }, () => 'Undo'), h(TButton, { variant: 'ghost' }, () => 'Undo')])
    const [inverse, plain] = root.querySelectorAll('button')
    expect(inverse!.className).toContain('t-button--inverse')
    expect(plain!.className).not.toContain('t-button--inverse')
  })
})

describe('TIconButton sizes', () => {
  it('is 30px (sm) by default, as tall as TButton; xs is the 28px of dense rows', () => {
    const root = mount(() => [
      h(TIconButton, { icon: 'lucide:x', label: 'Close' }),
      h(TIconButton, { icon: 'lucide:x', label: 'Close', size: 'xs' }),
      h(TIconButton, { icon: 'lucide:x', label: 'Close', size: 'md' }),
    ])
    const [sm, xs, md] = root.querySelectorAll('button')
    expect(sm!.className).toContain('t-icon-button--sm')
    expect(xs!.className).toContain('t-icon-button--xs')
    expect(md!.className).toContain('t-icon-button--md')
  })

  it('takes inverse for a dark bar', () => {
    const root = mount(() => h(TIconButton, { icon: 'lucide:x', label: 'Dismiss', inverse: true }))
    expect(root.querySelector('button')!.className).toContain('t-icon-button--inverse')
  })
})

describe('TTag size', () => {
  it('is 22px by default and 18px with size sm', () => {
    const root = mount(() => [h(TTag, null, () => 'Running'), h(TTag, { size: 'sm', dot: true, tone: 'success' }, () => 'Running')])
    const [md, sm] = root.querySelectorAll('.t-tag')
    expect(md!.className).not.toContain('t-tag--sm')
    expect(sm!.className).toContain('t-tag--sm')
    expect(sm!.querySelector('.t-tag__dot')).not.toBeNull()
  })
})

describe('TSegmented dot', () => {
  const options = [
    { value: 'a', label: 'Work' },
    { value: 'b', label: 'Home', dot: true, dotLabel: 'Sync failed' },
    { value: 'c', label: 'Old', dot: 'danger' as const },
    { value: 'd', label: 'Both', dot: true, badge: 2 },
  ]

  it('shows a dot with its screen reader words, danger when asked, and never beside a count', () => {
    const root = mount(() => h(TSegmented, { modelValue: 'a', options, label: 'Mailbox' }))
    const buttons = root.querySelectorAll('[role="radio"]')
    expect(buttons[0]!.querySelector('[data-segment-dot]')).toBeNull()
    const warning = buttons[1]!.querySelector('[data-segment-dot]')!
    expect(warning.className).not.toContain('--danger')
    expect(warning.textContent).toBe('Sync failed')
    expect(buttons[2]!.querySelector('[data-segment-dot]')!.className).toContain('t-segmented__dot--danger')
    expect(buttons[3]!.querySelector('[data-segment-dot]')).toBeNull()
    expect(buttons[3]!.querySelector('[data-segment-badge]')).not.toBeNull()
  })
})

describe('TSplitButton', () => {
  function mountSplit(props: Record<string, unknown> = {}) {
    const events: string[] = []
    const root = mount(() =>
      h(
        TSplitButton,
        { menuLabel: 'More ways to commit', onClick: () => events.push('commit'), ...props },
        {
          default: () => 'Commit',
          menu: ({ close }: { close: () => void }) => [
            h(TMenuItem, { onClick: () => (events.push('push'), close()) }, () => 'Commit and push'),
            h(TMenuItem, { onClick: () => (events.push('undo'), close()) }, () => 'Undo last commit'),
          ],
        },
      ),
    )
    const main = root.querySelector<HTMLButtonElement>('[data-split-main]')!
    const chevron = root.querySelector<HTMLButtonElement>('[data-split-menu]')!
    const items = () => Array.from(document.querySelectorAll<HTMLElement>('[role="menuitem"]'))
    return { root, main, chevron, items, events }
  }

  it('the main part acts; the chevron is named and says it opens a menu', async () => {
    const { main, chevron, events, items } = mountSplit()
    main.click()
    expect(events).toEqual(['commit'])
    expect(chevron.getAttribute('aria-label')).toBe('More ways to commit')
    expect(chevron.getAttribute('aria-haspopup')).toBe('menu')
    expect(chevron.getAttribute('aria-expanded')).toBe('false')
    expect(items()).toHaveLength(0)
    chevron.click()
    await flush()
    expect(chevron.getAttribute('aria-expanded')).toBe('true')
    expect(items()).toHaveLength(2)
  })

  it('Down on the chevron opens the menu and focuses its first entry; Escape gives focus back to the chevron', async () => {
    const { chevron, items } = mountSplit()
    chevron.focus()
    key(chevron, 'ArrowDown')
    await flush()
    await frame()
    expect(document.activeElement).toBe(items()[0])
    key(items()[0]!, 'Escape')
    await flush()
    expect(items()).toHaveLength(0)
    expect(document.activeElement).toBe(chevron)
  })

  it('choosing an entry runs it and closes the menu', async () => {
    const { chevron, items, events } = mountSplit()
    chevron.click()
    await flush()
    items()[1]!.click()
    await flush()
    expect(events).toEqual(['undo'])
    expect(items()).toHaveLength(0)
  })

  it('loading holds the main part only; disabled holds both', () => {
    const loading = mountSplit({ loading: true })
    expect(loading.main.disabled).toBe(true)
    expect(loading.main.getAttribute('aria-busy')).toBe('true')
    expect(loading.chevron.disabled).toBe(false)
    app?.unmount()
    document.body.innerHTML = ''
    const disabled = mountSplit({ disabled: true })
    expect(disabled.main.disabled).toBe(true)
    expect(disabled.chevron.disabled).toBe(true)
  })

  it('mainDisabled turns off the main part only: the menu still opens and its entries act', async () => {
    const { main, chevron, items, events } = mountSplit({ mainDisabled: true })
    expect(main.disabled).toBe(true)
    expect(chevron.disabled).toBe(false)
    main.click()
    expect(events).toEqual([])
    chevron.click()
    await flush()
    items()[1]!.click()
    await flush()
    expect(events).toEqual(['undo'])
  })

  it('carries its variant on both parts', () => {
    const { main, chevron } = mountSplit({ variant: 'soft' })
    expect(main.className).toContain('t-button--soft')
    expect(chevron.className).toContain('t-button--soft')
  })
})

describe('TBadge', () => {
  it('names an icon with a count as one image, and caps the number at max', () => {
    const root = mount(() => [
      h(TBadge, { icon: 'lucide:terminal', count: 3, label: '3 sessions running' }),
      h(TBadge, { icon: 'lucide:bell', count: 150 }),
    ])
    const [running, bell] = root.querySelectorAll<HTMLElement>('[data-badge]')
    expect(running!.getAttribute('role')).toBe('img')
    expect(running!.getAttribute('aria-label')).toBe('3 sessions running')
    expect(running!.querySelector('[data-badge-mark]')!.textContent).toBe('3')
    expect(running!.querySelector('[data-badge-mark]')!.getAttribute('aria-hidden')).toBe('true')
    expect(bell!.querySelector('[data-badge-mark]')!.textContent).toBe('99+')
    expect(bell!.hasAttribute('role')).toBe(false)
  })

  it('shows nothing at 0 unless asked, and a dot without a number', () => {
    const root = mount(() => [
      h(TBadge, { icon: 'lucide:bell', count: 0 }),
      h(TBadge, { icon: 'lucide:bell', count: 0, showZero: true }),
      h(TBadge, { icon: 'lucide:bell', dot: true, tone: 'danger' }),
    ])
    const [zero, shown, dot] = root.querySelectorAll<HTMLElement>('[data-badge]')
    expect(zero!.querySelector('[data-badge-mark]')).toBeNull()
    expect(shown!.querySelector('[data-badge-mark]')!.textContent).toBe('0')
    expect(dot!.querySelector('[data-badge-mark]')!.className).toContain('t-badge__mark--dot')
    expect(dot!.querySelector('[data-badge-mark]')!.textContent).toBe('')
    expect(dot!.className).toContain('t-badge--danger')
  })

  it('around a control, adds its words after the control instead of renaming it', () => {
    const root = mount(() => h(TBadge, { count: 2, label: '2 unread' }, () => h('button', { 'aria-label': 'Notifications' })))
    const badge = root.querySelector<HTMLElement>('[data-badge]')!
    expect(badge.hasAttribute('role')).toBe(false)
    expect(badge.querySelector('button')!.getAttribute('aria-label')).toBe('Notifications')
    expect(badge.querySelector('.sr-only')!.textContent).toBe('2 unread')
  })
})

describe('TInlineField', () => {
  it('labels its control through the slot id, and holds an end slot', () => {
    const to = ref('')
    const root = mount(() =>
      h(TInlineField, { label: 'To', labelWidth: 56 }, {
        default: ({ id }: { id: string }) => h('input', { id, class: 't-input t-input--bare', value: to.value }),
        end: () => h('button', { type: 'button' }, 'Cc'),
      }),
    )
    const label = root.querySelector('label')!
    const input = root.querySelector('input')!
    expect(label.getAttribute('for')).toBe(input.id)
    expect(input.id).not.toBe('')
    expect(label.style.width).toBe('56px')
    expect(root.querySelector('.t-inline-field__end button')!.textContent).toBe('Cc')
  })
})

describe('TSelect bare', () => {
  it('draws the field with no box when bare, and is labelled by an inline field row', () => {
    const from = ref('me')
    const root = mount(() =>
      h(TInlineField, { label: 'From' }, {
        default: ({ id }: { id: string }) =>
          h(TSelect, {
            id,
            bare: true,
            modelValue: from.value,
            options: [{ value: 'me', label: 'me@example.com' }, { value: 'team', label: 'team@example.com' }],
          }),
      }),
    )
    const field = root.querySelector<HTMLButtonElement>('.t-select')!
    expect(field.classList.contains('t-select--bare')).toBe(true)
    expect(root.querySelector('label')!.getAttribute('for')).toBe(field.id)
    expect(field.textContent).toContain('me@example.com')
  })

  it('is boxed by default', () => {
    const root = mount(() => h(TSelect, { modelValue: 'a', options: [{ value: 'a', label: 'A' }] }))
    expect(root.querySelector('.t-select')!.classList.contains('t-select--bare')).toBe(false)
  })
})
