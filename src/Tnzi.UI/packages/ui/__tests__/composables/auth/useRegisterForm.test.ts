import { describe, it, expect, vi } from 'vitest'
import { useRegisterForm } from '../../../src/composables/auth/useRegisterForm'
import type { RegisterProvider } from '../../../src/composables/auth/types'

function mockProvider(overrides: Partial<RegisterProvider> = {}): RegisterProvider {
  return {
    register: vi.fn().mockResolvedValue({ success: true, user: { id: 'u1' } }),
    ...overrides,
  }
}

function fillValidState(state: any) {
  state.value.username = 'alice'
  state.value.email = 'alice@example.com'
  state.value.password = 'abcdefgh'
  state.value.confirmPassword = 'abcdefgh'
  state.value.agreeTerms = true
}

describe('useRegisterForm', () => {
  describe('initial state', () => {
    it('defaults to empty strings and unchecked terms', () => {
      const { state, loading } = useRegisterForm({ provider: mockProvider() })
      expect(state.value.username).toBe('')
      expect(state.value.email).toBe('')
      expect(state.value.agreeTerms).toBe(false)
      expect(loading.value).toBe(false)
    })

    it('honors initialValues', () => {
      const { state } = useRegisterForm({
        provider: mockProvider(),
        initialValues: { username: 'bob', email: 'bob@x.co', agreeTerms: true },
      })
      expect(state.value.username).toBe('bob')
      expect(state.value.email).toBe('bob@x.co')
      expect(state.value.agreeTerms).toBe(true)
    })
  })

  describe('validate', () => {
    it('rejects missing username', () => {
      const { state, validate } = useRegisterForm({ provider: mockProvider() })
      fillValidState(state)
      state.value.username = ''
      expect(validate()).toBe(false)
      expect(state.value.errors.username).toMatch(/required/i)
    })

    it('rejects short username', () => {
      const { state, validate } = useRegisterForm({ provider: mockProvider() })
      fillValidState(state)
      state.value.username = 'ab'
      expect(validate()).toBe(false)
      expect(state.value.errors.username).toMatch(/3 characters/)
    })

    it('rejects invalid email', () => {
      const { state, validate } = useRegisterForm({ provider: mockProvider() })
      fillValidState(state)
      state.value.email = 'no-at'
      expect(validate()).toBe(false)
      expect(state.value.errors.email).toMatch(/invalid/)
    })

    it('rejects missing email', () => {
      const { state, validate } = useRegisterForm({ provider: mockProvider() })
      fillValidState(state)
      state.value.email = ''
      expect(validate()).toBe(false)
      expect(state.value.errors.email).toMatch(/required/i)
    })

    describe('phone validation', () => {
      it('requires phone when requirePhone=true and phone empty', () => {
        const { state, validate } = useRegisterForm({ provider: mockProvider(), requirePhone: true })
        fillValidState(state)
        expect(validate()).toBe(false)
        expect(state.value.errors.phone).toMatch(/required/i)
      })

      it('validates phone format when requirePhone=true', () => {
        const { state, validate } = useRegisterForm({ provider: mockProvider(), requirePhone: true })
        fillValidState(state)
        state.value.phone = 'abc'
        expect(validate()).toBe(false)
        expect(state.value.errors.phone).toMatch(/invalid/)
      })

      it('accepts valid phone when requirePhone=true', () => {
        const { state, validate } = useRegisterForm({ provider: mockProvider(), requirePhone: true })
        fillValidState(state)
        state.value.phone = '+1 (555) 123-4567'
        expect(validate()).toBe(true)
      })

      it('phone is optional when requirePhone=false', () => {
        const { state, validate } = useRegisterForm({ provider: mockProvider() })
        fillValidState(state)
        state.value.phone = ''
        expect(validate()).toBe(true)
      })

      it('validates phone format even when optional if provided', () => {
        const { state, validate } = useRegisterForm({ provider: mockProvider() })
        fillValidState(state)
        state.value.phone = 'garbage'
        expect(validate()).toBe(false)
        expect(state.value.errors.phone).toMatch(/invalid/)
      })
    })

    it('rejects missing password', () => {
      const { state, validate } = useRegisterForm({ provider: mockProvider() })
      fillValidState(state)
      state.value.password = ''
      expect(validate()).toBe(false)
      expect(state.value.errors.password).toBeTruthy()
    })

    it('rejects short password', () => {
      const { state, validate } = useRegisterForm({ provider: mockProvider() })
      fillValidState(state)
      state.value.password = 'short'
      state.value.confirmPassword = 'short'
      expect(validate()).toBe(false)
      expect(state.value.errors.password).toMatch(/8 characters/)
    })

    it('rejects empty confirmPassword', () => {
      const { state, validate } = useRegisterForm({ provider: mockProvider() })
      fillValidState(state)
      state.value.confirmPassword = ''
      expect(validate()).toBe(false)
      expect(state.value.errors.confirmPassword).toBeTruthy()
    })

    it('rejects mismatched confirmPassword', () => {
      const { state, validate } = useRegisterForm({ provider: mockProvider() })
      fillValidState(state)
      state.value.confirmPassword = 'differenT'
      expect(validate()).toBe(false)
      expect(state.value.errors.confirmPassword).toMatch(/do not match/)
    })

    it('rejects unchecked terms', () => {
      const { state, validate } = useRegisterForm({ provider: mockProvider() })
      fillValidState(state)
      state.value.agreeTerms = false
      expect(validate()).toBe(false)
      expect(state.value.errors.agreeTerms).toMatch(/agree/i)
    })

    it('passes when everything valid', () => {
      const { state, validate } = useRegisterForm({ provider: mockProvider() })
      fillValidState(state)
      expect(validate()).toBe(true)
      expect(state.value.errors).toEqual({})
    })
  })

  describe('handleSubmit', () => {
    it('does nothing if validation fails', async () => {
      const provider = mockProvider()
      const { handleSubmit } = useRegisterForm({ provider })
      await handleSubmit()
      expect(provider.register).not.toHaveBeenCalled()
    })

    it('calls onSubmit before provider.register', async () => {
      const onSubmit = vi.fn()
      const provider = mockProvider()
      const { state, handleSubmit } = useRegisterForm({ provider, onSubmit })
      fillValidState(state)
      await handleSubmit()
      expect(onSubmit).toHaveBeenCalled()
      expect(provider.register).toHaveBeenCalled()
    })

    it('invokes onSuccess with user on success', async () => {
      const onSuccess = vi.fn()
      const { state, handleSubmit } = useRegisterForm({ provider: mockProvider(), onSuccess })
      fillValidState(state)
      await handleSubmit()
      expect(onSuccess).toHaveBeenCalledWith({ id: 'u1' })
    })

    it('invokes onError with structured failure', async () => {
      const onError = vi.fn()
      const provider = mockProvider({ register: vi.fn().mockResolvedValue({ success: false, error: 'taken' }) })
      const { state, handleSubmit } = useRegisterForm({ provider, onError })
      fillValidState(state)
      await handleSubmit()
      expect(onError).toHaveBeenCalled()
      expect((onError.mock.calls[0]![0] as Error).message).toBe('taken')
    })

    it('invokes onError when register throws Error', async () => {
      const onError = vi.fn()
      const provider = mockProvider({ register: vi.fn().mockRejectedValue(new Error('network')) })
      const { state, handleSubmit } = useRegisterForm({ provider, onError })
      fillValidState(state)
      await handleSubmit()
      expect((onError.mock.calls[0]![0] as Error).message).toBe('network')
    })

    it('wraps non-Error throws', async () => {
      const onError = vi.fn()
      const provider = mockProvider({ register: vi.fn().mockRejectedValue('oops') })
      const { state, handleSubmit } = useRegisterForm({ provider, onError })
      fillValidState(state)
      await handleSubmit()
      expect((onError.mock.calls[0]![0] as Error).message).toBe('oops')
    })

    it('sets loading true during submit and false after', async () => {
      let resolveRegister: (v: any) => void = () => {}
      const provider = mockProvider({
        register: vi.fn(() => new Promise((r) => { resolveRegister = r })),
      })
      const { state, loading, handleSubmit } = useRegisterForm({ provider })
      fillValidState(state)
      const p = handleSubmit()
      expect(loading.value).toBe(true)
      resolveRegister({ success: true, user: {} })
      await p
      expect(loading.value).toBe(false)
    })
  })

  describe('checkUsername', () => {
    it('returns null when provider lacks checkUsernameAvailable', async () => {
      const { checkUsername } = useRegisterForm({ provider: mockProvider() })
      expect(await checkUsername()).toBeNull()
    })

    it('returns null when username is empty', async () => {
      const { checkUsername } = useRegisterForm({
        provider: mockProvider({ checkUsernameAvailable: vi.fn().mockResolvedValue(true) }),
      })
      expect(await checkUsername()).toBeNull()
    })

    it('calls provider.checkUsernameAvailable and forwards result', async () => {
      const check = vi.fn().mockResolvedValue(true)
      const { state, checkUsername } = useRegisterForm({
        provider: mockProvider({ checkUsernameAvailable: check }),
      })
      state.value.username = '  alice '
      expect(await checkUsername()).toBe(true)
      expect(check).toHaveBeenCalledWith('alice')
    })
  })

  describe('reset & clearErrors', () => {
    it('reset restores initialValues', () => {
      const { state, reset } = useRegisterForm({
        provider: mockProvider(),
        initialValues: { username: 'seed' },
      })
      state.value.username = 'changed'
      state.value.errors = { email: 'bad' }
      reset()
      expect(state.value.username).toBe('seed')
      expect(state.value.errors).toEqual({})
    })

    it('clearErrors empties errors', () => {
      const { state, clearErrors } = useRegisterForm({ provider: mockProvider() })
      state.value.errors = { username: 'taken' }
      clearErrors()
      expect(state.value.errors).toEqual({})
    })
  })
})
