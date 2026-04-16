import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { usePasswordReset } from '../../../src/composables/auth/usePasswordReset'
import type { PasswordResetProvider } from '../../../src/composables/auth/types'

function mockProvider(overrides: Partial<PasswordResetProvider> = {}): PasswordResetProvider {
  return {
    requestReset: vi.fn().mockResolvedValue(undefined),
    verifyCode: vi.fn().mockResolvedValue(undefined),
    resetPassword: vi.fn().mockResolvedValue(undefined),
    ...overrides,
  }
}

describe('usePasswordReset', () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('initializes with empty state at request step', () => {
    const { state, loading } = usePasswordReset({ provider: mockProvider() })
    expect(state.value.email).toBe('')
    expect(state.value.step).toBe('request')
    expect(state.value.countdown).toBe(0)
    expect(loading.value).toBe(false)
  })

  describe('sendCode', () => {
    it('rejects empty email', async () => {
      const { state, sendCode } = usePasswordReset({ provider: mockProvider() })
      await sendCode()
      expect(state.value.errors.email).toBeTruthy()
      expect(state.value.step).toBe('request')
    })

    it('rejects invalid email format', async () => {
      const { state, sendCode } = usePasswordReset({ provider: mockProvider() })
      state.value.email = 'not-an-email'
      await sendCode()
      expect(state.value.errors.email).toMatch(/invalid/i)
    })

    it('advances to verify step on success and starts countdown', async () => {
      const provider = mockProvider()
      const { state, sendCode } = usePasswordReset({ provider, countdownSeconds: 5 })
      state.value.email = 'alice@example.com'
      await sendCode()
      expect(provider.requestReset).toHaveBeenCalledWith('alice@example.com')
      expect(state.value.step).toBe('verify')
      expect(state.value.countdown).toBe(5)
    })

    it('countdown ticks down every second', async () => {
      const { state, sendCode } = usePasswordReset({ provider: mockProvider(), countdownSeconds: 3 })
      state.value.email = 'a@b.co'
      await sendCode()
      expect(state.value.countdown).toBe(3)
      vi.advanceTimersByTime(1000)
      expect(state.value.countdown).toBe(2)
      vi.advanceTimersByTime(1000)
      expect(state.value.countdown).toBe(1)
      vi.advanceTimersByTime(1000)
      expect(state.value.countdown).toBe(0)
    })

    it('uses default countdown (60s) when not specified', async () => {
      const { state, sendCode } = usePasswordReset({ provider: mockProvider() })
      state.value.email = 'a@b.co'
      await sendCode()
      expect(state.value.countdown).toBe(60)
    })

    it('invokes onError when requestReset throws', async () => {
      const onError = vi.fn()
      const provider = mockProvider({ requestReset: vi.fn().mockRejectedValue(new Error('smtp down')) })
      const { state, sendCode } = usePasswordReset({ provider, onError })
      state.value.email = 'a@b.co'
      await sendCode()
      expect(onError).toHaveBeenCalledWith(expect.any(Error))
      expect((onError.mock.calls[0]![0] as Error).message).toBe('smtp down')
      expect(state.value.step).toBe('request')
    })

    it('wraps non-Error throws', async () => {
      const onError = vi.fn()
      const provider = mockProvider({ requestReset: vi.fn().mockRejectedValue('plain-string') })
      const { sendCode, state } = usePasswordReset({ provider, onError })
      state.value.email = 'a@b.co'
      await sendCode()
      expect((onError.mock.calls[0]![0] as Error).message).toBe('plain-string')
    })
  })

  describe('verifyAndReset', () => {
    it('rejects empty captcha', async () => {
      const { state, verifyAndReset } = usePasswordReset({ provider: mockProvider() })
      state.value.newPassword = 'abcdefgh'
      state.value.confirmPassword = 'abcdefgh'
      await verifyAndReset()
      expect(state.value.errors.captcha).toBeTruthy()
    })

    it('rejects short password', async () => {
      const { state, verifyAndReset } = usePasswordReset({ provider: mockProvider() })
      state.value.captcha = '123456'
      state.value.newPassword = 'short'
      state.value.confirmPassword = 'short'
      await verifyAndReset()
      expect(state.value.errors.newPassword).toMatch(/8 characters/)
    })

    it('rejects missing confirmPassword', async () => {
      const { state, verifyAndReset } = usePasswordReset({ provider: mockProvider() })
      state.value.captcha = '123456'
      state.value.newPassword = 'abcdefgh'
      await verifyAndReset()
      expect(state.value.errors.confirmPassword).toBeTruthy()
    })

    it('rejects mismatched confirmPassword', async () => {
      const { state, verifyAndReset } = usePasswordReset({ provider: mockProvider() })
      state.value.captcha = '123456'
      state.value.newPassword = 'abcdefgh'
      state.value.confirmPassword = 'abcdefgZ'
      await verifyAndReset()
      expect(state.value.errors.confirmPassword).toMatch(/do not match/)
    })

    it('advances to done on success and invokes onSuccess', async () => {
      const onSuccess = vi.fn()
      const provider = mockProvider()
      const { state, verifyAndReset } = usePasswordReset({ provider, onSuccess })
      state.value.email = 'a@b.co'
      state.value.captcha = '123456'
      state.value.newPassword = 'abcdefgh'
      state.value.confirmPassword = 'abcdefgh'
      await verifyAndReset()
      expect(provider.resetPassword).toHaveBeenCalledWith('a@b.co', '123456', 'abcdefgh')
      expect(state.value.step).toBe('done')
      expect(onSuccess).toHaveBeenCalled()
    })

    it('invokes onError when resetPassword throws', async () => {
      const onError = vi.fn()
      const provider = mockProvider({ resetPassword: vi.fn().mockRejectedValue(new Error('invalid code')) })
      const { state, verifyAndReset } = usePasswordReset({ provider, onError })
      state.value.email = 'a@b.co'
      state.value.captcha = '000000'
      state.value.newPassword = 'abcdefgh'
      state.value.confirmPassword = 'abcdefgh'
      await verifyAndReset()
      expect(onError).toHaveBeenCalled()
      expect(state.value.step).not.toBe('done')
    })
  })

  describe('reset & clearErrors', () => {
    it('reset clears all state and stops timer', async () => {
      const { state, sendCode, reset } = usePasswordReset({ provider: mockProvider(), countdownSeconds: 5 })
      state.value.email = 'a@b.co'
      await sendCode()
      expect(state.value.countdown).toBe(5)
      reset()
      expect(state.value.email).toBe('')
      expect(state.value.step).toBe('request')
      expect(state.value.countdown).toBe(0)
      // timer cleared: advance and verify no further mutation
      vi.advanceTimersByTime(5000)
      expect(state.value.countdown).toBe(0)
    })

    it('clearErrors empties errors object', () => {
      const { state, clearErrors } = usePasswordReset({ provider: mockProvider() })
      state.value.errors = { email: 'bad' }
      clearErrors()
      expect(state.value.errors).toEqual({})
    })
  })
})
