// @vitest-environment node
/**
 * Forms in a widget, in a real browser: the runner page served with its own policy (read from
 * `WidgetRunnerEndpoint.cs`) on an origin apart from the host page, the widget's document composed
 * as `TWidgetFrame` composes it. A form fires `submit` (a click on its button and Enter in a field),
 * and nothing a form does takes the widget anywhere: not a submission the widget left alone, not
 * `form.submit()`, not a `target="_top"`.
 *
 * The sandbox and the policy are the browser's to enforce, which no test DOM models, so this needs
 * Chromium through Playwright. Playwright is not a dependency of the package: where it cannot be
 * loaded (or has no browser) the suite is skipped and says so.
 */
import { afterAll, beforeAll, describe, expect, it } from 'vitest'
import { createServer, type Server } from 'node:http'
import { readFileSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import type { AddressInfo } from 'node:net'
import { composeWidgetDocument, widgetHostContext } from '../../src/components/widget/widget-document'
import type { WidgetTheme } from '../../src/components/widget/widget-theme'

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../../../../../..')
const widgetsDir = resolve(repoRoot, 'src/Tnzi.AI/Infrastructure/Widgets')

/** The runner's policy as the server builds it: the string literals of `BuildContentSecurityPolicy`. */
function runnerPolicy(): string {
  const source = readFileSync(resolve(widgetsDir, 'WidgetRunnerEndpoint.cs'), 'utf8')
  const body = /return "sandbox[\s\S]*?;\s*\n\s*}/.exec(source)?.[0] ?? ''
  const literals = [...body.matchAll(/\$?"([^"]*)"/g)].map((m) => m[1]!)
  return literals.join('').replace('{frameAncestors}', '*')
}

const THEME: WidgetTheme = { scheme: 'light', variables: {} }

const WIDGET = `<!DOCTYPE html><html><head></head><body>
<form id="search" action="https://example.com/collect" method="get">
  <input id="q" name="q"><button id="go">Go</button>
</form>
<form id="plain" action="https://example.com/collect"><input id="p" name="p"><button id="plain-go">Go</button></form>
<form id="top" action="https://example.com/collect" target="_top"><button id="top-go">Go</button></form>
<script>
  window.submits = [];
  document.getElementById('search').addEventListener('submit', function (event) {
    event.preventDefault();
    window.submits.push('search:' + new FormData(event.target).get('q'));
  });
  document.getElementById('plain').addEventListener('submit', function () { window.submits.push('plain'); });
  document.getElementById('top').addEventListener('submit', function () { window.submits.push('top'); });
</script>
</body></html>`

const HOST_PAGE = (runner: string, html: string) => `<!DOCTYPE html><html><body>
<iframe id="w" src="${runner}" sandbox="allow-scripts allow-forms" style="width:600px;height:300px"></iframe>
<script>
  var frame = document.getElementById('w');
  window.navigations = 0;
  window.addEventListener('message', function (event) {
    if (event.source !== frame.contentWindow) return;
    var data = event.data;
    if (data && data.method === 'ui/notifications/sandbox-proxy-ready') {
      frame.contentWindow.postMessage({ jsonrpc: '2.0', method: 'ui/notifications/sandbox-resource-ready', params: { html: ${JSON.stringify(html).replace(/</g, '\\u003c')} } }, '*');
    }
    if (data && data.method === 'tnzi/widget-navigated') window.navigations += 1;
    if (data && data.id !== undefined && data.method) frame.contentWindow.postMessage({ jsonrpc: '2.0', id: data.id, result: {} }, '*');
  });
</script>
</body></html>`

type Playwright = typeof import('playwright')
let playwright: Playwright | null = null
let browser: Awaited<ReturnType<Playwright['chromium']['launch']>> | null = null
let skipReason = ''
try {
  playwright = (await import(/* @vite-ignore */ 'playwright' as string)) as Playwright
  browser = await playwright.chromium.launch()
} catch (error) {
  skipReason = `Playwright with Chromium is not available here (${(error as Error).message.split('\n')[0]})`
}

const servers: Server[] = []
let hostUrl = ''
const offsite: string[] = []

function listen(handler: Parameters<typeof createServer>[1]): Promise<string> {
  const server = createServer(handler)
  servers.push(server)
  return new Promise((done) => server.listen(0, '127.0.0.1', () => done(`http://127.0.0.1:${(server.address() as AddressInfo).port}`)))
}

describe.skipIf(!browser)(`widget forms in the sandbox${skipReason ? ` (skipped: ${skipReason})` : ''}`, () => {
  beforeAll(async () => {
    const runnerHtml = readFileSync(resolve(widgetsDir, 'widget-runner.html'), 'utf8')
    const policy = runnerPolicy()
    expect(policy).toContain('sandbox allow-scripts allow-forms;')
    expect(policy).toContain("form-action 'none'")
    const runner = await listen((_req, res) => {
      res.writeHead(200, { 'content-type': 'text/html; charset=utf-8', 'content-security-policy': policy })
      res.end(runnerHtml)
    })
    const document = composeWidgetDocument(WIDGET, THEME, widgetHostContext(THEME, 'inline', 'en'))
    hostUrl = await listen((_req, res) => {
      res.writeHead(200, { 'content-type': 'text/html; charset=utf-8' })
      res.end(HOST_PAGE(`${runner}/ai/widget-runner`, document))
    })
  })

  afterAll(async () => {
    await browser?.close()
    await Promise.all(servers.map((server) => new Promise((done) => server.close(done))))
  })

  async function open() {
    const page = await browser!.newPage()
    await page.route('https://example.com/**', (route) => {
      offsite.push(route.request().url())
      return route.abort()
    })
    await page.goto(hostUrl)
    let widget = page.frames().find((frame) => frame.url() === 'about:srcdoc')
    for (let i = 0; !widget && i < 50; i++) {
      await page.waitForTimeout(50)
      widget = page.frames().find((frame) => frame.url() === 'about:srcdoc')
    }
    expect(widget, `the widget frame among ${page.frames().map((f) => f.url()).join(', ')}`).toBeTruthy()
    await widget!.waitForFunction(() => Array.isArray((window as unknown as { submits?: unknown }).submits))
    return { page, widget: widget! }
  }

  const submits = (frame: { evaluate: (fn: () => unknown) => Promise<unknown> }) =>
    frame.evaluate(() => (window as unknown as { submits: string[] }).submits.slice())

  it('fires submit for a click on the button and for Enter in a field, and stays put', async () => {
    const { page, widget } = await open()
    await widget.fill('#q', 'first')
    await widget.click('#go')
    await widget.fill('#q', 'second')
    await widget.press('#q', 'Enter')
    await page.waitForTimeout(200)
    expect(await submits(widget)).toEqual(['search:first', 'search:second'])
    expect(await page.evaluate(() => (window as unknown as { navigations: number }).navigations)).toBe(0)
    await page.close()
  })

  it('takes a widget nowhere, whatever its form does', async () => {
    const { page, widget } = await open()
    await widget.fill('#p', 'secret')
    await widget.press('#p', 'Enter')
    await widget.click('#top-go')
    await widget.evaluate(() => (document.getElementById('plain') as HTMLFormElement).submit())
    await page.waitForTimeout(400)
    // The handlers ran (the bridge cancelled what they left alone), and the widget is still there.
    expect(await submits(widget)).toEqual(['plain', 'top'])
    expect(widget.isDetached()).toBe(false)
    expect(widget.url()).toBe('about:srcdoc')
    expect(page.url()).toBe(`${hostUrl}/`)
    expect(await page.evaluate(() => (window as unknown as { navigations: number }).navigations)).toBe(0)
    expect(offsite).toEqual([])
    await page.close()
  })
})
