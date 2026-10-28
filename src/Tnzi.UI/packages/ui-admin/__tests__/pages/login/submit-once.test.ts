import { describe, it, expect, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { reactive, ref, type Component } from 'vue'
import { LOGIN_CONTEXT_KEY, DEFAULT_LOGIN_FEATURES, type LoginContext } from '@tnzi/ui'
import PwdLogin from '../../../src/pages/login/modules/PwdLogin.vue'
import CodeLogin from '../../../src/pages/login/modules/CodeLogin.vue'
import Register from '../../../src/pages/login/modules/Register.vue'
import ResetPwd from '../../../src/pages/login/modules/ResetPwd.vue'
import PendingActions from '../../../src/pages/login/modules/PendingActions.vue'

/**
 * One request per submit on the built-in sign-in page. Each module listens for
 * Enter on its form and for a click on its button, and the in-flight flag went
 * up only after the (async) validation: a second Enter, or Enter followed by a
 * click, while the first attempt was still validating or answering sent the
 * same sign-in again (each attempt counts towards the lockout), spent a one-time
 * code twice, or registered twice.
 */

// Field validation is the form's own business; here it only has to pass.
vi.mock('../../../src/headless/useNaiveForm', () => ({
  useNaiveForm: () => ({ formRef: ref(null), validate: () => Promise.resolve() }),
}))

function pending(): { fn: ReturnType<typeof vi.fn>; settle: () => void } {
  let settle: () => void = () => undefined
  const fn = vi.fn(() => new Promise<void>((resolve) => { settle = resolve }))
  return { fn, settle: () => settle() }
}

function context(callbacks: LoginContext['callbacks'], extra: Partial<LoginContext> = {}): Partial<LoginContext> {
  return {
    translate: (_key: string, fallback?: string) => fallback ?? _key,
    toggleLoginModule: vi.fn(),
    callbacks,
    demoAccounts: [],
    ui: reactive({ labeled: false, pill: true }),
    thirdParty: [],
    scene: reactive({ typing: false, passwordVisible: false, passwordLength: 0 }),
    features: DEFAULT_LOGIN_FEATURES,
    pendingCaptcha: ref(null),
    resolveUrl: (url: string) => url,
    helpers: {
      setTwoFactorRequired: vi.fn(),
      clearTwoFactor: vi.fn(),
      setPendingActionRequired: vi.fn(),
      clearPendingAction: vi.fn(),
      setCaptchaRequired: vi.fn(),
      clearCaptcha: vi.fn(),
    },
    ...extra,
  } as Partial<LoginContext>
}

async function submitTwiceWhileInFlight(module: Component, ctx: Partial<LoginContext>, target = 'form'): Promise<void> {
  const wrapper = mount(module, {
    global: {
      provide: { [LOGIN_CONTEXT_KEY as unknown as symbol]: ctx },
      stubs: { TCaptcha: true, NQrCode: true },
    },
  })
  await flushPromises()
  const form = wrapper.findAll(target).at(-1)!
  // Two Enters back to back, then a click on the primary button: all before
  // the first attempt has answered.
  await form.trigger('keyup', { key: 'Enter' })
  await form.trigger('keyup', { key: 'Enter' })
  await flushPromises()
  await form.trigger('keyup', { key: 'Enter' })
  await flushPromises()
  wrapper.unmount()
}

describe('built-in login modules submit once per attempt', () => {
  it('password sign-in', async () => {
    const { fn } = pending()
    await submitTwiceWhileInFlight(PwdLogin, context({ pwdLogin: fn } as never))
    expect(fn).toHaveBeenCalledTimes(1)
  })

  it('code sign-in', async () => {
    const { fn } = pending()
    await submitTwiceWhileInFlight(CodeLogin, context({ codeLogin: fn } as never))
    expect(fn).toHaveBeenCalledTimes(1)
  })

  it('register', async () => {
    const { fn } = pending()
    await submitTwiceWhileInFlight(Register, context({ register: fn } as never))
    expect(fn).toHaveBeenCalledTimes(1)
  })

  it('reset password', async () => {
    const { fn } = pending()
    await submitTwiceWhileInFlight(ResetPwd, context({ resetPwd: fn } as never))
    expect(fn).toHaveBeenCalledTimes(1)
  })

  it('a pending action', async () => {
    const { fn } = pending()
    const ctx = context(
      {
        describePendingActions: vi.fn(async () => ({ requiredActions: ['ChangePassword'], userName: 'owing' })),
        completePasswordChange: fn,
      } as never,
      { pendingAction: ref({ tempToken: 'temp', userName: 'owing', requiredActions: ['ChangePassword'] }) } as never,
    )
    // The pending-action fields listen for Enter themselves (the last one does).
    await submitTwiceWhileInFlight(PendingActions, ctx, 'input[type="password"]')
    expect(fn).toHaveBeenCalledTimes(1)
  })

  it('a later attempt still goes through once the first has answered', async () => {
    const first = pending()
    const ctx = context({ pwdLogin: first.fn } as never)
    const wrapper = mount(PwdLogin, {
      global: { provide: { [LOGIN_CONTEXT_KEY as unknown as symbol]: ctx }, stubs: { TCaptcha: true } },
    })
    const form = wrapper.find('form')
    await form.trigger('keyup', { key: 'Enter' })
    await flushPromises()
    first.settle()
    await flushPromises()
    await form.trigger('keyup', { key: 'Enter' })
    await flushPromises()
    expect(first.fn).toHaveBeenCalledTimes(2)
    wrapper.unmount()
  })
})
