import { describe, it, expect, vi } from 'vitest'
import { useLoginForm } from '../../../src/composables/auth/useLoginForm'
import type { LoginProvider } from '../../../src/composables/auth/types'

describe('useLoginForm', () => {
  function mockProvider(overrides: Partial<LoginProvider> = {}): LoginProvider {
    return {
      login: vi.fn().mockResolvedValue({ success: true, user: { id: '1' } }),
      ...overrides,
    }
  }

  it('initializes with empty state', () => {
    const { state } = useLoginForm({ provider: mockProvider() })
    expect(state.value.username).toBe('')
    expect(state.value.password).toBe('')
    expect(state.value.rememberMe).toBe(false)
  })

  it('validates required username', () => {
    const { state, validate } = useLoginForm({ provider: mockProvider() })
    state.value.username = ''
    state.value.password = 'abcdef'
    const ok = validate()
    expect(ok).toBe(false)
    expect(state.value.errors.username).toBeTruthy()
  })

  it('validates required password', () => {
    const { state, validate } = useLoginForm({ provider: mockProvider() })
    state.value.username = 'alice'
    state.value.password = ''
    const ok = validate()
    expect(ok).toBe(false)
    expect(state.value.errors.password).toBeTruthy()
  })

  it('passes validation with both fields filled', () => {
    const { state, validate } = useLoginForm({ provider: mockProvider() })
    state.value.username = 'alice'
    state.value.password = 'secret'
    expect(validate()).toBe(true)
  })

  it('calls provider.login on handleSubmit when valid', async () => {
    const provider = mockProvider()
    const { state, handleSubmit } = useLoginForm({ provider })
    state.value.username = 'alice'
    state.value.password = 'secret'
    await handleSubmit()
    expect(provider.login).toHaveBeenCalledWith(expect.objectContaining({
      username: 'alice',
      password: 'secret',
    }))
  })

  it('sets loading during submit and clears after', async () => {
    const provider: LoginProvider = {
      login: vi.fn().mockImplementation(() => new Promise(resolve => setTimeout(() => resolve({ success: true, user: {} }), 10))),
    }
    const { state, loading, handleSubmit } = useLoginForm({ provider })
    state.value.username = 'aaaaaa'
    state.value.password = 'bbbbbb'
    const p = handleSubmit()
    expect(loading.value).toBe(true)
    await p
    expect(loading.value).toBe(false)
  })

  it('calls onSuccess callback on successful login', async () => {
    const onSuccess = vi.fn()
    const { state, handleSubmit } = useLoginForm({ provider: mockProvider(), onSuccess })
    state.value.username = 'aaaaaa'
    state.value.password = 'bbbbbb'
    await handleSubmit()
    expect(onSuccess).toHaveBeenCalledWith({ id: '1' })
  })

  it('calls onError callback on failed login', async () => {
    const provider: LoginProvider = {
      login: vi.fn().mockResolvedValue({ success: false, error: 'Invalid credentials' }),
    }
    const onError = vi.fn()
    const { state, handleSubmit } = useLoginForm({ provider, onError })
    state.value.username = 'aaaaaa'
    state.value.password = 'bbbbbb'
    await handleSubmit()
    expect(onError).toHaveBeenCalledWith(expect.objectContaining({ message: 'Invalid credentials' }))
  })

  it('handleSocial calls provider.loginWithSocial if available', async () => {
    const provider: LoginProvider = {
      login: vi.fn(),
      loginWithSocial: vi.fn().mockResolvedValue(undefined),
    }
    const { handleSocial } = useLoginForm({ provider })
    await handleSocial('github')
    expect(provider.loginWithSocial).toHaveBeenCalledWith('github')
  })
})
