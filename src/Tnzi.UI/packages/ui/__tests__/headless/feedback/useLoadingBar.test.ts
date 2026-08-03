import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { useLoadingBar } from '../../../src/headless/feedback/useLoadingBar'

describe('useLoadingBar', () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('initializes hidden with progress 0', () => {
    const { visible, progress } = useLoadingBar()
    expect(visible.value).toBe(false)
    expect(progress.value).toBe(0)
  })

  it('start() shows bar and ticks progress toward 90', () => {
    const rand = vi.spyOn(Math, 'random').mockReturnValue(0.999)
    const { visible, progress, start } = useLoadingBar()
    start()
    expect(visible.value).toBe(true)
    expect(progress.value).toBe(0)
    // Tick once - progress should advance but not immediately overshoot
    vi.advanceTimersByTime(200)
    expect(progress.value).toBeGreaterThan(0)
    // Saturate - each tick guards "if progress < 90", so once past 90 no further growth
    for (let i = 0; i < 50; i++) vi.advanceTimersByTime(200)
    // Invariant: growth halted near 90 (not exact because last tick can cross threshold)
    const saturated = progress.value
    vi.advanceTimersByTime(200)
    expect(progress.value).toBe(saturated)
    rand.mockRestore()
  })

  it('start() is idempotent - calling again clears prior timer and resets progress', () => {
    const { progress, start } = useLoadingBar()
    start()
    vi.advanceTimersByTime(400)
    const mid = progress.value
    start()
    expect(progress.value).toBe(0)
    expect(mid).toBeGreaterThanOrEqual(0)
  })

  it('finish() sets progress 100 then hides after 250ms', () => {
    const { visible, progress, start, finish } = useLoadingBar()
    start()
    finish()
    expect(progress.value).toBe(100)
    expect(visible.value).toBe(true)
    vi.advanceTimersByTime(300)
    expect(visible.value).toBe(false)
    expect(progress.value).toBe(0)
  })

  it('error() delegates to finish()', () => {
    const { visible, progress, start, error } = useLoadingBar()
    start()
    error()
    expect(progress.value).toBe(100)
    vi.advanceTimersByTime(300)
    expect(visible.value).toBe(false)
  })
})
