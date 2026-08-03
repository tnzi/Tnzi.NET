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
