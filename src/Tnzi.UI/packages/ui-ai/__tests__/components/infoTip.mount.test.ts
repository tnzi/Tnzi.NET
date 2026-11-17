/**
 * `TInfoTip`: supplementary information behind a named button, opened by a
 * click or a tap alike, in a non-modal panel that Escape closes back to the
 * button.
 */
import { afterEach, describe, expect, it } from 'vitest'
import { createApp, h, nextTick, type App } from 'vue'

const { TInfoTip } = await import('../../src/components/controls/index')

let app: App | null = null

function mount(render: () => unknown): HTMLElement {
  const host = document.createElement('div')
  document.body.appendChild(host)
  app = createApp({ render })
  app.mount(host)
  return host
}

afterEach(() => {
  app?.unmount()
  app = null
  document.body.innerHTML = ''
})

const frame = () => new Promise<void>((resolve) => requestAnimationFrame(() => resolve()))
const flush = async () => {
  await nextTick()
  await nextTick()
}

describe('TInfoTip', () => {
  it('a named button that opens the text in a dialog panel, and Escape gives focus back', async () => {
    const root = mount(() => h(TInfoTip, { text: 'Sorting orders only the rows shown on this page.' }))
    const button = root.querySelector<HTMLButtonElement>('[data-info-tip-button]')!
    expect(button.getAttribute('aria-label')).toBe('More information')
    expect(button.getAttribute('aria-haspopup')).toBe('dialog')
    expect(button.getAttribute('aria-expanded')).toBe('false')
    button.focus()
    button.click()
    await flush()
    await frame()
    const panel = document.querySelector<HTMLElement>('[data-info-tip]')!
    expect(panel.getAttribute('role')).toBe('dialog')
    expect(panel.getAttribute('aria-label')).toBe('More information')
    expect(panel.textContent).toContain('Sorting orders only the rows shown on this page.')
    expect(button.getAttribute('aria-expanded')).toBe('true')
    panel.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }))
    await flush()
    expect(document.querySelector('[data-info-tip]')).toBeNull()
    expect(document.activeElement).toBe(button)
  })

  it('takes its own words for the button and the default slot for the text', async () => {
    const root = mount(() => h(TInfoTip, { label: 'About sorting' }, () => h('strong', 'Only this page')))
    root.querySelector<HTMLButtonElement>('[data-info-tip-button]')!.click()
    await flush()
    expect(root.querySelector('[data-info-tip-button]')!.getAttribute('aria-label')).toBe('About sorting')
    expect(document.querySelector('[data-info-tip] strong')!.textContent).toBe('Only this page')
  })
})
