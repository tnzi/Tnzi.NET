import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { createThemeAdapter } from '../../src/adapters/theme'
import { createRuntimeAdapter } from '../../src/adapters/create-runtime-adapter'

describe('adapters/theme', () => {
  beforeEach(() => {
    document.documentElement.classList.remove('dark')
  })

  it('applyTheme delegates to applyThemeToDOM', () => {
    const t = createThemeAdapter()
    t.applyTheme('dark')
    expect(document.documentElement.classList.contains('dark')).toBe(true)
  })

  it('getResolvedTheme reads dark class', () => {
    const t = createThemeAdapter()
    expect(t.getResolvedTheme()).toBe('light')
    document.documentElement.classList.add('dark')
    expect(t.getResolvedTheme()).toBe('dark')
  })

  it('onSystemThemeChange registers listener and returns unsubscribe', () => {
    const handlers: Array<(e: MediaQueryListEvent) => void> = []
    const mql = {
      addEventListener: vi.fn((_: string, h: any) => handlers.push(h)),
      removeEventListener: vi.fn(),
    }
    const originalMM = window.matchMedia
    window.matchMedia = vi.fn().mockReturnValue(mql) as any

    const t = createThemeAdapter()
    const cb = vi.fn()
    const unsub = t.onSystemThemeChange(cb)
    expect(mql.addEventListener).toHaveBeenCalled()
    handlers[0]!({ matches: true } as any)
    expect(cb).toHaveBeenCalledWith('dark')
    handlers[0]!({ matches: false } as any)
    expect(cb).toHaveBeenCalledWith('light')
    unsub()
    expect(mql.removeEventListener).toHaveBeenCalled()

    window.matchMedia = originalMM
  })
})

describe('adapters/create-runtime-adapter', () => {
  it('delegates router methods to provided router', () => {
    const push = vi.fn()
    const replace = vi.fn()
    const back = vi.fn()
    const router = { push, replace, back, currentRoute: { value: { fullPath: '/foo' } } } as any
    const r = createRuntimeAdapter({ router })
    r.router.push('/x')
    r.router.replace('/y')
    r.router.back()
    expect(r.router.getCurrentPath()).toBe('/foo')
    expect(push).toHaveBeenCalledWith('/x')
    expect(replace).toHaveBeenCalledWith('/y')
    expect(back).toHaveBeenCalled()
  })

  it('provides no-op router when none passed', () => {
    const r = createRuntimeAdapter()
    expect(() => r.router.push('/x')).not.toThrow()
    expect(() => r.router.replace('/y')).not.toThrow()
    expect(() => r.router.back()).not.toThrow()
    expect(r.router.getCurrentPath()).toBe('/')
  })

  it('attaches a storage adapter', () => {
    const r = createRuntimeAdapter({ storagePrefix: 'test_' })
    expect(r.storage).toBeDefined()
  })
})
