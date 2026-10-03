import { describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { reactive, ref } from 'vue'
import {
  DEFAULT_LOGIN_FEATURES,
  LOGIN_CONTEXT_KEY,
  type LoginCallbacks,
  type LoginContext,
  type TwoFactorChallenge,
} from '@tnzi/ui'
import TwoFactorChallengeModule from '../../../src/pages/login/modules/TwoFactorChallenge.vue'

/**
 * The code boxes of the two-factor step size themselves from the deployment.
 * Emailed / texted codes are generated with `Identity:Otp:CodeLength` (4-8,
 * a runtime setting reported by `GET /auth/config`); with the boxes fixed at 6
 * a deployment configured for 8 sent codes no one could type in, so two-factor
 * sign-in was impossible. Authenticator-app codes are 6 by the standard and
 * must stay 6 whatever the setting says.
 */
function mountModule(challenge: TwoFactorChallenge, otpCodeLength: number | undefined, callbacks: Partial<LoginCallbacks> = {}) {
  const features = otpCodeLength === undefined ? DEFAULT_LOGIN_FEATURES : { ...DEFAULT_LOGIN_FEATURES, otpCodeLength }
  const ctx: Partial<LoginContext> = {
    translate: (key: string, fallback?: string) => fallback ?? key,
    toggleLoginModule: vi.fn(),
    callbacks,
    ui: reactive({ labeled: false, pill: true }),
    features,
    pendingTwoFactor: ref<TwoFactorChallenge | null>(challenge),
    helpers: {
      setTwoFactorRequired: vi.fn(),
      clearTwoFactor: vi.fn(),
      setPendingActionRequired: vi.fn(),
      clearPendingAction: vi.fn(),
      setCaptchaRequired: vi.fn(),
      clearCaptcha: vi.fn(),
    } as unknown as LoginContext['helpers'],
  }
  return mount(TwoFactorChallengeModule, {
    global: {
      provide: { [LOGIN_CONTEXT_KEY as unknown as symbol]: ctx },
      stubs: {
        InputOtp: {
          name: 'InputOtp',
          props: ['value', 'length'],
          emits: ['update:value', 'finish'],
          template: '<input class="otp" :data-length="length" />',
        },
      },
    },
  })
}

const boxes = (w: ReturnType<typeof mountModule>) => Number(w.find('.otp').attributes('data-length'))
const digits = (n: number) => Array.from({ length: n }, (_, i) => String((i + 1) % 10))

async function typeAndVerify(w: ReturnType<typeof mountModule>, n: number) {
  w.findComponent({ name: 'InputOtp' }).vm.$emit('update:value', digits(n))
  await flushPromises()
  await w.findAll('button').find((b) => b.text() === 'Verify')!.trigger('click')
  await flushPromises()
}

describe('TwoFactorChallenge - code length', () => {
  it('an emailed code on an 8-digit deployment gets 8 boxes and an 8-digit instruction', async () => {
    const verifyTwoFactor = vi.fn(async () => {})
    const w = mountModule({ challengeId: 'c', method: 'email', methods: ['email'] }, 8, { verifyTwoFactor })
    await flushPromises()

    expect(boxes(w)).toBe(8)
    expect(w.text()).toContain('Enter the 8-digit code we sent to your email.')

    // Six digits are not a complete code there: nothing is sent.
    await typeAndVerify(w, 6)
    expect(verifyTwoFactor).not.toHaveBeenCalled()

    await typeAndVerify(w, 8)
    expect(verifyTwoFactor).toHaveBeenCalledWith(
      expect.objectContaining({ code: '12345678', method: 'email' }),
      expect.anything(),
    )
  })

  it('a texted code to a known address also says 8 digits', async () => {
    const w = mountModule({ challengeId: 'c', method: 'sms', methods: ['sms'], maskedAddress: '+1***99' }, 8)
    await flushPromises()

    expect(boxes(w)).toBe(8)
    expect(w.text()).toContain('Enter the 8-digit code we sent to +1***99.')
  })

  it('defaults to 6 when the backend does not report a length', async () => {
    const w = mountModule({ challengeId: 'c', method: 'sms', methods: ['sms'] }, undefined)
    await flushPromises()

    expect(boxes(w)).toBe(6)
    expect(w.text()).toContain('Enter the 6-digit code we texted to your phone.')
  })

  it('the authenticator app stays at 6 on an 8-digit deployment', async () => {
    const verifyTwoFactor = vi.fn(async () => {})
    const w = mountModule({ challengeId: 'c', method: 'totp', methods: ['totp', 'email'] }, 8, { verifyTwoFactor })
    await flushPromises()

    expect(boxes(w)).toBe(6)
    expect(w.text()).toContain('Enter the 6-digit code from your authenticator app.')
    await typeAndVerify(w, 6)
    expect(verifyTwoFactor).toHaveBeenCalledWith(expect.objectContaining({ code: '123456', method: 'totp' }), expect.anything())
  })

  it('switching from the authenticator to email resizes the boxes', async () => {
    const resendTwoFactor = vi.fn(async () => ({ maskedAddress: 'j***@example.com' }))
    const w = mountModule({ challengeId: 'c', method: 'totp', methods: ['totp', 'email'] }, 8, { resendTwoFactor })
    await flushPromises()
    expect(boxes(w)).toBe(6)

    await w.findAll('button').find((b) => b.text() === 'Email')!.trigger('click')
    await flushPromises()

    expect(boxes(w)).toBe(8)
    expect(w.text()).toContain('Enter the 8-digit code we sent to j***@example.com.')
  })
})
