/**
 * Defaults that used to be overrides in a consumer: `TSpinner`'s size,
 * `TSegmented`'s long labels, `TTag` and `TSpinner` not shrinking in a row,
 * resource cards as tall as their grid row (tighter on a phone), and the
 * settings row's label and control spacing. Layout is pinned on the declared
 * styles (happy-dom computes none).
 */
import { afterEach, describe, expect, it } from 'vitest'
import { createApp, h, nextTick, type App } from 'vue'
import { cssRule } from '../support/css-rule'

const { TSpinner } = await import('../../src/components/controls/index')
const { default: TSegmented } = await import('../../src/components/layout/TSegmented.vue')

let app: App | null = null
let host: HTMLElement | null = null

function mount(render: () => unknown): HTMLElement {
  host = document.createElement('div')
  document.body.appendChild(host)
  app = createApp({ render: render as never })
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

describe('TSpinner', () => {
  it('is 24px by default and 16px with size="sm"', async () => {
    const el = mount(() => [h(TSpinner), h(TSpinner, { size: 'sm' })])
    await nextTick()
    const [md, sm] = el.querySelectorAll('.t-spinner')
    expect(md!.classList.contains('t-spinner--sm')).toBe(false)
    expect(sm!.classList.contains('t-spinner--sm')).toBe(true)
    expect(sm!.getAttribute('aria-hidden')).toBe('true')
    expect(cssRule('components/controls/TSpinner.vue', '.t-spinner')).toMatch(/flex: none;[\s\S]*width: 24px;/)
    expect(cssRule('components/controls/TSpinner.vue', '.t-spinner--sm')).toMatch(/width: 16px;\s*height: 16px;/)
  })
})

describe('TTag', () => {
  it('never shrinks in a row', () => {
    expect(cssRule('components/controls/TTag.vue', '.t-tag')).toMatch(/flex-shrink: 0;/)
  })
})

describe('TSegmented long labels', () => {
  const options = [
    { value: 'a', label: 'a-very-long-address@example.com' },
    { value: 'b', label: 'Work' },
  ]

  it('cuts a label past 13em, except in a block group that shares the width', () => {
    const file = 'components/layout/TSegmented.vue'
    expect(cssRule(file, '.t-segmented__label')).toMatch(/max-width: 13em;\s*overflow: hidden;\s*text-overflow: ellipsis;/)
    expect(cssRule(file, '.t-segmented--block .t-segmented__label')).toMatch(/max-width: none;/)
  })

  it('shows a cut label whole in its tooltip, and nothing for one that fits', async () => {
    const el = mount(() => h(TSegmented, { modelValue: 'a', options, label: 'Mailbox' }))
    await nextTick()
    const [long, short] = el.querySelectorAll<HTMLElement>('.t-segmented__option')
    const cut = long!.querySelector('.t-segmented__label') as HTMLElement
    Object.defineProperty(cut, 'scrollWidth', { configurable: true, value: 300 })
    Object.defineProperty(cut, 'clientWidth', { configurable: true, value: 180 })
    const fits = short!.querySelector('.t-segmented__label') as HTMLElement
    Object.defineProperty(fits, 'scrollWidth', { configurable: true, value: 40 })
    Object.defineProperty(fits, 'clientWidth', { configurable: true, value: 40 })
    // Keyboard focus opens a tooltip at once.
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab' }))
    short!.dispatchEvent(new FocusEvent('focus'))
    expect(document.querySelector('.t-tooltip[data-open]')).toBeNull()
    long!.dispatchEvent(new FocusEvent('focus'))
    const bubble = document.querySelector('.t-tooltip[data-open]')
    expect(bubble?.textContent).toBe('a-very-long-address@example.com')
    // The option keeps its own text as its name.
    expect(long!.hasAttribute('aria-label')).toBe(false)
  })
})

describe('TResourceCard', () => {
  it('fills its grid row, and is tighter on a phone', () => {
    expect(cssRule('styles/index.css', '.t-resource-grid .t-resource-card')).toMatch(/height: 100%;/)
    expect(cssRule('styles/index.css', '.t-resource-grid')).not.toMatch(/align-items: start/)
    expect(cssRule('components/layout/TResourceCard.vue', '  .t-resource-card')).toMatch(/padding: 10px;\s*gap: 6px;/)
  })
})

describe('TSettingRow', () => {
  it('wraps the label with 6px between lines and 8px between items; the control spaces its items', () => {
    const label = cssRule('components/layout/TSettingRow.vue', '.t-setting-row__label')
    expect(label).toMatch(/display: flex;/)
    expect(label).toMatch(/flex-wrap: wrap;/)
    expect(label).toMatch(/gap: 6px 8px;/)
    expect(label).toMatch(/min-width: 0;/)
    const control = cssRule('components/layout/TSettingRow.vue', '.t-setting-row__control')
    expect(control).toMatch(/display: flex;[\s\S]*align-items: center;[\s\S]*gap: 8px;/)
  })
})

describe('TAuthField', () => {
  it('draws a field marked invalid in the danger colour', () => {
    expect(cssRule('auth/TAuthField.vue', ".t-auth-field[aria-invalid='true']")).toMatch(/border-color: var\(--tnzi-ai-danger\)/)
  })
})
