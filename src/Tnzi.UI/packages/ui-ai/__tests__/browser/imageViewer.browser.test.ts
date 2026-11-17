// @vitest-environment node
/**
 * `TImageViewer` on a page of controls, in a real browser: the page loads the built
 * `@tnzi/ui-ai/dialogs` and `@tnzi/ui-ai/controls.css` and no other stylesheet of the package
 * (`fixtures/image-viewer-page.ts`, bundled here with Vite). The viewer must be drawn by that
 * sheet alone, in the light and the dark palette, and open, zoom (buttons, keys, wheel), pan
 * (drag) and close (Escape, its button).
 *
 * Layout and the stylesheet are the browser's, which no test DOM models, so this needs Chromium
 * through Playwright, and `pnpm build` first (it reads `dist/`). Playwright is not a dependency
 * of the package: where it cannot be loaded (or has no browser) the suite is skipped and says so.
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
if (!existsSync(resolve(pkgRoot, 'dist/dialogs.js')) || !existsSync(resolve(pkgRoot, 'dist/controls.css'))) {
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
<link rel="stylesheet" href="/controls.css">
</head><body><div id="app"></div><script type="module" src="/page.js"></script></body></html>`

let server: Server | null = null
let pageUrl = ''
let outDir = ''

describe.skipIf(!browser)(`TImageViewer on a page of controls${skipReason ? ` (skipped: ${skipReason})` : ''}`, () => {
  beforeAll(async () => {
    outDir = mkdtempSync(join(tmpdir(), 'tnzi-image-viewer-'))
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
        lib: { entry: resolve(pkgRoot, '__tests__/browser/fixtures/image-viewer-page.ts'), formats: ['es'], fileName: () => 'page.js' },
      },
    })
    const files: Record<string, { body: string; type: string }> = {
      '/': { body: PAGE, type: 'text/html' },
      '/page.js': { body: readFileSync(join(outDir, 'page.js'), 'utf8'), type: 'text/javascript' },
      '/controls.css': { body: readFileSync(resolve(pkgRoot, 'dist/controls.css'), 'utf8'), type: 'text/css' },
    }
    server = createServer((req, res) => {
      const file = files[(req.url ?? '/').split('?')[0]!]
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

  async function openViewer(dark = false) {
    const page = await browser!.newPage({ viewport: { width: 1000, height: 800 } })
    const errors: string[] = []
    page.on('pageerror', (error) => errors.push(error.message))
    await page.goto(pageUrl)
    await page.waitForFunction(() => 'viewer' in window)
    if (dark) await page.evaluate(() => document.documentElement.classList.add('dark'))
    await page.evaluate(() => (window as unknown as { viewer: { open: (i: number) => void } }).viewer.open(0))
    await page.waitForSelector('[data-image-viewer-image]')
    await page.waitForFunction(() => document.querySelector<HTMLImageElement>('[data-image-viewer-image]')!.complete)
    return { page, errors }
  }

  /** The picture's box once its transform transition has ended. */
  async function pictureBox(page: Awaited<ReturnType<typeof openViewer>>['page']) {
    await page.waitForFunction(() => document.getAnimations().length === 0)
    return page.evaluate(() => {
      const box = document.querySelector('[data-image-viewer-image]')!.getBoundingClientRect()
      return { x: box.x, y: box.y, width: box.width, height: box.height }
    })
  }

  const styles = (page: Awaited<ReturnType<typeof openViewer>>['page']) =>
    page.evaluate(() => {
      const viewer = document.querySelector<HTMLElement>('[data-image-viewer]')!
      const layer = getComputedStyle(viewer)
      const close = getComputedStyle(viewer.querySelector('[data-image-viewer-close]')!)
      const bar = getComputedStyle(viewer.querySelector('.t-image-viewer__bar')!)
      const image = getComputedStyle(viewer.querySelector('[data-image-viewer-image]')!)
      return {
        position: layer.position,
        inset: [layer.top, layer.right, layer.bottom, layer.left].join(' '),
        zIndex: Number(layer.zIndex),
        background: layer.backgroundColor,
        touchAction: layer.touchAction,
        closePosition: close.position,
        closeSize: `${close.width} x ${close.height}`,
        closeRadius: close.borderRadius,
        closeColor: close.color,
        closeBackground: close.backgroundColor,
        barBackground: bar.backgroundColor,
        barDisplay: bar.display,
        barPosition: bar.position,
        imageFit: image.objectFit,
        accentText: layer.getPropertyValue('--tnzi-ai-accent-text').trim(),
        focusOutline: layer.getPropertyValue('--tnzi-ai-focus-outline').trim(),
      }
    })

  /** WCAG contrast of white on a computed colour (`rgba(...)` or `color(srgb ...)`) laid over white. */
  const whiteContrastOverWhite = (color: string) => {
    const numbers = color.match(/[\d.]+/g)!.map(Number)
    const srgb = color.startsWith('color(')
    const [r, g, b] = numbers.slice(0, 3).map(n => (srgb ? n : n / 255))
    const alpha = numbers[3] ?? 1
    const channel = (c: number) => {
      const v = c * alpha + (1 - alpha)
      return v <= 0.04045 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4
    }
    const luminance = 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b)
    return 1.05 / (luminance + 0.05)
  }

  it('is drawn by controls.css alone, in the light palette and the dark one', async () => {
    const light = await openViewer()
    const lightStyles = await styles(light.page)
    expect(lightStyles).toMatchObject({
      position: 'fixed',
      inset: '0px 0px 0px 0px',
      background: 'rgba(0, 0, 0, 0.9)',
      touchAction: 'none',
      closePosition: 'absolute',
      closeSize: '40px x 40px',
      closeRadius: '50%',
      closeColor: 'rgb(255, 255, 255)',
      barDisplay: 'flex',
      barPosition: 'absolute',
      imageFit: 'contain',
    })
    expect(lightStyles.zIndex).toBeGreaterThan(1000)
    // White on the backdrop, the buttons and the bar reads even with a white page or a white
    // zoomed picture under them (4.5:1, the level's text included).
    for (const background of [lightStyles.background, lightStyles.closeBackground, lightStyles.barBackground])
      expect(whiteContrastOverWhite(background)).toBeGreaterThanOrEqual(4.5)
    expect(lightStyles.focusOutline).not.toBe('')
    // The picture is fitted inside the area between the top and the zoom bar.
    const box = await pictureBox(light.page)
    expect(box.width).toBeLessThanOrEqual(1000 - 32 + 0.5)
    expect(box.y).toBeGreaterThanOrEqual(48 - 0.5)
    expect(box.y + box.height).toBeLessThanOrEqual(800 - 64 + 0.5)
    expect(light.errors).toEqual([])
    await light.page.close()

    const dark = await openViewer(true)
    const darkStyles = await styles(dark.page)
    expect(darkStyles).toMatchObject({ position: 'fixed', background: 'rgba(0, 0, 0, 0.9)', closeSize: '40px x 40px', barDisplay: 'flex' })
    // The focus ring follows the dark palette's accent.
    expect(darkStyles.accentText).not.toBe('')
    expect(darkStyles.accentText).not.toBe(lightStyles.accentText)
    await dark.page.close()
  })

  it('zooms with its buttons, keys and the wheel, pans by dragging, and closes', async () => {
    const { page, errors } = await openViewer()
    const fitted = await pictureBox(page)

    await page.click('[data-image-viewer-zoom-in]')
    const zoomed = await pictureBox(page)
    expect(zoomed.width / fitted.width).toBeCloseTo(1.5, 2)
    expect(await page.textContent('[data-image-viewer-fit]')).toContain('150%')

    await page.keyboard.press('+')
    expect((await pictureBox(page)).width / fitted.width).toBeCloseTo(2.25, 2)

    // A drag moves the zoomed picture by the pointer's travel.
    const before = await pictureBox(page)
    await page.mouse.move(500, 380)
    await page.mouse.down()
    await page.mouse.move(440, 340, { steps: 4 })
    await page.mouse.up()
    const dragged = await pictureBox(page)
    expect(dragged.x - before.x).toBeCloseTo(-60, 0)
    expect(dragged.y - before.y).toBeCloseTo(-40, 0)
    expect(await page.evaluate(() => (window as unknown as { viewer: { index: () => number | null } }).viewer.index())).toBe(0)

    await page.keyboard.press('0')
    expect(await pictureBox(page)).toEqual(fitted)

    await page.mouse.move(700, 400)
    await page.mouse.wheel(0, -300)
    const wheeled = await pictureBox(page)
    expect(wheeled.width).toBeGreaterThan(fitted.width * 1.5)

    await page.keyboard.press('Escape')
    await page.waitForSelector('[data-image-viewer]', { state: 'detached' })
    expect(await page.evaluate(() => (window as unknown as { viewer: { index: () => number | null } }).viewer.index())).toBeNull()

    await page.evaluate(() => (window as unknown as { viewer: { open: (i: number) => void } }).viewer.open(1))
    await page.click('[data-image-viewer-close]')
    await page.waitForSelector('[data-image-viewer]', { state: 'detached' })
    expect(errors).toEqual([])
    await page.close()
  })
})
