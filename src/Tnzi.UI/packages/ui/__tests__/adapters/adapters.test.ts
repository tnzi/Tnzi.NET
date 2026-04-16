import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { createMessageAdapter } from '../../src/adapters/message'
import { createNotificationAdapter } from '../../src/adapters/notification'
import { createLoadingBarAdapter } from '../../src/adapters/loading-bar'
import { createThemeAdapter } from '../../src/adapters/theme'
import { createDialogAdapter } from '../../src/adapters/dialog'
import { createUiAdapter } from '../../src/adapters/create-ui-adapter'
import { createRuntimeAdapter } from '../../src/adapters/create-runtime-adapter'

describe('adapters/message', () => {
  it('delegates to explicit API when provided', () => {
    const api = {
      success: vi.fn(),
      error: vi.fn(),
      warning: vi.fn(),
      info: vi.fn(),
      loading: vi.fn().mockReturnValue({ destroy: vi.fn() }),
    }
    const m = createMessageAdapter(api as any)
    m.success('ok', { duration: 100, closable: true })
    m.error('bad')
    m.warning('hm')
    m.info('fyi')
    expect(api.success).toHaveBeenCalledWith('ok', { duration: 100, closable: true })
    expect(api.error).toHaveBeenCalledWith('bad', undefined)
    expect(api.warning).toHaveBeenCalled()
    expect(api.info).toHaveBeenCalled()
  })

  it('loading returns destroyer that delegates to instance', () => {
    const destroy = vi.fn()
    const api = { success: vi.fn(), error: vi.fn(), warning: vi.fn(), info: vi.fn(), loading: vi.fn().mockReturnValue({ destroy }) }
    const m = createMessageAdapter(api as any)
    const stop = m.loading('wait')
    stop()
    expect(destroy).toHaveBeenCalled()
  })

  it('falls back to window.$message when no API passed', () => {
    const api = { success: vi.fn(), error: vi.fn(), warning: vi.fn(), info: vi.fn(), loading: vi.fn().mockReturnValue({ destroy: vi.fn() }) }
    ;(window as any).$message = api
    const m = createMessageAdapter()
    m.success('hi')
    expect(api.success).toHaveBeenCalledWith('hi', undefined)
    delete (window as any).$message
  })

  it('silently no-ops when no API available', () => {
    delete (window as any).$message
    const m = createMessageAdapter()
    expect(() => m.success('hi')).not.toThrow()
    const stop = m.loading('wait')
    expect(() => stop()).not.toThrow()
  })
})

describe('adapters/notification', () => {
  it('delegates to explicit API and maps options', () => {
    const api = { info: vi.fn(), success: vi.fn(), warning: vi.fn(), error: vi.fn(), destroyAll: vi.fn() }
    const n = createNotificationAdapter(api as any)
    n.info('c', { title: 'T', duration: 1000, closable: false, meta: 'meta' as any })
    expect(api.info).toHaveBeenCalledWith(expect.objectContaining({ title: 'T', content: 'c', duration: 1000, closable: false }))
    n.success('s'); n.warning('w'); n.error('e'); n.destroyAll()
    expect(api.success).toHaveBeenCalled()
    expect(api.warning).toHaveBeenCalled()
    expect(api.error).toHaveBeenCalled()
    expect(api.destroyAll).toHaveBeenCalled()
  })

  it('defaults closable=true when not specified', () => {
    const api = { info: vi.fn(), success: vi.fn(), warning: vi.fn(), error: vi.fn(), destroyAll: vi.fn() }
    const n = createNotificationAdapter(api as any)
    n.info('c')
    expect(api.info).toHaveBeenCalledWith(expect.objectContaining({ closable: true }))
  })

  it('falls back to window.$notification', () => {
    const api = { info: vi.fn(), success: vi.fn(), warning: vi.fn(), error: vi.fn(), destroyAll: vi.fn() }
    ;(window as any).$notification = api
    createNotificationAdapter().info('hi')
    expect(api.info).toHaveBeenCalled()
    delete (window as any).$notification
  })

  it('no-ops when API missing', () => {
    delete (window as any).$notification
    const n = createNotificationAdapter()
    expect(() => n.destroyAll()).not.toThrow()
  })
})

