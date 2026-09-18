import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { nextTick } from 'vue'

/**
 * `TCaptcha` renders whichever provider the deployment runs. Two things matter:
 * the `image` provider composes `{captchaId}:{code}` and (re)loads through the
 * caller's loader; script providers hand the widget's token through unchanged
 * and `execute()` / `reset()` reach the widget. naive-ui is stubbed so the
 * input can be driven by name; the Turnstile global is faked the same way the
 * core driver tests do.
 */
vi.mock('naive-ui', () => ({
  NInput: {
    name: 'NInput',
    props: ['value', 'disabled', 'placeholder'],
    emits: ['update:value'],
    template: '<input class="ninput" />',
  },
  NSpin: { name: 'NSpin', template: '<span class="nspin" />' },
  NText: { name: 'NText', props: ['type', 'depth'], template: '<span class="ntext"><slot /></span>' },
}))

import TCaptcha from '../../../src/components/form/TCaptcha.vue'

type Rendered = { container: HTMLElement; params: Record<string, unknown> }

function fakeTurnstile() {
  const rendered: Rendered[] = []
  const api = {
    render: vi.fn((container: HTMLElement, params: Record<string, unknown>) => {
      rendered.push({ container, params })
      return 'w1'
    }),
    reset: vi.fn(),
    remove: vi.fn(),
    getResponse: vi.fn(() => ''),
  }
  ;(globalThis as unknown as Record<string, unknown>).turnstile = api
  return { api, rendered }
}

beforeEach(() => {
  delete (globalThis as unknown as Record<string, unknown>).turnstile
})

afterEach(() => {
  vi.restoreAllMocks()
})

describe('TCaptcha - image provider', () => {
  const challenge = { provider: 'image', captchaId: 'cid-1', imageBase64: 'AAAA', expirationSeconds: 300 }

  it('loads a picture on mount through loadImage and composes id:code as the token', async () => {
    const loadImage = vi.fn().mockResolvedValue(challenge)
    const w = mount(TCaptcha, { props: { purpose: 'login', loadImage, token: '' } })
    await flushPromises()

    expect(loadImage).toHaveBeenCalledWith('login')
    expect(w.find('img').attributes('src')).toBe('data:image/png;base64,AAAA')
    expect(w.attributes('data-provider')).toBe('image')

    await w.findComponent({ name: 'NInput' }).vm.$emit('update:value', 'xyz1')
    await nextTick()
    const updates = w.emitted('update:token') ?? []
    expect(updates[updates.length - 1]?.[0]).toBe('cid-1:xyz1')
    await expect((w.vm as unknown as { execute: () => Promise<string> }).execute()).resolves.toBe('cid-1:xyz1')
  })

  it('seeds from the challenge the backend pushed inline and does not fetch', async () => {
    const loadImage = vi.fn()
    const w = mount(TCaptcha, { props: { purpose: 'login', loadImage, seed: challenge, token: '' } })
    await flushPromises()

    expect(loadImage).not.toHaveBeenCalled()
    expect(w.find('img').attributes('src')).toBe('data:image/png;base64,AAAA')
  })

  it('execute() refuses an empty answer and reset() fetches a fresh picture', async () => {
    const loadImage = vi.fn().mockResolvedValue(challenge)
    const w = mount(TCaptcha, { props: { purpose: 'register', loadImage, token: '' } })
    await flushPromises()
    const vm = w.vm as unknown as { execute: () => Promise<string>; reset: () => void }

    await expect(vm.execute()).rejects.toThrow()

    vm.reset()
    await flushPromises()
    expect(loadImage).toHaveBeenCalledTimes(2)
  })

  it('falls back to the image provider when the config is disabled or absent', async () => {
    const loadImage = vi.fn().mockResolvedValue(challenge)
    const w = mount(TCaptcha, { props: { purpose: 'login', loadImage, config: { enabled: false }, token: '' } })
    await flushPromises()
    expect(w.attributes('data-provider')).toBe('image')
  })

  it('surfaces a failed picture load as an error', async () => {
    const loadImage = vi.fn().mockRejectedValue(new Error('captcha service down'))
    const w = mount(TCaptcha, { props: { purpose: 'login', loadImage, token: '' } })
    await flushPromises()

    expect(w.emitted('error')?.[0]?.[0]).toBe('captcha service down')
    expect(w.text()).toContain('captcha service down')
  })
})

