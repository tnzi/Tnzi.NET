import { describe, it, expect, vi } from 'vitest'
import { openPopStateGate } from '../../src/headless/popstate-gate'
import { enableTouchPress } from '../../src/headless/touch-press'

describe('openPopStateGate', () => {
  it('adds one listener per target, ahead of later ones, and hands events to the handler set later', () => {
    const target = new EventTarget()
    const order: string[] = []
    const add = vi.spyOn(target, 'addEventListener')
    const gate = openPopStateGate(target)
    openPopStateGate(target)
    expect(add).toHaveBeenCalledTimes(1)
    target.addEventListener('popstate', () => order.push('router'))
    target.dispatchEvent(new Event('popstate'))
    expect(order).toEqual(['router'])
    gate.setHandler(() => order.push('gate'))
    target.dispatchEvent(new Event('popstate'))
    expect(order).toEqual(['router', 'gate', 'router'])
  })
})

describe('enableTouchPress', () => {
  it('listens for touchstart once, passively', () => {
    const add = vi.spyOn(document, 'addEventListener')
    enableTouchPress()
    enableTouchPress()
    const calls = add.mock.calls.filter(([type]) => type === 'touchstart')
    expect(calls).toHaveLength(1)
    expect(calls[0]![2]).toEqual({ passive: true })
    add.mockRestore()
  })
})
