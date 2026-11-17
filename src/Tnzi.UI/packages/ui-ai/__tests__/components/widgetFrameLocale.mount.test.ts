/**
 * The language a widget is handed: in the host context of `ui/initialize` from the start (the
 * `locale` prop, else the app's language from its catalogue, else `<html lang>`), and as
 * `ui/notifications/host-context-changed` when it changes, as the MCP Apps host context says.
 */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createApp, h, nextTick, ref, type App } from 'vue'
import TWidgetFrame from '../../src/components/widget/TWidgetFrame.vue'
import { createAiI18nPlugin } from '../../src/i18n/index'
import { en } from '../../src/locales/en'
import { zhCn } from '../../src/locales/zh-cn'

const RUNNER = 'https://widgets.example.com/ai/widget-runner'

let app: App | null = null
let host: HTMLElement | null = null
let savedObserver: typeof IntersectionObserver | undefined

beforeEach(() => {
  savedObserver = globalThis.IntersectionObserver
  ;(globalThis as { IntersectionObserver?: unknown }).IntersectionObserver = undefined
})

afterEach(() => {
  app?.unmount()
  host?.remove()
  app = null
  host = null
  ;(globalThis as { IntersectionObserver?: unknown }).IntersectionObserver = savedObserver
  document.documentElement.removeAttribute('lang')
})

interface Mounted {
  readonly sent: () => Array<Record<string, any>>
  readonly receive: (data: unknown) => void
}

async function mount(options: { plugin?: ReturnType<typeof createAiI18nPlugin>; locale?: () => string | undefined } = {}): Promise<Mounted> {
  host = document.createElement('div')
  document.body.appendChild(host)
  app = createApp({
    setup: () => () => h(TWidgetFrame, { runnerUrl: RUNNER, html: '<p>w</p>', title: 'W', locale: options.locale?.() }),
  })
  if (options.plugin) app.use(options.plugin)
  app.mount(host)
  await nextTick()
  // The test DOM loads no frame: the runner's window is a stand-in the host posts to.
  const frame = host.querySelector('iframe') as HTMLIFrameElement
  const postMessage = vi.fn()
  const target = { postMessage }
  Object.defineProperty(frame, 'contentWindow', { configurable: true, get: () => target })
  return {
    sent: () => postMessage.mock.calls.map((call) => call[0] as Record<string, any>),
    receive: (data) => {
      const event = new MessageEvent('message', { data })
      Object.defineProperty(event, 'source', { value: target })
      window.dispatchEvent(event)
    },
  }
}

async function initialize(m: Mounted): Promise<Record<string, any>> {
  m.receive({ jsonrpc: '2.0', id: 1, method: 'ui/initialize', params: {} })
  await vi.waitFor(() => expect(m.sent().some((message) => message.id === 1)).toBe(true))
  return m.sent().find((message) => message.id === 1)!.result.hostContext
}

const contextChanges = (m: Mounted) => m.sent().filter((message) => message.method === 'ui/notifications/host-context-changed')

describe('a widget frame', () => {
  it("hands the widget the app's language from the start, and every switch of it", async () => {
    const plugin = createAiI18nPlugin({ messages: { en, 'zh-CN': zhCn }, locale: 'en-US' })
    const m = await mount({ plugin })
    expect((await initialize(m)).locale).toBe('en-US')

    plugin.setLocale('zh-CN')
    await nextTick()
    const changes = contextChanges(m)
    expect(changes).toHaveLength(1)
    expect(changes[0]!.params.locale).toBe('zh-CN')
    expect(changes[0]!.params.theme).toBe('light')
  })

  it('takes the locale prop over the app language', async () => {
    const plugin = createAiI18nPlugin({ locale: 'en' })
    const locale = ref('fr-CA')
    const m = await mount({ plugin, locale: () => locale.value })
    expect((await initialize(m)).locale).toBe('fr-CA')
    locale.value = 'de'
    await nextTick()
    await nextTick()
    expect(contextChanges(m).at(-1)?.params.locale).toBe('de')
  })

  it('falls back to <html lang>, and follows a change of it', async () => {
    document.documentElement.lang = 'ja'
    const m = await mount()
    expect((await initialize(m)).locale).toBe('ja')
    document.documentElement.lang = 'ko'
    await vi.waitFor(() => expect(contextChanges(m).at(-1)?.params.locale).toBe('ko'))
  })

  it('sends no locale when nobody named one', async () => {
    const m = await mount()
    expect((await initialize(m)).locale).toBeUndefined()
  })
})
