import { describe, it, expect, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { ref, reactive } from 'vue'
import {
  LOGIN_CONTEXT_KEY,
  DEFAULT_LOGIN_FEATURES,
  type LoginContext,
  type PendingActionChallenge,
} from '@tnzi/ui'
import PendingActions from '../../../src/pages/login/modules/PendingActions.vue'

function makeContext(overrides: {
  describe?: LoginContext['callbacks']['describePendingActions']
  pendingAction?: PendingActionChallenge | null
} = {}) {
  const pendingAction = ref<PendingActionChallenge | null>(
    overrides.pendingAction === undefined
      ? { tempToken: 'temp', userName: 'owing', requiredActions: ['ChangePassword'] }
      : overrides.pendingAction,
  )

  const ctx: Partial<LoginContext> = {
    translate: (_key: string, fallback?: string) => fallback ?? _key,
    toggleLoginModule: vi.fn(),
    callbacks: { describePendingActions: overrides.describe },
    ui: reactive({ labeled: false, pill: true }),
    features: DEFAULT_LOGIN_FEATURES,
    pendingAction,
    helpers: {
      setTwoFactorRequired: vi.fn(),
      clearTwoFactor: vi.fn(),
      setPendingActionRequired: vi.fn(),
      clearPendingAction: vi.fn(),
      setCaptchaRequired: vi.fn(),
      clearCaptcha: vi.fn(),
    },
  }

  return { ctx, pendingAction }
}

function mountPanel(ctx: Partial<LoginContext>) {
  return mount(PendingActions, {
    global: {
      provide: { [LOGIN_CONTEXT_KEY as unknown as symbol]: ctx },
      stubs: { NQrCode: true },
    },
  })
}

describe('PendingActions', () => {
  it('renders the form for the action the server reports', async () => {
    const { ctx } = makeContext({
      describe: vi.fn(async () => ({
        requiredActions: ['ChangePassword'],
        userName: 'owing',
      })),
    })

    const wrapper = mountPanel(ctx)
    await flushPromises()

    expect(wrapper.text()).toContain('set a new password')
    expect(wrapper.findAll('input[type="password"]').length).toBe(2)
  })

  /**
   * ★★★ 读不到待办清单时不能猜一个表单出来。
   *
   * 失败与「没有待办」的 `remaining` 都是空的，混为一谈的后果是渲染出三元链的兜底分支
   * （确认邮箱），用户填完只会拿到一句「该流程尚未配置」，真正的原因一个字都没说。
   */
  it('shows an actionable error instead of guessing a form when the lookup fails', async () => {
    const { ctx } = makeContext({ describe: vi.fn(async () => null) })

    const wrapper = mountPanel(ctx)
    await flushPromises()

    expect(wrapper.text()).toContain('could not load')
    expect(wrapper.text()).not.toContain('confirm your email')
    expect(wrapper.findAll('input').length).toBe(0)
  })

  it('treats a thrown lookup the same as a failed one', async () => {
    const { ctx } = makeContext({
      describe: vi.fn(async () => {
        throw new Error('network down')
      }),
    })

    const wrapper = mountPanel(ctx)
    await flushPromises()

    expect(wrapper.text()).toContain('could not load')
    expect(wrapper.findAll('input').length).toBe(0)
  })

  /**
   * 清单为空但请求成功（前后端状态不一致）同样不渲染表单 —— 没有当前动作就没有表单可填。
   */
  /**
   * 办完这件之后签发会话仍要过守卫链：账号还开着别的 2FA 方式时，回调把后端的 403
   * `2FA_REQUIRED` 交给 shell 并答 `challenged: true`。此时这枚待办令牌已被消费 ——
   * 本页要清掉挑战并到此为止，而不是把它当成「还欠着别的」重新拉清单。
   */
  it('hands over to the two-factor challenge when completing the action is answered with one', async () => {
    const { ctx } = makeContext({
      describe: vi.fn(async () => ({ requiredActions: ['ChangePassword'], userName: 'owing' })),
    })
    const completePasswordChange = vi.fn(async () => ({ completed: false, remainingActions: [], challenged: true }))
    ctx.callbacks!.completePasswordChange = completePasswordChange
    const wrapper = mountPanel(ctx)
    await flushPromises()

    const vm = wrapper.vm as unknown as {
      model: { password: string; confirmPassword: string }
      handleSubmit: () => Promise<void>
      submitError: string
    }
    vm.model.password = 'Str0ng!Pass#2026'
    vm.model.confirmPassword = 'Str0ng!Pass#2026'
    await vm.handleSubmit()
    await flushPromises()

    expect(completePasswordChange).toHaveBeenCalledWith(
      { tempToken: 'temp', newPassword: 'Str0ng!Pass#2026' },
      ctx.helpers,
    )
    expect(ctx.helpers!.clearPendingAction).toHaveBeenCalledTimes(1)
    expect(vm.submitError).toBe('')
    // Nothing was re-fetched: the challenge is not "something else still owed".
    expect(ctx.callbacks!.describePendingActions).toHaveBeenCalledTimes(1)
  })

  it('renders no form when the server reports nothing owed', async () => {
    const { ctx } = makeContext({
      describe: vi.fn(async () => ({ requiredActions: [], userName: 'owing' })),
    })

    const wrapper = mountPanel(ctx)
    await flushPromises()

    expect(wrapper.findAll('input').length).toBe(0)
  })
})
