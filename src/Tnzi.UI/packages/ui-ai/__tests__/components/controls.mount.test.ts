/**
 * The controls (`components/controls`): what each one renders and, for the ones
 * a keyboard drives, the keys. `TSelect`, `TMultiSelect`, `TDropdownMenu` and
 * `TSegmented` are in `controlsKeyboard.mount.test.ts`.
 */
import { afterEach, describe, expect, it } from 'vitest'
import { createApp, h, nextTick, ref, type App } from 'vue'

const { TButton, TIconButton, TField, TSwitch, TTag, TNotice, TSpinner } = await import('../../src/components/controls/index')

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

describe('TButton', () => {
  it('is a 30px secondary button by default, a 36px one at md', () => {
    const root = mount(() => [h(TButton, null, () => 'Save'), h(TButton, { size: 'md', variant: 'primary' }, () => 'Go')])
    const [plain, large] = root.querySelectorAll('button')
    expect(plain!.className).toContain('t-button--secondary')
    expect(plain!.className).toContain('t-button--sm')
    expect(plain!.getAttribute('type')).toBe('button')
    expect(large!.className).toContain('t-button--primary')
    expect(large!.className).toContain('t-button--md')
  })

  it('refuses clicks and says it is busy while loading', async () => {
    let clicks = 0
    const root = mount(() => h(TButton, { loading: true, onClick: () => clicks++ }, () => 'Save'))
    const button = root.querySelector('button')!
    expect(button.disabled).toBe(true)
    expect(button.getAttribute('aria-busy')).toBe('true')
    button.click()
    await nextTick()
    expect(clicks).toBe(0)
  })

  it('passes a submit type through', () => {
    const root = mount(() => h(TButton, { type: 'submit' }, () => 'Send'))
    expect(root.querySelector('button')!.getAttribute('type')).toBe('submit')
  })
})

describe('TIconButton', () => {
  it('is named by its label and says pressed only when it is a toggle', () => {
    const root = mount(() => [
      h(TIconButton, { icon: 'lucide:trash-2', label: 'Delete' }),
      h(TIconButton, { icon: 'lucide:pin', label: 'Pin', active: true }),
      h(TIconButton, { icon: 'lucide:pin', label: 'Pin', active: false }),
    ])
    const [plain, on, off] = root.querySelectorAll('button')
    expect(plain!.getAttribute('aria-label')).toBe('Delete')
    expect(plain!.hasAttribute('aria-pressed')).toBe(false)
    expect(on!.getAttribute('aria-pressed')).toBe('true')
    expect(off!.getAttribute('aria-pressed')).toBe('false')
  })
})

describe('TSwitch', () => {
  it('is a native button with the switch role, so Space and Enter press it as any button', () => {
    const root = mount(() => h(TSwitch, { modelValue: false, label: 'Notifications' }))
    const button = root.querySelector('button')!
    expect(button.getAttribute('type')).toBe('button')
    expect(button.getAttribute('role')).toBe('switch')
    expect(button.getAttribute('aria-checked')).toBe('false')
    expect(button.getAttribute('aria-label')).toBe('Notifications')
  })

  it('toggles on press and reflects the new state', async () => {
    const on = ref(false)
    const root = mount(() => h(TSwitch, { modelValue: on.value, 'onUpdate:modelValue': (v: boolean) => (on.value = v) }))
    const button = root.querySelector('button')!
    button.click()
    await nextTick()
    expect(on.value).toBe(true)
    expect(button.getAttribute('aria-checked')).toBe('true')
    button.click()
    await nextTick()
    expect(on.value).toBe(false)
  })

  it('takes no press while disabled or saving', async () => {
    const on = ref(false)
    const root = mount(() => [
      h(TSwitch, { modelValue: on.value, disabled: true, 'onUpdate:modelValue': (v: boolean) => (on.value = v) }),
      h(TSwitch, { modelValue: on.value, loading: true, 'onUpdate:modelValue': (v: boolean) => (on.value = v) }),
    ])
    const [disabled, saving] = root.querySelectorAll('button')
    disabled!.click()
    saving!.click()
    await nextTick()
    expect(on.value).toBe(false)
    expect(saving!.getAttribute('aria-busy')).toBe('true')
  })
})

describe('TField', () => {
  it('ties the label, the control and the line under it together', () => {
    const root = mount(() =>
      h(TField, { label: 'Name', hint: 'As shown to others', required: true }, {
        default: ({ id, describedBy, invalid }: { id: string; describedBy?: string; invalid: boolean }) =>
          h('input', { id, class: 't-input', 'aria-describedby': describedBy, 'aria-invalid': invalid }),
      }),
    )
    const label = root.querySelector('label')!
    const input = root.querySelector('input')!
    expect(label.getAttribute('for')).toBe(input.id)
    expect(label.textContent).toContain('*')
    const hint = root.querySelector(`#${CSS.escape(input.getAttribute('aria-describedby')!)}`)!
    expect(hint.textContent).toBe('As shown to others')
    expect(input.getAttribute('aria-invalid')).toBe('false')
  })

  it('shows an error instead of the hint, announced, and marks the control invalid', () => {
    const root = mount(() =>
      h(TField, { label: 'Name', hint: 'Hint', error: 'Required' }, {
        default: ({ id, describedBy, invalid }: { id: string; describedBy?: string; invalid: boolean }) =>
          h('input', { id, 'aria-describedby': describedBy, 'aria-invalid': invalid }),
      }),
    )
    const error = root.querySelector('[role="alert"]')!
    expect(error.textContent).toBe('Required')
    expect(root.textContent).not.toContain('Hint')
    expect(root.querySelector('input')!.getAttribute('aria-invalid')).toBe('true')
  })
})

describe('status pieces', () => {
  it('TTag carries its tone and an optional dot', () => {
    const root = mount(() => h(TTag, { tone: 'success', dot: true }, () => 'Online'))
    const tag = root.querySelector('.t-tag')!
    expect(tag.className).toContain('t-tag--success')
    expect(tag.querySelector('.t-tag__dot')).not.toBeNull()
  })

  it('TNotice announces an error only', () => {
    const root = mount(() => [h(TNotice, { tone: 'error' }, () => 'Failed'), h(TNotice, { tone: 'info' }, () => 'Note')])
    const [error, info] = root.querySelectorAll('.t-notice')
    expect(error!.getAttribute('role')).toBe('alert')
    expect(info!.hasAttribute('role')).toBe(false)
  })

  it('TSpinner is decoration', () => {
    const root = mount(() => h(TSpinner))
    expect(root.querySelector('.t-spinner')!.getAttribute('aria-hidden')).toBe('true')
  })
})
