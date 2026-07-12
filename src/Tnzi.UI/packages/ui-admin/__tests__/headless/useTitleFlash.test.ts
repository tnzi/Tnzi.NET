import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { useTitleFlash } from '../../src/headless/useTitleFlash'

describe('useTitleFlash', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    document.title = 'Admin'
  })
  afterEach(() => {
    vi.useRealTimers()
  })

  it('shows the alt title immediately, alternates, and restores on stop', () => {
    const f = useTitleFlash()
    f.flash('(2) New message')
    expect(document.title).toBe('(2) New message') // immediate

    vi.advanceTimersByTime(1000)
    expect(document.title).toBe('Admin') // back to original

    vi.advanceTimersByTime(1000)
    expect(document.title).toBe('(2) New message')

    f.stop()
    expect(document.title).toBe('Admin')
  })

  it('updates the alt text without restarting when already flashing', () => {
    const f = useTitleFlash()
    f.flash('(1) New message')
    f.flash('(3) New message')

    vi.advanceTimersByTime(2000) // original, then updated alt
    expect(document.title).toBe('(3) New message')
    f.stop()
  })

  it('restores the title when the tab becomes visible again', () => {
    const f = useTitleFlash()
    f.flash('(1) New message')
    expect(document.title).toBe('(1) New message')

    // Simulate the tab regaining focus.
    window.dispatchEvent(new Event('focus'))
    expect(document.title).toBe('Admin')
  })

  it('stop() is a safe no-op when not flashing', () => {
    const f = useTitleFlash()
    expect(() => f.stop()).not.toThrow()
    expect(document.title).toBe('Admin')
  })
})