describe('adapters/loading-bar', () => {
  it('delegates start/finish/error', () => {
    const api = { start: vi.fn(), finish: vi.fn(), error: vi.fn() }
    const lb = createLoadingBarAdapter(api as any)
    lb.start(); lb.finish(); lb.error()
    expect(api.start).toHaveBeenCalled()
    expect(api.finish).toHaveBeenCalled()
    expect(api.error).toHaveBeenCalled()
  })

  it('falls back to window.$loadingBar', () => {
    const api = { start: vi.fn(), finish: vi.fn(), error: vi.fn() }
    ;(window as any).$loadingBar = api
    createLoadingBarAdapter().start()
    expect(api.start).toHaveBeenCalled()
    delete (window as any).$loadingBar
  })

  it('no-ops when nothing available', () => {
    delete (window as any).$loadingBar
    expect(() => createLoadingBarAdapter().start()).not.toThrow()
  })
})

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

describe('adapters/dialog', () => {
  function mockApi() {
    return {
      success: vi.fn(),
      error: vi.fn(),
      warning: vi.fn(),
      info: vi.fn(),
      create: vi.fn(),
    }
  }

  afterEach(() => {
    delete (window as any).$dialog
  })

  it('confirm resolves true on positive click', async () => {
    const api = mockApi()
    api.warning.mockImplementation((opts: any) => { opts.onPositiveClick() })
    const d = createDialogAdapter(api as any)
    expect(await d.confirm('sure?')).toBe(true)
  })

  it('confirm resolves false on negative/close', async () => {
    const api = mockApi()
    api.warning.mockImplementation((opts: any) => { opts.onNegativeClick() })
    expect(await createDialogAdapter(api as any).confirm('no')).toBe(false)
    api.warning.mockImplementation((opts: any) => { opts.onClose() })
    expect(await createDialogAdapter(api as any).confirm('x')).toBe(false)
  })

  it('confirm honors options.type and falls back to warning', async () => {
    const api = mockApi()
    api.error.mockImplementation((opts: any) => { opts.onPositiveClick() })
    const d = createDialogAdapter(api as any)
    await d.confirm('err', { type: 'error' } as any)
    expect(api.error).toHaveBeenCalled()
  })

  it('confirm falls back to window.confirm when no api', async () => {
    const spy = vi.spyOn(window, 'confirm').mockReturnValue(true)
    const d = createDialogAdapter()
    expect(await d.confirm('fallback')).toBe(true)
    spy.mockRestore()
  })

  it('alert resolves on positive click', async () => {
    const api = mockApi()
    api.info.mockImplementation((opts: any) => { opts.onPositiveClick() })
    await expect(createDialogAdapter(api as any).alert('msg')).resolves.toBeUndefined()
    expect(api.info).toHaveBeenCalled()
  })

  it('alert onClose also resolves', async () => {
    const api = mockApi()
    api.info.mockImplementation((opts: any) => { opts.onClose() })
    await expect(createDialogAdapter(api as any).alert('msg')).resolves.toBeUndefined()
  })

  it('alert falls back to window.alert', async () => {
    const spy = vi.spyOn(window, 'alert').mockImplementation(() => {})
    await createDialogAdapter().alert('hi')
    expect(spy).toHaveBeenCalledWith('hi')
    spy.mockRestore()
  })

  it('prompt resolves input value on positive', async () => {
    const api = mockApi()
    api.create.mockImplementation((opts: any) => { opts.onPositiveClick() })
    const d = createDialogAdapter(api as any)
    const result = await d.prompt('name', { content: 'default' } as any)
    expect(result).toBe('default')
  })

  it('prompt resolves null on negative/close', async () => {
    const api = mockApi()
    api.create.mockImplementation((opts: any) => { opts.onNegativeClick() })
    expect(await createDialogAdapter(api as any).prompt('n')).toBeNull()
    api.create.mockImplementation((opts: any) => { opts.onClose() })
    expect(await createDialogAdapter(api as any).prompt('n')).toBeNull()
  })

  it('prompt falls back to window.prompt', async () => {
    const spy = vi.spyOn(window, 'prompt').mockReturnValue('typed')
    const d = createDialogAdapter()
    expect(await d.prompt('name')).toBe('typed')
    spy.mockRestore()
  })

  it('prompt fallback returns null when user cancels', async () => {
    const spy = vi.spyOn(window, 'prompt').mockReturnValue(null)
    expect(await createDialogAdapter().prompt('x')).toBeNull()
    spy.mockRestore()
  })
})

describe('adapters/create-ui-adapter', () => {
  it('creates a composite adapter with message/dialog/theme', () => {
    const ui = createUiAdapter()
    expect(ui.message).toBeDefined()
    expect(ui.dialog).toBeDefined()
    expect(ui.theme).toBeDefined()
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
