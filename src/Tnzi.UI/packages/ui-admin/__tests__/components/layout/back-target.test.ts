import { describe, it, expect, vi, beforeEach } from 'vitest'
import type { Router } from 'vue-router'
import { runBack, hasInAppHistory } from '../../../src/components/layout/back-target'

function fakeRouter(): Router {
  return { push: vi.fn(), back: vi.fn() } as unknown as Router
}

describe('runBack', () => {
  beforeEach(() => {
    // Fresh deep-load state: no in-app history entry to step back to.
    window.history.replaceState({}, '')
  })

  it('pushes a string target', () => {
    const r = fakeRouter()
    runBack('/admin/clients', r)
    expect(r.push).toHaveBeenCalledWith('/admin/clients')
    expect(r.back).not.toHaveBeenCalled()
  })

  it('`true` goes back through history', () => {
    const r = fakeRouter()
    runBack(true, r)
    expect(r.back).toHaveBeenCalled()
    expect(r.push).not.toHaveBeenCalled()
  })

  it('smart `{ fallback }` pushes the fallback on a fresh deep-load (no history)', () => {
    const r = fakeRouter()
    runBack({ fallback: '/admin/clients/1?section=files' }, r)
    expect(r.push).toHaveBeenCalledWith('/admin/clients/1?section=files')
    expect(r.back).not.toHaveBeenCalled()
  })

  it('smart `{ fallback }` prefers in-app history (keeps origin deep-link)', () => {
    window.history.replaceState({ back: '/admin/clients/1?section=files' }, '')
    expect(hasInAppHistory()).toBe(true)
    const r = fakeRouter()
    runBack({ fallback: '/x' }, r)
    expect(r.back).toHaveBeenCalled()
    expect(r.push).not.toHaveBeenCalled()
  })

  it('no-ops without a router', () => {
    expect(() => runBack('/x', undefined)).not.toThrow()
    expect(() => runBack({ fallback: '/x' }, undefined)).not.toThrow()
  })
})

describe('per-window back probe', () => {
  beforeEach(() => {
    window.history.replaceState({}, '')
  })

  it('asks the router own probe instead of browser history when it has one', () => {
    // Browser history says "yes, you can go back" - it is a single global stack
    // shared by every open desktop window, so it answers for all of them at once.
    window.history.replaceState({ back: '/somewhere' }, '')

    const windowRouter = {
      push: vi.fn(),
      back: vi.fn(),
      __tnziCanGoBack: () => false,
    } as unknown as Router

    expect(hasInAppHistory(windowRouter)).toBe(false)

    // So a smart back inside a fresh window takes the fallback rather than
    // stepping the whole application back.
    runBack({ fallback: '/admin/users' }, windowRouter)
    expect(windowRouter.push).toHaveBeenCalledWith('/admin/users')
    expect(windowRouter.back).not.toHaveBeenCalled()
  })

  it('steps back when the window own stack has somewhere to go', () => {
    const windowRouter = {
      push: vi.fn(),
      back: vi.fn(),
      __tnziCanGoBack: () => true,
    } as unknown as Router

    runBack({ fallback: '/admin/users' }, windowRouter)
    expect(windowRouter.back).toHaveBeenCalled()
    expect(windowRouter.push).not.toHaveBeenCalled()
  })

  it('falls back to browser history for a router without a probe', () => {
    window.history.replaceState({ back: '/somewhere' }, '')
    const plain = { push: vi.fn(), back: vi.fn() } as unknown as Router

    expect(hasInAppHistory(plain)).toBe(true)
    runBack({ fallback: '/admin/users' }, plain)
    expect(plain.back).toHaveBeenCalled()
  })
})