describe('TCaptcha - script providers', () => {
  const config = { enabled: true, provider: 'turnstile', siteKey: 'site', scriptUrl: 'https://cdn.example/turnstile.js?render=explicit' }

  it('mounts the provider widget into its container and forwards the token', async () => {
    const { api, rendered } = fakeTurnstile()
    const w = mount(TCaptcha, { props: { purpose: 'contact', config, token: '' } })
    await flushPromises()

    expect(w.attributes('data-provider')).toBe('turnstile')
    expect(w.find('img').exists()).toBe(false)
    expect(rendered).toHaveLength(1)
    expect(rendered[0]!.params.sitekey).toBe('site')
    expect(rendered[0]!.params.action).toBe('contact')

    ;(rendered[0]!.params.callback as (t: string) => void)('tok-turnstile')
    await nextTick()
    const updates = w.emitted('update:token') ?? []
    expect(updates[updates.length - 1]?.[0]).toBe('tok-turnstile')
    expect(w.emitted('solved')?.[0]?.[0]).toBe('tok-turnstile')

    ;(w.vm as unknown as { reset: () => void }).reset()
    expect(api.reset).toHaveBeenCalled()
  })

  it('★ Altcha: resolves the API-relative challenge URL through resolveUrl before handing it to the widget', async () => {
    if (!customElements.get('altcha-widget')) customElements.define('altcha-widget', class extends HTMLElement {})
    const altcha = { enabled: true, provider: 'altcha', scriptUrl: 'https://cdn.example/altcha.js', challengeUrl: 'captcha/altcha/challenge?purpose={purpose}' }
    const resolveUrl = vi.fn((u: string) => `/api${u}`)
    const w = mount(TCaptcha, { props: { purpose: 'login', config: altcha, token: '', resolveUrl } })
    await flushPromises()

    expect(resolveUrl).toHaveBeenCalledWith('/captcha/altcha/challenge?purpose=login')
    expect(w.find('altcha-widget').attributes('challengeurl')).toBe('/api/captcha/altcha/challenge?purpose=login')
    expect(w.find('.t-captcha__error').exists()).toBe(false)
  })

  it('★ Altcha without resolveUrl reports the missing client instead of fetching the challenge relative to the page', async () => {
    if (!customElements.get('altcha-widget')) customElements.define('altcha-widget', class extends HTMLElement {})
    const altcha = { enabled: true, provider: 'altcha', scriptUrl: 'https://cdn.example/altcha.js', challengeUrl: 'captcha/altcha/challenge?purpose={purpose}' }
    const w = mount(TCaptcha, { props: { purpose: 'login', config: altcha, token: '' } })
    await flushPromises()

    expect(w.find('altcha-widget').exists()).toBe(false)
    expect(w.find('.t-captcha__error').text()).toMatch(/relative to the API root/)
    expect(w.emitted('error')?.[0]?.[0]).toMatch(/relative to the API root/)
  })

  it('names a provider it has no widget for instead of rendering nothing', async () => {
    const w = mount(TCaptcha, { props: { purpose: 'login', config: { enabled: true, provider: 'sliding' }, token: '' } })
    await flushPromises()

    expect(w.attributes('data-provider')).toBe('sliding')
    expect(w.text()).toContain('sliding')
    await expect((w.vm as unknown as { execute: () => Promise<string> }).execute()).rejects.toThrow(/sliding/)
  })
})
