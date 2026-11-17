/**
 * TSegmented's sliding thumb: the chosen option's fill is an element of its own,
 * placed under the chosen option (place and size), that slides to a new choice and
 * stands in place at once on the first render, on a resize, and under
 * `prefers-reduced-motion`.
 *
 * happy-dom lays nothing out, so each option's box is given here (by its
 * `data-value`), as is the group's; the transition itself is the stylesheet's,
 * pinned by its rules.
 */
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createApp, h, nextTick, ref, type App } from 'vue'

const { default: TSegmented } = await import('../../src/components/layout/TSegmented.vue')

type Box = { left: number; top: number; width: number; height: number }

const GROUP: Box = { left: 100, top: 40, width: 260, height: 30 }
let boxes: Record<string, Box> = {}
let reduceMotion = false
const observers: Array<{ callback: () => void; targets: Element[] }> = []

class FakeResizeObserver {
  private readonly entry: { callback: () => void; targets: Element[] }
  constructor(callback: () => void) {
    this.entry = { callback, targets: [] }
    observers.push(this.entry)
  }
  observe(target: Element): void {
    this.entry.targets.push(target)
  }
  unobserve(): void {}
  disconnect(): void {
    this.entry.targets = []
  }
}

const rectOf = (box: Box) =>
  ({ ...box, x: box.left, y: box.top, right: box.left + box.width, bottom: box.top + box.height, toJSON() {} }) as DOMRect

let app: App | null = null
let host: HTMLElement | null = null
const realRect = HTMLElement.prototype.getBoundingClientRect

function mount(render: () => unknown): HTMLElement {
  host = document.createElement('div')
  document.body.appendChild(host)
  app = createApp({ render })
  app.mount(host)
  return host
}

async function settle(): Promise<void> {
  for (let i = 0; i < 3; i += 1) {
    await Promise.resolve()
    await nextTick()
  }
}

const thumbOf = (root: Element) => root.querySelector<HTMLElement>('[data-segment-thumb]')!

const options = [
  { value: 'chat', label: 'Chat' },
  { value: 'code', label: 'Code' },
  { value: 'mail', label: 'Mail', disabled: true },
]

beforeEach(() => {
  boxes = {
    chat: { left: 102, top: 42, width: 52, height: 26 },
    code: { left: 156, top: 42, width: 60.5, height: 26 },
    mail: { left: 218.5, top: 42, width: 48, height: 26 },
  }
  reduceMotion = false
  observers.length = 0
  HTMLElement.prototype.getBoundingClientRect = function (this: HTMLElement) {
    if (this.classList.contains('t-segmented')) return rectOf(GROUP)
    const box = this.dataset.value ? boxes[this.dataset.value] : undefined
    return box ? rectOf(box) : realRect.call(this)
  }
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: reduceMotion && query.includes('prefers-reduced-motion: reduce'),
    media: query,
    addEventListener() {},
    removeEventListener() {},
  }))
  vi.stubGlobal('ResizeObserver', FakeResizeObserver)
})

afterEach(() => {
  app?.unmount()
  host?.remove()
  app = null
  host = null
  document.body.innerHTML = ''
  HTMLElement.prototype.getBoundingClientRect = realRect
  vi.unstubAllGlobals()
})

