import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
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
 * The passkey leg of the two-factor step. A security key has no code, so the
 * OTP boxes give way to one "Use security key" button that runs the ceremony
 * through `verifyTwoFactorWithPasskey`; a closed system dialog leaves the
 * challenge open; and a passkey the consumer cannot complete (callback not
 * wired) is never offered, with the challenge falling back to the account's
 * next method.
 */
function makeContext(challenge: TwoFactorChallenge, callbacks: Partial<LoginCallbacks>) {
  const pendingTwoFactor = ref<TwoFactorChallenge | null>(challenge)
  const helpers = {
    setTwoFactorRequired: vi.fn(),
    clearTwoFactor: vi.fn(),
    setPendingActionRequired: vi.fn(),
    clearPendingAction: vi.fn(),
    setCaptchaRequired: vi.fn(),
    clearCaptcha: vi.fn(),
  }
  const ctx: Partial<LoginContext> = {
    translate: (key: string, fallback?: string) => fallback ?? key,
    toggleLoginModule: vi.fn(),
    callbacks,
    ui: reactive({ labeled: false, pill: true }),
    features: DEFAULT_LOGIN_FEATURES,
    pendingTwoFactor,
    helpers,
  }
  return { ctx, helpers }
}

/** The WebAuthn JSON bridges `isPasskeySupported()` looks for; happy-dom has none. */
function givenPasskeyCapableBrowser() {
  vi.stubGlobal('PublicKeyCredential', {
    parseCreationOptionsFromJSON: vi.fn(),
    parseRequestOptionsFromJSON: vi.fn(),
  })
}

beforeEach(() => givenPasskeyCapableBrowser())
afterEach(() => vi.unstubAllGlobals())

function mountModule(ctx: Partial<LoginContext>) {
  return mount(TwoFactorChallengeModule, {
    global: {
      provide: { [LOGIN_CONTEXT_KEY as unknown as symbol]: ctx },
      // naive registers the component as `InputOtp`; a stub under the `N` name is ignored.
      stubs: { InputOtp: { template: '<input class="otp" />' } },
    },
  })
}

const buttons = (w: ReturnType<typeof mountModule>) => w.findAll('button').map((b) => b.text()).filter(Boolean)

describe('TwoFactorChallenge - passkey / security key', () => {
  it('shows one ceremony button instead of the code boxes and signs in through the callback', async () => {
    const verifyTwoFactorWithPasskey = vi.fn(async () => true)
    const { ctx, helpers } = makeContext(
      { challengeId: 'tmp', userName: 'alice', method: 'passkey', methods: ['passkey', 'totp'] },
      { verifyTwoFactorWithPasskey, verifyTwoFactor: vi.fn() },
    )
    const w = mountModule(ctx)
    await flushPromises()

    expect(w.find('.otp').exists()).toBe(false)
    expect(w.text()).toContain('Insert your security key')
    expect(buttons(w)).toContain('Use security key')
    expect(buttons(w)).not.toContain('Verify')

    await w.findAll('button').find((b) => b.text() === 'Use security key')!.trigger('click')
    await flushPromises()

    expect(verifyTwoFactorWithPasskey).toHaveBeenCalledWith({ challengeId: 'tmp' }, expect.anything())
    expect(helpers.clearTwoFactor).toHaveBeenCalledTimes(1)
  })

  /**
   * ★ After the key is accepted the wrapped callback starts the navigation
   * without waiting for it and the challenge is cleared; with the target
   * chunk still loading, the form for a challenge that no longer exists was
   * on screen for seconds - and an empty challenge renders as the
   * authenticator-app prompt. The module holds a "signing you in" state
   * instead, whatever happens to the challenge afterwards.
   */
  it('shows "signing you in" after the key is accepted, even once the challenge is cleared', async () => {
    const { ctx } = makeContext(
      { challengeId: 'tmp', method: 'passkey', methods: ['passkey'] },
      { verifyTwoFactorWithPasskey: vi.fn(async () => true) },
    )
    const w = mountModule(ctx)
    await flushPromises()

    await w.findAll('button').find((b) => b.text() === 'Use security key')!.trigger('click')
    await flushPromises()
    // What the shell does right after: the challenge is gone, the page is still here.
    ctx.pendingTwoFactor!.value = null
    await flushPromises()

    expect(w.text()).toContain('Signing you in')
    expect(w.text()).not.toContain('authenticator app')
    expect(w.find('.otp').exists()).toBe(false)
    expect(buttons(w)).toEqual([])
  })

  it('says so when there is no challenge to answer, instead of an unanswerable form', async () => {
    const { ctx } = makeContext({ challengeId: 'tmp', method: 'passkey', methods: ['passkey'] }, {})
    ctx.pendingTwoFactor!.value = null
    const w = mountModule(ctx)
    await flushPromises()

    expect(w.text()).toContain('no verification pending')
    expect(w.find('.otp').exists()).toBe(false)
    expect(buttons(w)).toEqual(['Back'])
  })

  it('leaves the challenge open when the system dialog was closed', async () => {
    const { ctx, helpers } = makeContext(
      { challengeId: 'tmp', method: 'passkey', methods: ['passkey'] },
      { verifyTwoFactorWithPasskey: vi.fn(async () => false) },
    )
    const w = mountModule(ctx)
    await flushPromises()

    await w.findAll('button').find((b) => b.text() === 'Use security key')!.trigger('click')
    await flushPromises()

    expect(helpers.clearTwoFactor).not.toHaveBeenCalled()
    expect(w.find('[role="alert"]').exists()).toBe(false)
    expect(buttons(w)).toContain('Use security key')
  })

  it('offers the passkey in the switcher and switches to it without sending anything', async () => {
    const resendTwoFactor = vi.fn(async () => ({ maskedAddress: 'a***@x' }))
    const { ctx } = makeContext(
      { challengeId: 'tmp', method: 'email', methods: ['email', 'passkey'], maskedAddress: 'a***@x' },
      { verifyTwoFactorWithPasskey: vi.fn(async () => true), resendTwoFactor, verifyTwoFactor: vi.fn() },
    )
    const w = mountModule(ctx)
    await flushPromises()

    const option = w.findAll('button').find((b) => b.text() === 'Security key / passkey')!
    await option.trigger('click')
    await flushPromises()

    expect(resendTwoFactor).not.toHaveBeenCalled()
    expect(w.find('.otp').exists()).toBe(false)
    expect(buttons(w)).toContain('Use security key')
    // No resend line for a method that delivers nothing.
    expect(w.text()).not.toContain("Didn't get a code?")
  })

  /**
   * A button that cannot complete is worse than no button: without the
   * callback the passkey is not offered, and a challenge that preferred it
   * opens on the next method the account has.
   */
  it('never offers the passkey when the consumer did not wire the ceremony', async () => {
    const { ctx } = makeContext(
      { challengeId: 'tmp', method: 'passkey', methods: ['passkey', 'totp'] },
      { verifyTwoFactor: vi.fn() },
    )
    const w = mountModule(ctx)
    await flushPromises()

    expect(w.text()).toContain('authenticator app')
    expect(w.find('.otp').exists()).toBe(true)
    expect(buttons(w)).not.toContain('Use security key')
    expect(buttons(w)).not.toContain('Security key / passkey')
  })
})

