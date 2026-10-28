/**
 * The right-side panel: the `TAsidePanel` frame (resize handle, keyboard, narrow layer) and its
 * wiring in `TChatApp` (`aside` slot, per-conversation state, toggle, programmatic open).
 */
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { createApp, h, nextTick, ref, type App } from 'vue'
import { useChatAside } from '../../src/headless/useAsidePanel'

const { default: TChatApp } = await import('../../src/components/chat/TChatApp.vue')
const { default: TAsidePanel } = await import('../../src/components/layout/TAsidePanel.vue')

let app: App | null = null
let host: HTMLElement | null = null

async function settle(): Promise<void> {
  for (let i = 0; i < 4; i += 1) {
    await Promise.resolve()
    await nextTick()
  }
}

function mount(render: () => unknown): HTMLElement {
  host = document.createElement('div')
  document.body.appendChild(host)
  app = createApp({ render: render as never })
  app.mount(host)
  return host
}

beforeEach(() => {
  localStorage.clear()
  Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1280 })
})
afterEach(() => {
  app?.unmount()
  host?.remove()
  app = null
  host = null
})

describe('TAsidePanel', () => {
  it('exposes an accessible, keyboard-operable resize handle', async () => {
    const width = ref(400)
    const el = mount(() =>
      h(TAsidePanel, { width: width.value, minWidth: 300, maxWidth: 500, 'onUpdate:width': (w: number) => (width.value = w) }, () => 'x'),
    )
    const handle = el.querySelector('[role="separator"]') as HTMLElement
    expect(handle.getAttribute('aria-valuenow')).toBe('400')
    expect(handle.getAttribute('aria-valuemin')).toBe('300')
    expect(handle.tabIndex).toBe(0)
    const press = (key: string, shiftKey = false) =>
      handle.dispatchEvent(new KeyboardEvent('keydown', { key, shiftKey, bubbles: true, cancelable: true }))
    press('ArrowLeft')
    await settle()
    expect(width.value).toBe(416)
    press('ArrowRight', true)
    await settle()
    expect(width.value).toBe(352)
    press('End')
    await settle()
    expect(width.value).toBe(500)
    press('Home')
    await settle()
    expect(width.value).toBe(300)
  })

  it('resizes by dragging the left edge, within the bounds', async () => {
    const width = ref(400)
    const el = mount(() =>
      h(TAsidePanel, { width: width.value, minWidth: 300, maxWidth: 500, 'onUpdate:width': (w: number) => (width.value = w) }),
    )
    const handle = el.querySelector('[role="separator"]') as HTMLElement
    handle.dispatchEvent(new MouseEvent('pointerdown', { clientX: 1000, bubbles: true, cancelable: true }))
    window.dispatchEvent(new MouseEvent('pointermove', { clientX: 950 }))
    await settle()
    expect(width.value).toBe(450)
    window.dispatchEvent(new MouseEvent('pointermove', { clientX: 0 }))
    await settle()
    expect(width.value).toBe(500)
    window.dispatchEvent(new MouseEvent('pointerup'))
    window.dispatchEvent(new MouseEvent('pointermove', { clientX: 1100 }))
    await settle()
    expect(width.value).toBe(500)
  })

  it('is a layer with a back button when narrow', () => {
    let closed = 0
    const el = mount(() => h(TAsidePanel, { narrow: true, onClose: () => (closed += 1) }, () => 'x'))
    expect(el.querySelector('[role="separator"]')).toBeNull()
    ;(el.querySelector('.t-aside-panel__back') as HTMLElement).click()
    el.querySelector('aside')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))
    expect(closed).toBe(2)
  })
})

describe('TChatApp aside', () => {
  const messages = [{ id: 'm1', role: 'user', content: 'hi' }]
  const baseProps = { sidebarStorageKey: 'aside-test-sidebar', asideStorageKey: 'aside-test', autoApplyTheme: false, messages }

  it('reserves nothing and shows no toggle without aside content', async () => {
    const el = mount(() => h(TChatApp, { ...baseProps, activeThreadId: 't1', asideDefaultOpen: true }))
    await settle()
    expect(el.querySelector('.t-aside-panel')).toBeNull()
    expect(el.querySelector('.t-chat-app__aside-toggle')).toBeNull()
  })

  it('renders the slot and toggles from the top bar', async () => {
    const el = mount(() =>
      h(TChatApp, { ...baseProps, activeThreadId: 't1' }, { aside: () => h('div', { class: 'probe' }, 'inside') }),
    )
    await settle()
    const toggle = el.querySelector('.t-chat-app__aside-toggle') as HTMLButtonElement
    expect(toggle.getAttribute('aria-label')).toBe('Show side panel')
    expect(el.querySelector('.probe')).toBeNull()
    toggle.click()
    await settle()
    expect(el.querySelector('.probe')?.textContent).toBe('inside')
    expect(toggle.getAttribute('aria-expanded')).toBe('true')
    toggle.click()
    await settle()
    expect(el.querySelector('.t-aside-panel')).toBeNull()
  })

  it('remembers open state per conversation', async () => {
    const thread = ref('t1')
    const el = mount(() =>
      h(TChatApp, { ...baseProps, activeThreadId: thread.value }, { aside: () => h('div', { class: 'probe' }) }),
    )
    await settle()
    ;(el.querySelector('.t-chat-app__aside-toggle') as HTMLElement).click()
    await settle()
    expect(el.querySelector('.probe')).not.toBeNull()
    thread.value = 't2'
    await settle()
    expect(el.querySelector('.probe')).toBeNull()
    thread.value = 't1'
    await settle()
    expect(el.querySelector('.probe')).not.toBeNull()
  })

  it('lets a renderer inside the conversation open it', async () => {
    const Opener = {
      setup() {
        const aside = useChatAside()
        return () => h('button', { class: 'opener', onClick: () => aside?.show('changes') })
      },
    }
    const el = mount(() =>
      h(TChatApp, { ...baseProps, activeThreadId: 't1' }, { aside: () => h('div', { class: 'probe' }), message: () => h(Opener) }),
    )
    await settle()
    ;(el.querySelector('.opener') as HTMLElement).click()
    await settle()
    expect(el.querySelector('.probe')).not.toBeNull()
  })

  it('passes the tab to the slot and is a closable full-size layer on a phone', async () => {
    let seen: string | undefined
    const el = mount(() =>
      h(
        TChatApp,
        { ...baseProps, activeThreadId: 't1', isMobile: true, asideDefaultOpen: true, asideDefaultTab: 'diff' },
        {
          aside: (p: { tab?: string }) => {
            seen = p.tab
            return h('div', { class: 'probe' })
          },
        },
      ),
    )
    await settle()
    expect(el.querySelector('.t-aside-panel--narrow')).not.toBeNull()
    expect(seen).toBe('diff')
    ;(el.querySelector('.t-aside-panel__back') as HTMLElement).click()
    await settle()
    expect(el.querySelector('.t-aside-panel')).toBeNull()
  })
})
