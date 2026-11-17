import { describe, it, expect, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { nextTick, reactive, ref } from 'vue'
import { LOGIN_CONTEXT_KEY, DEFAULT_LOGIN_FEATURES, type LoginContext, type LoginFeatures } from '@tnzi/ui'
import PwdLogin from '../../../src/pages/login/modules/PwdLogin.vue'

/**
 * The sign-in page renders with the default features (everything on) until
 * `GET /auth/config` answers, then hides what the deployment switched off.
 * The entries around the Sign in button ("Forgot password?" above it, "Code
 * login" / "Register" below it) therefore go from rendered to not rendered on
 * a live page. That transition once left three identical Sign in buttons on
 * screen: the entries sat in an `NSpace`, whose 2.45.3 build gives every item
 * wrapper the same key, so Vue patched the Sign in vnode into all three slots.
 */

function context(features: LoginFeatures): Partial<LoginContext> {
  const noop = vi.fn(() => Promise.resolve())
  return {
    translate: (_key: string, fallback?: string) => fallback ?? _key,
    toggleLoginModule: vi.fn(),
    callbacks: { pwdLogin: noop, codeLogin: noop, register: noop, resetPwd: noop } as never,
    demoAccounts: [],
    ui: reactive({ labeled: false, pill: true }),
    thirdParty: [],
    scene: reactive({ typing: false, passwordVisible: false, passwordLength: 0 }),
    features,
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
  } as Partial<LoginContext>
}

function buttonLabels(wrapper: ReturnType<typeof mount>): string[] {
  return wrapper.findAll('button.n-button').map((b) => b.text().trim())
}

async function mountWith(features: LoginFeatures) {
  const wrapper = mount(PwdLogin, {
    global: {
      provide: { [LOGIN_CONTEXT_KEY as unknown as symbol]: context(features) },
      stubs: { TCaptcha: true },
    },
  })
  await flushPromises()
  return wrapper
}

describe('PwdLogin when /auth/config answers after the first render', () => {
  it('drops the entries the deployment switched off and keeps one Sign in', async () => {
    const features = reactive<LoginFeatures>({ ...DEFAULT_LOGIN_FEATURES })
    const wrapper = await mountWith(features)
    expect(buttonLabels(wrapper)).toEqual(['Forgot password?', 'Sign in', 'Code login', 'Register'])

    features.passwordRecovery = false
    features.codeLogin = false
    features.register = false
    await nextTick()

    expect(buttonLabels(wrapper)).toEqual(['Sign in'])
    wrapper.unmount()
  })

  it('shows entries the deployment switched on after the first render', async () => {
    const features = reactive<LoginFeatures>({
      ...DEFAULT_LOGIN_FEATURES,
      passwordRecovery: false,
      codeLogin: false,
      register: false,
    })
    const wrapper = await mountWith(features)
    expect(buttonLabels(wrapper)).toEqual(['Sign in'])

    features.passwordRecovery = true
    features.codeLogin = true
    await nextTick()
    expect(buttonLabels(wrapper)).toEqual(['Forgot password?', 'Sign in', 'Code login'])

    features.passwordRecovery = false
    await nextTick()
    expect(buttonLabels(wrapper)).toEqual(['Sign in', 'Code login'])
    wrapper.unmount()
  })
})
