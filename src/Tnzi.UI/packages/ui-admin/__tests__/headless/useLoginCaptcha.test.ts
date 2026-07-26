import { describe, it, expect, vi, beforeEach } from 'vitest'

// Mock the login context so the composable resolves a wired `getCaptcha`
// callback (outside a component `inject` would fall back to an empty one).
const { getCaptcha } = vi.hoisted(() => ({ getCaptcha: vi.fn() }))
vi.mock('../../src/pages/login/useLoginContext', () => ({
  useLoginContext: () => ({
    callbacks: { getCaptcha },
    translate: (_k: string, fallback?: string) => fallback ?? _k,
  }),
}))

import { useLoginCaptcha } from '../../src/headless/useLoginCaptcha'

describe('useLoginCaptcha', () => {
  beforeEach(() => {
    getCaptcha.mockReset()
  })

  it('canRefresh reflects whether getCaptcha is wired', () => {
    getCaptcha.mockResolvedValue({ captchaId: 'c', imageBase64: 'i' })
    expect(useLoginCaptcha('login').canRefresh).toBe(true)
  })

  it('load() fetches, stores id + image, and clears the typed code', async () => {
    getCaptcha.mockResolvedValue({ captchaId: 'cid', imageBase64: 'AAA', expirationSeconds: 300 })
    const c = useLoginCaptcha('login')
    c.code.value = 'typed'
    await c.load()
    expect(getCaptcha).toHaveBeenCalledWith('login')
    expect(c.captchaId.value).toBe('cid')
    expect(c.imageBase64.value).toBe('AAA')
    expect(c.code.value).toBe('')
    expect(c.loading.value).toBe(false)
  })

  it('seed() sets id + image and clears the code without fetching', () => {
    const c = useLoginCaptcha('register')
    c.code.value = 'x'
    c.seed({ captchaId: 'seeded', imageBase64: 'IMG' })
    expect(getCaptcha).not.toHaveBeenCalled()
    expect(c.captchaId.value).toBe('seeded')
    expect(c.imageBase64.value).toBe('IMG')
    expect(c.code.value).toBe('')
  })

  it('load() surfaces the error and stops loading', async () => {
    getCaptcha.mockRejectedValueOnce(new Error('boom'))
    const c = useLoginCaptcha('login')
    await c.load()
    expect(c.error.value).toBe('boom')
    expect(c.loading.value).toBe(false)
  })

  it('reset() clears id, image, code and error', () => {
    const c = useLoginCaptcha('login')
    c.seed({ captchaId: 'a', imageBase64: 'b' })
    c.code.value = 'z'
    c.reset()
    expect(c.captchaId.value).toBe('')
    expect(c.imageBase64.value).toBe('')
    expect(c.code.value).toBe('')
    expect(c.error.value).toBe('')
  })
})
