/**
 * The window's own controls over the top right corner (a frameless desktop
 * window): `--tnzi-ai-window-inset-right` / `-top`.
 *
 * - `TChatApp`'s top bar and `TGuestWelcome`'s bar keep clear of the corner;
 * - with a full-height panel open the corner is the panel's: the top bar loses
 *   the inset (`t-chat-app__topbar--corner-free`), the panel's content reads
 *   `--tnzi-ai-aside-inset-top` / `-right` and the `aside` slot is told
 *   `fullHeight`.
 *
 * The padding itself is pinned on the declared styles: happy-dom does no layout.
 */
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { createApp, h, nextTick, type App } from 'vue'
import { cssRule } from '../support/css-rule'

const { default: TChatApp } = await import('../../src/components/chat/TChatApp.vue')

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

const messages = [{ id: 'm1', role: 'user', content: 'hi' }]
const baseProps = { sidebarStorageKey: 'insets-sidebar', asideStorageKey: 'insets-aside', autoApplyTheme: false, messages, activeThreadId: 't1' }

describe('window corner insets', () => {
  it('the top bars keep clear of the window controls', () => {
    expect(cssRule('components/chat/TChatApp.vue', '.t-chat-app__topbar')).toMatch(
      /padding: 0 calc\(16px \+ var\(--tnzi-ai-window-inset-right, 0px\)\) 0 16px;/,
    )
    expect(cssRule('components/chat/TChatApp.vue', '.t-chat-app--narrow .t-chat-app__topbar')).toMatch(
      /calc\(8px \+ var\(--tnzi-ai-window-inset-right, 0px\)\)/,
    )
    expect(cssRule('auth/TGuestWelcome.vue', '.t-guest-welcome__bar')).toMatch(
      /padding: 12px calc\(20px \+ var\(--tnzi-ai-window-inset-right, 0px\)\) 12px 20px;/,
    )
  })

  it('hands the corner to a full-height panel: plain gutter on the bar, insets for the panel', () => {
    expect(cssRule('components/chat/TChatApp.vue', '.t-chat-app__topbar--corner-free')).toMatch(/padding-right: 16px;/)
    expect(cssRule('components/chat/TChatApp.vue', '.t-chat-app__aside')).toMatch(/--tnzi-ai-aside-inset-top: 0px;/)
    const full = cssRule('components/chat/TChatApp.vue', '.t-chat-app__main--aside-full-height .t-chat-app__aside')
    expect(full).toMatch(/--tnzi-ai-aside-inset-top: var\(--tnzi-ai-window-inset-top, 0px\);/)
    expect(full).toMatch(/--tnzi-ai-aside-inset-right: var\(--tnzi-ai-window-inset-right, 0px\);/)
  })

  it('marks the bar corner-free only while a full-height panel is open, and tells the slot', async () => {
    const seen: boolean[] = []
    const el = mount(() =>
      h(TChatApp, { ...baseProps, asideFullHeight: true }, {
        aside: ({ fullHeight }: { fullHeight: boolean }) => {
          seen.push(fullHeight)
          return h('div', { class: 'probe' })
        },
      }),
    )
    await settle()
    const bar = () => el.querySelector('.t-chat-app__topbar')!
    expect(bar().classList.contains('t-chat-app__topbar--corner-free')).toBe(false)
    ;(el.querySelector('.t-chat-app__aside-toggle') as HTMLElement).click()
    await settle()
    expect(el.querySelector('.probe')).not.toBeNull()
    expect(bar().classList.contains('t-chat-app__topbar--corner-free')).toBe(true)
    expect(seen.at(-1)).toBe(true)
  })

  it('keeps the inset on the bar beside a panel that is not full height', async () => {
    const seen: boolean[] = []
    const el = mount(() =>
      h(TChatApp, { ...baseProps, asideDefaultOpen: true }, {
        aside: ({ fullHeight }: { fullHeight: boolean }) => {
          seen.push(fullHeight)
          return h('div', { class: 'probe' })
        },
      }),
    )
    await settle()
    expect(el.querySelector('.probe')).not.toBeNull()
    expect(el.querySelector('.t-chat-app__topbar')!.classList.contains('t-chat-app__topbar--corner-free')).toBe(false)
    expect(seen.at(-1)).toBe(false)
  })
})