describe('TSegmented thumb', () => {
  it('stands under the chosen option on the first render, without sliding there', async () => {
    const root = mount(() => h(TSegmented, { modelValue: 'code', options, label: 'Mode' }))
    await settle()
    const thumb = thumbOf(root)

    expect(thumb.getAttribute('aria-hidden')).toBe('true')
    expect(thumb.style.transform).toBe('translate(56px, 2px)')
    expect(thumb.style.width).toBe('60.5px')
    expect(thumb.style.height).toBe('26px')
    expect(thumb.hasAttribute('data-sliding')).toBe(false)
  })

  it('slides to the option chosen next, by click and by arrow key', async () => {
    const value = ref('chat')
    const root = mount(() =>
      h(TSegmented, { modelValue: value.value, options, label: 'Mode', 'onUpdate:modelValue': (v: string) => (value.value = v) }),
    )
    await settle()
    const thumb = thumbOf(root)
    expect(thumb.style.transform).toBe('translate(2px, 2px)')

    root.querySelector<HTMLElement>('[data-value="code"]')!.click()
    await settle()
    expect(thumb.hasAttribute('data-sliding')).toBe(true)
    expect(thumb.style.transform).toBe('translate(56px, 2px)')
    expect(thumb.style.width).toBe('60.5px')

    // The disabled "mail" is skipped: the right arrow from "code" wraps to "chat".
    root.querySelector<HTMLElement>('[data-value="code"]')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }))
    await settle()
    expect(value.value).toBe('chat')
    expect(thumb.hasAttribute('data-sliding')).toBe(true)
    expect(thumb.style.transform).toBe('translate(2px, 2px)')
    expect(thumb.style.width).toBe('52px')
  })

  it('moves at once under prefers-reduced-motion', async () => {
    reduceMotion = true
    const value = ref('chat')
    const root = mount(() => h(TSegmented, { modelValue: value.value, options, label: 'Mode' }))
    await settle()

    value.value = 'code'
    await settle()
    const thumb = thumbOf(root)
    expect(thumb.style.transform).toBe('translate(56px, 2px)')
    expect(thumb.hasAttribute('data-sliding')).toBe(false)
  })

  it('follows its option when the option changes size, at once', async () => {
    const value = ref('chat')
    const root = mount(() => h(TSegmented, { modelValue: value.value, options, label: 'Mode' }))
    await settle()
    value.value = 'code'
    await settle()
    const thumb = thumbOf(root)
    expect(thumb.hasAttribute('data-sliding')).toBe(true)

    const observer = observers.at(-1)!
    expect(observer.targets).toContain(root.querySelector('.t-segmented'))
    expect(observer.targets).toContain(root.querySelector('[data-value="code"]'))

    boxes.code = { left: 156, top: 42, width: 80, height: 26 }
    observer.callback()
    expect(thumb.style.width).toBe('80px')
    expect(thumb.hasAttribute('data-sliding')).toBe(false)
  })

  it('follows a label change and watches the options it now has', async () => {
    const list = ref(options.slice(0, 2))
    const root = mount(() => h(TSegmented, { modelValue: 'code', options: list.value, label: 'Mode' }))
    await settle()

    boxes.code = { left: 156, top: 42, width: 120, height: 26 }
    list.value = [options[0]!, { value: 'code', label: 'Code and more' }, options[2]!]
    await settle()
    const thumb = thumbOf(root)
    expect(thumb.style.width).toBe('120px')
    expect(thumb.hasAttribute('data-sliding')).toBe(false)
    expect(observers.at(-1)!.targets).toContain(root.querySelector('[data-value="mail"]'))
  })

  it('is hidden when no option is chosen, and does not slide in from nowhere', async () => {
    const value = ref('none')
    const root = mount(() => h(TSegmented, { modelValue: value.value, options, label: 'Mode' }))
    await settle()
    const thumb = thumbOf(root)
    expect(thumb.style.display).toBe('none')

    value.value = 'chat'
    await settle()
    expect(thumb.style.display).toBe('')
    expect(thumb.style.transform).toBe('translate(2px, 2px)')
    expect(thumb.hasAttribute('data-sliding')).toBe(false)
  })

  it('takes the group scrolled sideways into account and lies among the options, so it scrolls with them', async () => {
    const root = mount(() => h(TSegmented, { modelValue: 'code', options, label: 'Mode' }))
    await settle()
    const group = root.querySelector<HTMLElement>('.t-segmented')!
    expect(group.firstElementChild).toBe(thumbOf(root))

    Object.defineProperty(group, 'scrollLeft', { configurable: true, value: 30 })
    observers.at(-1)!.callback()
    expect(thumbOf(root).style.transform).toBe('translate(86px, 2px)')
  })

  it('is dimmed with a disabled group, and leaves the options their meaning', async () => {
    const root = mount(() => h(TSegmented, { modelValue: 'chat', options, label: 'Mode', disabled: true }))
    await settle()
    expect(thumbOf(root).hasAttribute('data-disabled')).toBe(true)
    const radios = Array.from(root.querySelectorAll('[role="radio"]'))
    expect(radios.map((r) => r.getAttribute('aria-checked'))).toEqual(['true', 'false', 'false'])
    expect(radios.map((r) => r.getAttribute('tabindex'))).toEqual(['0', '-1', '-1'])
  })

  it('keeps the selected fill on the thumb only, transitions only while sliding, and not under reduced motion', () => {
    const sfc = readFileSync(resolve(__dirname, '../../src/components/layout/TSegmented.vue'), 'utf8')
    const style = (/<style[^>]*>([\s\S]*?)<\/style>/.exec(sfc)?.[1] ?? '').replace(/\/\*[\s\S]*?\*\//g, '')
    const rule = (selector: string) =>
      Array.from(style.matchAll(/([^{}]+)\{([^{}]*)\}/g)).find((m) => m[1]!.trim() === selector)?.[2] ?? ''

    expect(rule('.t-segmented__option.is-selected')).not.toMatch(/background|box-shadow/)
    expect(rule('.t-segmented__thumb')).not.toMatch(/transition/)
    expect(rule('.t-segmented__thumb[data-sliding]')).toMatch(/transition: transform .*width/s)
    expect(style).toMatch(/@media \(prefers-reduced-motion: reduce\) \{\s*\.t-segmented__option,\s*\.t-segmented__thumb\[data-sliding\] \{\s*transition: none;/)
    expect(style).toMatch(/:root\.dark \.t-segmented__thumb,\s*:root\[data-theme='dark'\] \.t-segmented__thumb \{\s*background: var\(--tnzi-ai-selected\);/)
  })
})
