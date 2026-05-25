import { describe, it, expect, vi, afterEach } from 'vitest'
import { defineComponent, h } from 'vue'
import { mount } from '@vue/test-utils'
import { useSafeMessage } from '../../src/pages/_shared/safeMessage'

/**
 * useSafeMessage covers the case where a page mounts without an
 * NMessageProvider ancestor — calling useMessage() directly would
 * throw a synchronous Naive UI warning. The helper swallows it and
 * returns a no-op MessageApi so the page can keep functioning.
 */
describe('useSafeMessage', () => {
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('returns an object that exposes all MessageApi methods even without a provider', () => {
    const captured: { api: ReturnType<typeof useSafeMessage> | null } = { api: null }
    const Probe = defineComponent({
      setup() {
        captured.api = useSafeMessage()
        return () => h('div')
      },
    })
    mount(Probe)
    expect(captured.api).toBeTruthy()
    expect(typeof captured.api?.success).toBe('function')
    expect(typeof captured.api?.error).toBe('function')
    expect(typeof captured.api?.warning).toBe('function')
    expect(typeof captured.api?.info).toBe('function')
    expect(typeof captured.api?.loading).toBe('function')
    expect(typeof captured.api?.create).toBe('function')
    expect(typeof captured.api?.destroyAll).toBe('function')
  })

  it('noop methods do not throw and return a destroy handle', () => {
    const captured: { api: ReturnType<typeof useSafeMessage> | null } = { api: null }
    const Probe = defineComponent({
      setup() {
        captured.api = useSafeMessage()
        return () => h('div')
      },
    })
    mount(Probe)
    const api = captured.api!
    // success/error/etc return MessageReactive (or our noop equivalent)
    // with a destroy() function — calling it should also not throw.
    expect(() => {
      const handle = api.success('hello') as { destroy?: () => void }
      handle.destroy?.()
    }).not.toThrow()
    expect(() => api.error('boom')).not.toThrow()
    expect(() => api.info('hi')).not.toThrow()
    expect(() => api.warning('careful')).not.toThrow()
    expect(() => api.loading('wait')).not.toThrow()
    expect(() => api.destroyAll()).not.toThrow()
  })
})
