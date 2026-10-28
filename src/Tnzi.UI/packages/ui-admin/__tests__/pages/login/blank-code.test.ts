import { describe, it, expect, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { defineComponent, h, reactive, ref, type Component } from 'vue'
import { NForm, NFormItem, type FormInst, type FormItemRule } from 'naive-ui'
import { LOGIN_CONTEXT_KEY, DEFAULT_LOGIN_FEATURES, type LoginContext } from '@tnzi/ui'
import CodeLogin from '../../../src/pages/login/modules/CodeLogin.vue'
import Register from '../../../src/pages/login/modules/Register.vue'
import ResetPwd from '../../../src/pages/login/modules/ResetPwd.vue'
import PendingActions from '../../../src/pages/login/modules/PendingActions.vue'

/**
 * A verification code of only spaces is as empty as a blank one. The backend's
 * [Required] refuses it, so a code rule that lets it through only trades the
 * field message for a failed request (and, on sign-in, an attempt counted
 * towards the lockout).
 */

function context(extra: Partial<LoginContext> = {}): Partial<LoginContext> {
  return {
    translate: (_key: string, fallback?: string) => fallback ?? _key,
    toggleLoginModule: vi.fn(),
    callbacks: {
      describePendingActions: vi.fn(async () => ({ requiredActions: ['ConfirmEmail'], userName: 'owing' })),
    } as never,
    demoAccounts: [],
    ui: reactive({ labeled: false, pill: true }),
    thirdParty: [],
    scene: reactive({ typing: false, passwordVisible: false, passwordLength: 0 }),
    features: DEFAULT_LOGIN_FEATURES,
    pendingCaptcha: ref(null),
    pendingAction: ref({ tempToken: 'temp', userName: 'owing', requiredActions: ['ConfirmEmail'] }),
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

/** The module's own `code` rules, as its form would apply them. */
async function codeRules(module: Component): Promise<FormItemRule[]> {
  const wrapper = mount(module, {
    global: {
      provide: { [LOGIN_CONTEXT_KEY as unknown as symbol]: context() },
      stubs: { TCaptcha: true, NQrCode: true },
    },
  })
  await flushPromises()
  const rules = (wrapper.vm as unknown as { rules: { code: FormItemRule[] } }).rules.code
  wrapper.unmount()
  return rules
}

/** Messages a real NForm reports for `value` under `rules`. */
async function errorsFor(rules: FormItemRule[], value: string): Promise<string[]> {
  const formRef = ref<FormInst | null>(null)
  const wrapper = mount(defineComponent({
    setup: () => () =>
      h(NForm, { ref: formRef, model: { code: value }, rules: { code: rules } }, () =>
        h(NFormItem, { path: 'code', label: 'Code' }, () => h('span'))),
  }))
  try {
    await formRef.value!.validate()
    return []
  } catch (errors) {
    return (errors as Array<Array<{ message?: string }>>).flat().map((e) => e.message ?? '')
  } finally {
    wrapper.unmount()
  }
}

describe('built-in login modules: a whitespace-only verification code is empty', () => {
  it.each([
    ['code sign-in', CodeLogin],
    ['register', Register],
    ['reset password', ResetPwd],
    ['a pending action', PendingActions],
  ] as const)('%s refuses a whitespace-only code with the required message', async (_case, module) => {
    const rules = await codeRules(module)
    expect(await errorsFor(rules, '   ')).toEqual(['Please enter the verification code'])
    expect(await errorsFor(rules, '\t \n')).toEqual(['Please enter the verification code'])
    // Positive control: a real code passes the same rules.
    expect(await errorsFor(rules, '123456')).toEqual([])
  })
})
