/**
 * The bridge inside a widget's document, run in the test DOM: forms fire `submit` for the widget's
 * own handlers and never navigate (the bridge cancels what they left alone; `form.submit()` does
 * nothing), and the language of the host context becomes the document's `lang`, at the start and
 * on `host-context-changed`. What the browser's sandbox and policy add is
 * `__tests__/browser/widgetForms.browser.test.ts`.
 */
import { afterAll, beforeAll, describe, expect, it, vi } from 'vitest'
import { widgetBridgeScript, widgetHostContext } from '../../src/components/widget/widget-document'

const originalSubmit = HTMLFormElement.prototype.submit

beforeAll(() => {
  // In the test DOM the document is the top window, so the bridge's host is this window itself.
  vi.spyOn(window, 'postMessage').mockImplementation(() => {})
  const script = widgetBridgeScript(widgetHostContext({ scheme: 'light', variables: {} }, 'inline', 'en-US'))
  new Function(script.replace(/^<script>/, '').replace(/<\/script>$/, ''))()
})

afterAll(() => {
  HTMLFormElement.prototype.submit = originalSubmit
  vi.restoreAllMocks()
})

function form(): HTMLFormElement {
  const element = document.createElement('form')
  element.action = 'https://example.com/collect'
  element.innerHTML = '<input name="q" value="x"><button>Go</button>'
  document.body.appendChild(element)
  return element
}

describe('the widget bridge', () => {
  it("lets the widget's submit handler run, then cancels the submission", () => {
    const element = form()
    const seen: boolean[] = []
    element.addEventListener('submit', (event) => seen.push(event.defaultPrevented))
    let cancelled: boolean | null = null
    element.addEventListener('submit', (event) => queueMicrotask(() => (cancelled = event.defaultPrevented)))
    element.requestSubmit()
    expect(seen).toEqual([false])
    return Promise.resolve().then(() => expect(cancelled).toBe(true))
  })

  it('cancels a submission the widget left alone', () => {
    const element = form()
    const event = new Event('submit', { bubbles: true, cancelable: true })
    element.dispatchEvent(event)
    expect(event.defaultPrevented).toBe(true)
  })

  it('makes form.submit() do nothing rather than navigate', () => {
    expect(HTMLFormElement.prototype.submit).not.toBe(originalSubmit)
    expect(() => form().submit()).not.toThrow()
  })

  it('writes the language of the host context as the document lang, and follows a change', () => {
    expect(document.documentElement.getAttribute('lang')).toBe('en-US')
    const event = new MessageEvent('message', {
      data: { jsonrpc: '2.0', method: 'ui/notifications/host-context-changed', params: { theme: 'dark', locale: 'zh-CN' } },
    })
    Object.defineProperty(event, 'source', { value: window.parent })
    window.dispatchEvent(event)
    expect(document.documentElement.getAttribute('lang')).toBe('zh-CN')
    expect((window as unknown as { widget: { hostContext: { locale: string } } }).widget.hostContext.locale).toBe('zh-CN')
  })
})
