// @vitest-environment node
/**
 * `useChatViewFill` in a real browser, on a phone-sized window: the built `TChatApp`
 * (`@tnzi/ui-ai/chat` with `style.css`, `fixtures/view-fill-page.ts`) shows a page that fills the
 * view area with a frame scrolling inside itself, and an ordinary short page. The filling page
 * gets the area's whole width (no scrollbar room) and the area cannot be scrolled; the ordinary
 * page keeps the room, so it does not move sideways when a long page comes and goes.
 *
 * Scrollbar room is layout, which no test DOM models, so this needs Chromium through Playwright,
 * and `pnpm build` first (it reads `dist/`). Where Playwright cannot be loaded (or has no
 * browser) the suite is skipped and says so.
 */
import { afterAll, beforeAll, describe, expect, it } from 'vitest'
import { createServer, type Server } from 'node:http'
import { existsSync, mkdtempSync, readFileSync, rmSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import type { AddressInfo } from 'node:net'

const pkgRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..')

type Playwright = typeof import('playwright')
let browser: Awaited<ReturnType<Playwright['chromium']['launch']>> | null = null
let skipReason = ''
if (!existsSync(resolve(pkgRoot, 'dist/chat.js')) || !existsSync(resolve(pkgRoot, 'dist/style.css'))) {
  skipReason = 'dist/ is not built (run `pnpm build` first)'
} else {
  try {
    const playwright = (await import(/* @vite-ignore */ 'playwright' as string)) as Playwright
    browser = await playwright.chromium.launch()
  } catch (error) {
    skipReason = `Playwright with Chromium is not available here (${(error as Error).message.split('\n')[0]})`
  }
}

const PAGE = `<!DOCTYPE html><html lang="en"><head>
<meta name="viewport" content="width=device-width, initial-scale=1">
<link rel="stylesheet" href="/style.css">
<style>html, body, #app { height: 100%; margin: 0 }</style>
</head><body><div id="app"></div><script type="module" src="/page.js"></script></body></html>`

let server: Server | null = null
let pageUrl = ''
let outDir = ''

describe.skipIf(!browser)(`useChatViewFill in TChatApp${skipReason ? ` (skipped: ${skipReason})` : ''}`, () => {
  beforeAll(async () => {
    outDir = mkdtempSync(join(tmpdir(), 'tnzi-view-fill-'))
    const { build } = await import('vite')
    await build({
      configFile: false,
      root: pkgRoot,
      logLevel: 'silent',
      define: { 'process.env.NODE_ENV': '"production"' },
      build: {
        outDir,
        emptyOutDir: true,
        minify: false,
        lib: { entry: resolve(pkgRoot, '__tests__/browser/fixtures/view-fill-page.ts'), formats: ['es'], fileName: () => 'page.js' },
      },
    })
    // The shell splits off chunks of its own (loaded on demand), so everything the build wrote is served.
    const fixed: Record<string, { body: string; type: string }> = {
      '/': { body: PAGE, type: 'text/html' },
      '/style.css': { body: readFileSync(resolve(pkgRoot, 'dist/style.css'), 'utf8'), type: 'text/css' },
    }
    server = createServer((req, res) => {
      const path = (req.url ?? '/').split('?')[0]!
      const built = /^\/[\w.-]+\.js$/.test(path) && existsSync(join(outDir, path)) ? join(outDir, path) : null
      const file = fixed[path] ?? (built ? { body: readFileSync(built, 'utf8'), type: 'text/javascript' } : null)
      if (!file) {
        res.writeHead(404).end()
        return
      }
      res.writeHead(200, { 'content-type': `${file.type}; charset=utf-8` })
      res.end(file.body)
    })
    pageUrl = await new Promise((done) => server!.listen(0, '127.0.0.1', () => done(`http://127.0.0.1:${(server!.address() as AddressInfo).port}/`)))
  }, 60_000)

  afterAll(async () => {
    await browser?.close()
    await new Promise((done) => (server ? server.close(done) : done(undefined)))
    if (outDir) rmSync(outDir, { recursive: true, force: true })
  })

  const area = (page: Awaited<ReturnType<NonNullable<typeof browser>['newPage']>>) =>
    page.evaluate(() => {
      const el = document.querySelector<HTMLElement>('section.t-chat-app__view')!
      const style = getComputedStyle(el)
      el.scrollTop = 200
      const scrolled = el.scrollHeight > el.clientHeight
      const result = {
        width: el.offsetWidth,
        clientWidth: el.clientWidth,
        overflowY: style.overflowY,
        gutter: style.scrollbarGutter,
        overflows: scrolled,
      }
      el.scrollTop = 0
      return result
    })

  it('a filling page has the whole width and no outer scroll; an ordinary page keeps the gutter', async () => {
    const page = await browser!.newPage({ viewport: { width: 412, height: 800 } })
    const errors: string[] = []
    page.on('pageerror', (error) => errors.push(error.message))
    await page.goto(pageUrl)
    await page.waitForSelector('.list-page')

    const ordinary = await area(page)
    expect(ordinary.overflowY).toBe('auto')
    expect(ordinary.gutter).toBe('stable')
    expect(ordinary.clientWidth).toBeLessThan(ordinary.width)

    await page.evaluate(() => (window as unknown as { shell: { show: (v: string) => void } }).shell.show('frame'))
    await page.waitForSelector('.frame-page')
    const filling = await area(page)
    expect(filling.overflowY).toBe('hidden')
    expect(filling.gutter).toBe('auto')
    expect(filling.clientWidth).toBe(filling.width)
    expect(filling.overflows).toBe(false)
    const frame = await page.evaluate(() => {
      const box = document.querySelector('.frame-page')!.getBoundingClientRect()
      const view = document.querySelector('section.t-chat-app__view')!.getBoundingClientRect()
      return { frameWidth: box.width, frameHeight: box.height, viewWidth: view.width, viewHeight: view.height }
    })
    expect(frame.frameWidth).toBe(frame.viewWidth)
    expect(frame.frameHeight).toBe(frame.viewHeight)

    await page.evaluate(() => (window as unknown as { shell: { show: (v: string) => void } }).shell.show('list'))
    await page.waitForSelector('.list-page')
    const back = await area(page)
    expect(back.gutter).toBe('stable')
    expect(back.clientWidth).toBe(ordinary.clientWidth)

    expect(errors).toEqual([])
    await page.close()
  }, 30_000)
})
