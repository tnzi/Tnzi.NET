/**
 * `useElementSize`: the size of whatever element a ref holds, following it
 * through a `ResizeObserver` (content box by default), switching observers
 * when the element changes and letting go when the scope ends.
 */
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { effectScope, nextTick, ref } from 'vue'
import { useElementSize } from '../../src/headless/useElementSize'

type Callback = (entries: Array<Partial<ResizeObserverEntry>>) => void

const observers: FakeObserver[] = []

class FakeObserver {
  readonly targets: Array<{ el: Element; box?: string }> = []
  disconnected = false
  constructor(readonly callback: Callback) {
    observers.push(this)
  }
  observe(el: Element, options?: { box?: string }): void {
    this.targets.push({ el, box: options?.box })
  }
  disconnect(): void {
    this.disconnected = true
  }
  unobserve(): void {}
  report(width: number, height: number): void {
    const size = [{ inlineSize: width, blockSize: height }]
    this.callback([{ contentBoxSize: size, borderBoxSize: size }])
  }
}

const original = globalThis.ResizeObserver

beforeEach(() => {
  observers.length = 0
  ;(globalThis as { ResizeObserver: unknown }).ResizeObserver = FakeObserver
})
afterEach(() => {
  ;(globalThis as { ResizeObserver: unknown }).ResizeObserver = original
  document.body.innerHTML = ''
})

describe('useElementSize', () => {
  it('reports the observed size, and 0 x 0 without an element', async () => {
    const target = ref<HTMLElement | null>(null)
    const scope = effectScope()
    const size = scope.run(() => useElementSize(target))!
    await nextTick()
    expect([size.width.value, size.height.value]).toEqual([0, 0])
    expect(observers).toHaveLength(0)

    const el = document.createElement('div')
    document.body.appendChild(el)
    target.value = el
    await nextTick()
    expect(observers).toHaveLength(1)
    expect(observers[0]!.targets[0]).toEqual({ el, box: 'content-box' })
    observers[0]!.report(640.5, 320)
    expect([size.width.value, size.height.value]).toEqual([640.5, 320])
    scope.stop()
  })

  it('follows a new element and drops the old observer; stops with its scope', async () => {
    const first = document.createElement('div')
    const second = document.createElement('div')
    const target = ref<HTMLElement | null>(first)
    const scope = effectScope()
    const size = scope.run(() => useElementSize(() => target.value, { box: 'border-box' }))!
    await nextTick()
    expect(observers[0]!.targets[0]).toEqual({ el: first, box: 'border-box' })
    target.value = second
    await nextTick()
    expect(observers[0]!.disconnected).toBe(true)
    expect(observers[1]!.targets[0]!.el).toBe(second)
    observers[1]!.report(300, 100)
    expect(size.width.value).toBe(300)
    target.value = null
    await nextTick()
    expect(observers[1]!.disconnected).toBe(true)
    expect([size.width.value, size.height.value]).toEqual([0, 0])
    target.value = first
    await nextTick()
    scope.stop()
    expect(observers[2]!.disconnected).toBe(true)
  })
})