describe('TwoFactorChallenge - browser support', () => {
  /**
   * A consumer that wired the ceremony still cannot run it in a browser
   * without the WebAuthn JSON bridges; offering it there leaves a button
   * whose only outcome is an error.
   */
  it('does not offer the passkey when the browser cannot run the ceremony', async () => {
    vi.stubGlobal('PublicKeyCredential', undefined)
    const { ctx } = makeContext(
      { challengeId: 'tmp', method: 'passkey', methods: ['passkey', 'totp'] },
      { verifyTwoFactorWithPasskey: vi.fn(async () => true), verifyTwoFactor: vi.fn() },
    )
    const w = mountModule(ctx)
    await flushPromises()

    expect(w.find('.otp').exists()).toBe(true)
    expect(buttons(w)).not.toContain('Use security key')
    expect(buttons(w)).not.toContain('Security key / passkey')
  })
})

describe('TwoFactorChallenge - initial code delivery', () => {
  it('shows the delivery failure and offers a resend instead of claiming a code was sent', async () => {
    const { ctx } = makeContext(
      { challengeId: 'tmp', method: 'email', methods: ['email'], codeSendError: 'Too many codes requested' },
      { verifyTwoFactor: vi.fn(), resendTwoFactor: vi.fn(async () => ({ maskedAddress: 'a***@x' })) },
    )
    const w = mountModule(ctx)
    await flushPromises()

    expect(w.find('[role="alert"]').text()).toBe('Too many codes requested')
    expect(w.text()).not.toContain('A new code has been sent.')
    // The resend entry is live, not a running countdown.
    const resend = w.findAll('button').find((b) => b.text() === 'Resend')
    expect(resend).toBeDefined()
    expect(resend!.attributes('disabled')).toBeUndefined()
  })

  it('still says a code was sent when the delivery succeeded', async () => {
    const { ctx } = makeContext(
      { challengeId: 'tmp', method: 'sms', methods: ['sms'], maskedAddress: '***71' },
      { verifyTwoFactor: vi.fn(), resendTwoFactor: vi.fn() },
    )
    const w = mountModule(ctx)
    await flushPromises()

    expect(w.text()).toContain('A new code has been sent.')
    expect(w.find('[role="alert"]').exists()).toBe(false)
  })
})

describe('TwoFactorChallenge - one request at a time', () => {
  it('ignores a second finish while the first verification is in flight', async () => {
    let release: () => void = () => {}
    const verifyTwoFactor = vi.fn(() => new Promise<void>((resolve) => { release = resolve }))
    const { ctx } = makeContext({ challengeId: 'tmp', method: 'totp', methods: ['totp'] }, { verifyTwoFactor })
    const w = mount(TwoFactorChallengeModule, {
      global: {
        provide: { [LOGIN_CONTEXT_KEY as unknown as symbol]: ctx },
        stubs: {
          InputOtp: { name: 'InputOtp', props: ['value'], emits: ['update:value', 'finish'], template: '<input class="otp" />' },
        },
      },
    })
    await flushPromises()
    const otp = w.findComponent({ name: 'InputOtp' })
    otp.vm.$emit('update:value', ['1', '2', '3', '4', '5', '6'])
    await flushPromises()

    otp.vm.$emit('finish')
    otp.vm.$emit('finish')
    await flushPromises()

    expect(verifyTwoFactor).toHaveBeenCalledTimes(1)
    release()
    await flushPromises()
  })
})
