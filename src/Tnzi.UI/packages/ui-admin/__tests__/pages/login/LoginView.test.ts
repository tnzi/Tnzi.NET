import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { defineComponent, h } from 'vue'

// ---------------------------------------------------------------------------
// The login ROUTE is the one place the session-end notice can be shown: core
// leaves `auth.error` = "ended for security reasons" after a rejected refresh,
// `createTnziClient` keeps it across the unauthorized signal, and defineAdminApp
// redirects here. Until this landed nothing read it - the user was bounced to
// an empty password form with no explanation.
// ---------------------------------------------------------------------------

const runtimeMock = vi.hoisted(() => ({ current: null as null | { auth: { sessionEndReason: string | null } } }))

vi.mock('vue-router', () => ({
  useRoute: () => ({ params: {}, query: {} }),
  useRouter: () => ({ replace: vi.fn() }),
}))
vi.mock('../../../src/plugin/login-config', () => ({
  useAdminLoginConfig: () => ({ loadConfigFromServer: false }),
}))
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => undefined,
}))
vi.mock('../../../src/plugin/runtime', () => ({
  useAdminRuntime: () => runtimeMock.current,
}))
vi.mock('../../../src/services/bridges/identity-bridge', () => ({
  createIdentityBridge: () => ({ getAuthConfig: vi.fn(async () => null) }),
}))

const { stub } = vi.hoisted(() => ({
  stub: (name: string) => ({ default: { name, render: () => null } }),
}))
vi.mock('../../../src/pages/login/modules/PwdLogin.vue', () => stub('PwdLogin'))
vi.mock('../../../src/pages/login/modules/CodeLogin.vue', () => stub('CodeLogin'))
vi.mock('../../../src/pages/login/modules/Register.vue', () => stub('Register'))
vi.mock('../../../src/pages/login/modules/ResetPwd.vue', () => stub('ResetPwd'))
vi.mock('../../../src/pages/login/modules/BindWechat.vue', () => stub('BindWechat'))
vi.mock('../../../src/pages/login/modules/TwoFactorChallenge.vue', () => stub('TwoFactorChallenge'))
vi.mock('../../../src/pages/login/modules/PendingActions.vue', () => stub('PendingActions'))

// The shell is covered by its own tests; here it only has to echo the prop.
vi.mock('../../../src/components/pages/TLoginPage.vue', () => ({
  default: defineComponent({
    name: 'TLoginPage',
    props: { sessionEndReason: { type: String, default: null }, module: { type: String, default: '' } },
    render() {
      return h('div', { 'data-test': 'shell', 'data-reason': this.sessionEndReason ?? '' })
    },
  }),
}))

import LoginView from '../../../src/pages/login/LoginView.vue'

function reasonOnShell(): string {
  return mount(LoginView).find('[data-test="shell"]').attributes('data-reason') ?? ''
}

describe('LoginView - session-end notice', () => {
  beforeEach(() => {
    runtimeMock.current = null
  })

  it('★ forwards "security" from the wired runtime to the shell', () => {
    runtimeMock.current = { auth: { sessionEndReason: 'security' } }
    expect(reasonOnShell()).toBe('security')
  })

  it('forwards "expired"', () => {
    runtimeMock.current = { auth: { sessionEndReason: 'expired' } }
    expect(reasonOnShell()).toBe('expired')
  })

  it('passes nothing on a plain visit', () => {
    runtimeMock.current = { auth: { sessionEndReason: null } }
    expect(reasonOnShell()).toBe('')
  })

  it('tolerates a host that passed `client` instead of `runtime` (no runtime provided)', () => {
    runtimeMock.current = null
    expect(reasonOnShell()).toBe('')
  })

  it('snapshots the reason at mount so a later clearAuth() cannot blank the notice mid-read', async () => {
    const auth = { sessionEndReason: 'security' as string | null }
    runtimeMock.current = { auth }
    const wrapper = mount(LoginView)
    auth.sessionEndReason = null
    await wrapper.vm.$nextTick()
    expect(wrapper.find('[data-test="shell"]').attributes('data-reason')).toBe('security')
  })
})
