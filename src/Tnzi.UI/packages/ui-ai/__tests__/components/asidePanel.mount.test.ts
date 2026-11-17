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

  describe('asideFullHeight', () => {
    const FULL = 't-chat-app__main--aside-full-height'
    const withAside = { aside: () => h('div', { class: 'probe' }) }
    const isFull = (el: HTMLElement) => el.querySelector('.t-chat-app__main')!.classList.contains(FULL)

    it('is off by default: the panel stays in the workspace under a plain main area', async () => {
      const el = mount(() => h(TChatApp, { ...baseProps, activeThreadId: 't1', asideDefaultOpen: true }, withAside))
      await settle()
      expect(isFull(el)).toBe(false)
      expect(el.querySelector('.t-chat-app__workspace > .t-aside-panel')).not.toBeNull()
    })

    it('lays the main area out beside the panel while it is available, open or closed', async () => {
      const el = mount(() => h(TChatApp, { ...baseProps, activeThreadId: 't1', asideFullHeight: true }, withAside))
      await settle()
      expect(isFull(el)).toBe(true)
      ;(el.querySelector('.t-chat-app__aside-toggle') as HTMLElement).click()
      await settle()
      expect(isFull(el)).toBe(true)
      // The panel is not moved in the DOM, so the layout never remounts its content.
      expect(el.querySelector('.t-chat-app__workspace > .t-aside-panel.t-chat-app__aside .probe')).not.toBeNull()
      expect(el.querySelector('.t-chat-app__topbar .t-chat-app__aside-toggle')).not.toBeNull()
    })

    it('leaves the narrow layer alone', async () => {
      const el = mount(() =>
        h(TChatApp, { ...baseProps, activeThreadId: 't1', isMobile: true, asideDefaultOpen: true, asideFullHeight: true }, withAside),
      )
      await settle()
      expect(isFull(el)).toBe(false)
      expect(el.querySelector('.t-aside-panel--narrow')).not.toBeNull()
    })

    it('does not apply without aside content', async () => {
      const el = mount(() => h(TChatApp, { ...baseProps, activeThreadId: 't1', asideFullHeight: true }))
      await settle()
      expect(isFull(el)).toBe(false)
    })

    it('does not apply on a product view or the landing page', async () => {
      const view = ref('chat')
      const msgs = ref<typeof messages>(messages)
      const el = mount(() =>
        h(
          TChatApp,
          { ...baseProps, messages: msgs.value, view: view.value, activeThreadId: 't1', asideFullHeight: true },
          { ...withAside, view: () => h('div', { class: 'page' }) },
        ),
      )
      await settle()
      expect(isFull(el)).toBe(true)
      view.value = 'agents'
      await settle()
      expect(el.querySelector('.page')).not.toBeNull()
      expect(isFull(el)).toBe(false)
      view.value = 'chat'
      msgs.value = []
      await settle()
      expect(el.querySelector('.t-landing')).not.toBeNull()
      expect(el.querySelector('.t-chat-app__thread')).toBeNull()
      expect(isFull(el)).toBe(false)
    })
  })

  describe('asideOnLanding', () => {
    const landingProps = { ...baseProps, messages: [] as typeof messages }
    type Shell = { openAside: (tab?: string) => void }

    it('is off by default: no panel and no toggle on the landing page, openAside() shows nothing', async () => {
      const shell = ref<Shell | null>(null)
      const el = mount(() => h(TChatApp, { ...landingProps, ref: shell }, { aside: () => h('div', { class: 'probe' }) }))
      await settle()
      expect(el.querySelector('.t-landing')).not.toBeNull()
      expect(el.querySelector('.t-chat-app__aside-toggle')).toBeNull()
      shell.value!.openAside()
      await settle()
      expect(el.querySelector('.t-aside-panel')).toBeNull()
    })

    it('shows the panel beside the landing page, from openAside() and from the top bar toggle', async () => {
      const shell = ref<Shell | null>(null)
      const el = mount(() =>
        h(TChatApp, { ...landingProps, asideOnLanding: true, ref: shell }, { aside: (p: { tab?: string }) => h('div', { class: 'probe' }, p.tab) }),
      )
      await settle()
      const toggle = el.querySelector('.t-chat-app__aside-toggle') as HTMLButtonElement
      expect(toggle).not.toBeNull()
      expect(el.querySelector('.t-aside-panel')).toBeNull()

      shell.value!.openAside('files')
      await settle()
      // Same frame as in a conversation: a resizable column beside the landing content, in the workspace
      expect(el.querySelector('.t-chat-app__workspace > .t-landing + .t-aside-panel.t-chat-app__aside .probe')?.textContent).toBe('files')
      expect(el.querySelector('.t-aside-panel [role="separator"]')).not.toBeNull()
      expect(toggle.getAttribute('aria-expanded')).toBe('true')

      toggle.click()
      await settle()
      expect(el.querySelector('.t-aside-panel')).toBeNull()
    })

    it('keeps the panel open and mounted when the first message turns the landing page into a conversation', async () => {
      const msgs = ref<typeof messages>([])
      const thread = ref<string | null>(null)
      let slotMounts = 0
      const Probe = { setup: () => { slotMounts += 1; return () => h('div', { class: 'probe' }) } }
      const shell = ref<Shell | null>(null)
      const el = mount(() =>
        h(
          TChatApp,
          { ...baseProps, messages: msgs.value, activeThreadId: thread.value, asideOnLanding: true, ref: shell },
          { aside: () => h(Probe) },
        ),
      )
      await settle()
      shell.value!.openAside()
      await settle()
      const panel = el.querySelector('.t-aside-panel')
      const probe = el.querySelector('.probe')
      expect(panel).not.toBeNull()

      // The first message is shown before the conversation has an id, then the id arrives
      msgs.value = messages
      await settle()
      expect(el.querySelector('.t-landing')).toBeNull()
      expect(el.querySelector('.t-chat-app__thread')).not.toBeNull()
      expect(el.querySelector('.t-aside-panel')).toBe(panel)
      thread.value = 't-new'
      await settle()

      expect(el.querySelector('.t-aside-panel')).toBe(panel)
      expect(el.querySelector('.probe')).toBe(probe)
      expect(slotMounts).toBe(1)
      // The new conversation took over the landing page's state, so it is remembered as open
      expect(JSON.parse(localStorage.getItem('aside-test')!)['t-new']).toMatchObject({ open: true })
    })

    it('stores the width the landing page was resized to with the conversation it becomes', async () => {
      const msgs = ref<typeof messages>([])
      const thread = ref<string | null>(null)
      const el = mount(() =>
        h(
          TChatApp,
          { ...baseProps, messages: msgs.value, activeThreadId: thread.value, asideOnLanding: true, asideDefaultOpen: true },
          { aside: () => h('div', { class: 'probe' }) },
        ),
      )
      await settle()
      const handle = el.querySelector('.t-aside-panel [role="separator"]') as HTMLElement
      handle.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft', shiftKey: true, bubbles: true }))
      await settle()
      msgs.value = messages
      thread.value = 't-new'
      await settle()
      expect(JSON.parse(localStorage.getItem('aside-test')!)['t-new']).toMatchObject({ width: 420 + 64 })
    })

    it('lays the landing page out beside a full-height panel and is a full-size layer on a phone', async () => {
      const wide = mount(() =>
        h(TChatApp, { ...landingProps, asideOnLanding: true, asideFullHeight: true, asideDefaultOpen: true }, { aside: () => h('div', { class: 'probe' }) }),
      )
      await settle()
      expect(wide.querySelector('.t-chat-app__main')!.classList.contains('t-chat-app__main--aside-full-height')).toBe(true)
      expect(wide.querySelector('.t-landing.t-chat-app__landing')).not.toBeNull()
      app?.unmount()
      host?.remove()

      const phone = mount(() =>
        h(TChatApp, { ...landingProps, asideOnLanding: true, isMobile: true, asideDefaultOpen: true }, { aside: () => h('div', { class: 'probe' }) }),
      )
      await settle()
      expect(phone.querySelector('.t-aside-panel--narrow .probe')).not.toBeNull()
    })

    it('does not put the artifact pane on the landing page', async () => {
      const el = mount(() =>
        h(TChatApp, { ...landingProps, asideOnLanding: true, artifact: { id: 'a', title: 'A', type: 'code', content: 'x' } as never }),
      )
      await settle()
      expect(el.querySelector('.t-landing')).not.toBeNull()
      expect(el.querySelector('.t-chat-app__artifact-slot')).toBeNull()
    })
  })
})
