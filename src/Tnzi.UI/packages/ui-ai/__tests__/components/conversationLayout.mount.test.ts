/**
 * The conversation's default layout, mounted for real where there is behaviour
 * and pinned on the declared styles where there is only layout (happy-dom
 * computes none):
 *
 * - an answer the agent went on with by itself (two answers, no user message
 *   between) gets a wavy line with words before it, in the locale's words;
 * - a turn that only compacted the conversation has no actions;
 * - the composer's toolbar: 10px under the text, the left cluster gives way,
 *   the right one and the icon buttons do not, 16px icons;
 * - the greeting is as wide as the composer and a size container;
 * - beside a panel the conversation keeps a readable width; beside the landing
 *   page a full-height panel is capped at 30%;
 * - the title takes its text's width up to a cap.
 */
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { createApp, h, nextTick, type App } from 'vue'
import type { ChatMessage } from '../../src/headless/useChat'
import { cssRule } from '../support/css-rule'

const { default: TChatApp } = await import('../../src/components/chat/TChatApp.vue')
const { default: TThreadMessage } = await import('../../src/components/chat/TThreadMessage.vue')
const { default: TLandingPage } = await import('../../src/components/chat/TLandingPage.vue')

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

const shell = { sidebarStorageKey: 'layout-sidebar', autoApplyTheme: false, activeThreadId: 't1' }
const msg = (id: string, role: ChatMessage['role'], content: string, extra: Partial<ChatMessage> = {}): ChatMessage =>
  ({ id, role, content, ...extra }) as ChatMessage

describe('an answer the agent went on with by itself', () => {
  it('gets a wavy line with words before it, and only that answer', async () => {
    const el = mount(() =>
      h(TChatApp, {
        ...shell,
        messages: [
          msg('u1', 'user', 'run the tests'),
          msg('a1', 'assistant', 'Started them in the background.', { status: 'done' }),
          msg('a2', 'assistant', 'They passed.', { status: 'done' }),
          msg('u2', 'user', 'thanks'),
          msg('a3', 'assistant', 'Any time.', { status: 'done' }),
        ],
      }),
    )
    await settle()
    const marks = [...el.querySelectorAll('.t-chat-app__continued')]
    expect(marks).toHaveLength(1)
    expect(marks[0]!.classList.contains('t-wave-divider')).toBe(true)
    expect(marks[0]!.closest('.t-chat-app__message')!.getAttribute('data-message-id')).toBe('a2')
    expect(marks[0]!.textContent?.trim()).toBe('Continued after a background task')
  })

  it('has no mark while the second row waits or has nothing yet', async () => {
    const el = mount(() =>
      h(TChatApp, {
        ...shell,
        messages: [
          msg('u1', 'user', 'go'),
          msg('a1', 'assistant', 'Done for now.', { status: 'done' }),
          msg('a2', 'assistant', '', { status: 'waiting', backgroundTasks: [{ id: 'b1', description: 'tests' }] }),
        ],
      }),
    )
    await settle()
    expect(el.querySelector('.t-chat-app__continued')).toBeNull()
  })

  it('keeps 14px of air on each side', () => {
    expect(cssRule('components/chat/TChatApp.vue', '.t-chat-app__continued')).toMatch(/margin: 14px 0;/)
  })
})

describe('a turn that only compacted the conversation', () => {
  it('shows its quiet line and no actions', async () => {
    const el = mount(() => h(TThreadMessage, { message: msg('a1', 'assistant', '', { status: 'compacted' }) }))
    await settle()
    expect(el.querySelector('.t-thread-message__stopped[role="status"]')).not.toBeNull()
    expect(el.querySelector('.t-thread-message__actions')).toBeNull()
  })

  it('a finished answer keeps its actions', async () => {
    const el = mount(() => h(TThreadMessage, { message: msg('a1', 'assistant', 'hi', { status: 'done' }) }))
    await settle()
    expect(el.querySelector('.t-thread-message__actions')).not.toBeNull()
  })
})

describe('composer toolbar', () => {
  const file = 'components/chat/TThreadComposer.vue'
  it('sits 10px under the text and gives way on the left', () => {
    expect(cssRule(file, '.t-thread-composer__bar')).toMatch(/padding: 10px 6px 0;/)
    expect(cssRule(file, '.t-thread-composer__left')).toMatch(/flex: 0 1 auto;\s*min-width: 0;/)
    expect(cssRule(file, '.t-thread-composer__right')).toMatch(/flex: none;/)
    expect(cssRule(file, '.t-thread-composer__act')).toMatch(/flex: none;/)
    expect(cssRule(file, '.t-thread-composer__act')).toMatch(/font-size: 16px;/)
    // A phone keeps its tighter bar.
    expect(cssRule(file, '.t-thread-composer-wrap.is-compact .t-thread-composer__bar')).toMatch(/padding: 2px 4px 0;/)
  })
})

describe('landing greeting', () => {
  it('is as wide as the composer, and a size container', async () => {
    expect(cssRule('components/chat/TLandingPage.vue', '.t-landing__headline')).toMatch(/width: 100%;\s*container-type: inline-size;/)
    const el = mount(() => h(TLandingPage, { modelValue: '', maxWidth: 640 }))
    await settle()
    expect((el.querySelector('.t-landing__headline') as HTMLElement).style.maxWidth).toBe('640px')
  })
})

describe('panel and conversation widths', () => {
  const file = 'components/chat/TChatApp.vue'
  it('keeps the conversation readable beside the panel, which gives way first', () => {
    expect(cssRule(file, '.t-chat-app__main--aside-full-height')).toMatch(
      /minmax\(min\(480px, 60%\), 1fr\)\s*auto\s*minmax\(0, min\(var\(--t-chat-app-aside-width, 0px\), var\(--tnzi-ai-aside-max-share, 70%\)\)\);/,
    )
    expect(cssRule(file, '.t-chat-app__main--aside-full-height.t-chat-app__main--landing')).toMatch(
      /var\(--tnzi-ai-aside-landing-max-share, 30%\)/,
    )
    expect(cssRule(file, '.t-chat-app__workspace--has-aside .t-chat-app__aside')).toMatch(/flex-shrink: 1;/)
  })

  it('marks the main area while the landing page is up', async () => {
    const el = mount(() => h(TChatApp, { ...shell, activeThreadId: null, messages: [] }))
    await settle()
    expect(el.querySelector('.t-chat-app__main')!.classList.contains('t-chat-app__main--landing')).toBe(true)
  })

  it('caps the title instead of letting it fill the bar', () => {
    const title = cssRule(file, '.t-chat-app__topbar .t-chat-app__thread-title')
    expect(title).toMatch(/flex: 1000 1 0%;/)
    expect(title).toMatch(/max-width: min\(520px, 55%\);/)
    const plain = cssRule(file, '.t-chat-app__topbar .t-chat-app__workspace-switcher')
    expect(plain).toMatch(/flex: 0 1 auto;/)
    expect(plain).toMatch(/max-width: min\(520px, 55%\);/)
  })
})
