import { describe, it, expect, vi, beforeEach } from 'vitest'
// jsdom has no real AudioContext — stub it
const startSpy = vi.fn(), connectSpy = vi.fn(), resumeSpy = vi.fn(async () => {})
beforeEach(() => {
  startSpy.mockClear()
  ;(globalThis as never as { AudioContext: unknown }).AudioContext = class {
    state = 'running'; currentTime = 0
    createOscillator() { return { connect: connectSpy, frequency: { setValueAtTime: vi.fn() }, start: startSpy, stop: vi.fn() } }
    createGain() { return { connect: connectSpy, gain: { setValueAtTime: vi.fn(), exponentialRampToValueAtTime: vi.fn() } } }
    get destination() { return {} }
    resume() { return resumeSpy() }
  }
})
import { useNotificationSound } from '../../src/headless/useNotificationSound'
describe('useNotificationSound', () => {
  it('play() triggers an oscillator when enabled', () => {
    const s = useNotificationSound(); s.play()
    expect(startSpy).toHaveBeenCalled()
  })
  it('play() does nothing when disabled', () => {
    const s = useNotificationSound(); s.setEnabled(false); s.play()
    expect(startSpy).not.toHaveBeenCalled()
  })
  it('play() swallows errors (no throw) if AudioContext missing', () => {
    ;(globalThis as never as { AudioContext: unknown }).AudioContext = undefined
    const s = useNotificationSound()
    expect(() => s.play()).not.toThrow()
  })
})
