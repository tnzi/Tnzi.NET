import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import { useAdminAuthStore } from '../../../src/stores/useAdminAuthStore'
import { resetAllFileUrlResolvers } from '../../../src/services/file-url-resolver'

/**
 * The invitee's page. Everyone who reaches it comes from an email with no
 * account and no session, so what matters: it previews before acting, a dead
 * link reads one way, `completed: false` keeps the link usable, and a completed
 * acceptance that comes with tokens signs the invitee straight in.
 */
const acceptance = vi.hoisted(() => ({
  preview: vi.fn(async () => ({
    userName: 'newhire',
    maskedEmail: 'n***@example.com',
    maskedPhoneNumber: null,
    expiresAt: '2026-09-19T00:00:00Z',
  })),
  accept: vi.fn(async () => ({ completed: true, token: { accessToken: 'at', refreshToken: 'rt', expiresIn: 3600 } })),
}))

const routeQuery = vi.hoisted(() => ({ value: { token: 'invite.token' } as Record<string, string> }))
const routerMock = vi.hoisted(() => ({ push: vi.fn(), replace: vi.fn(), back: vi.fn(), resolve: vi.fn(() => ({ path: '/' })) }))
const runtimeMock = vi.hoisted(() => ({
  value: { auth: { applyTokenSession: vi.fn(async () => undefined) } } as unknown,
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: routeQuery.value, params: {}, path: '/accept-invitation' }),
  useRouter: () => routerMock,
}))
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))
vi.mock('../../../src/plugin/runtime', () => ({
  useAdminRuntime: () => runtimeMock.value,
}))
vi.mock('../../../src/services/file-url-resolver', () => ({
  resetAllFileUrlResolvers: vi.fn(),
}))
const bridgeDeps = vi.hoisted(() => ({ last: undefined as Record<string, unknown> | undefined }))
vi.mock('../../../src/services/bridges/identity-bridge', () => ({
  createIdentityBridge: (deps: Record<string, unknown>) => {
    bridgeDeps.last = deps
    return { invitationAcceptance: acceptance }
  },
}))

const stubs = {
  Spin: { template: '<div><slot /></div>' },
  Button: { template: '<button @click="$emit(\'click\')"><slot /></button>' },
  Form: { template: '<form><slot /></form>', methods: { validate: async () => undefined } },
  FormItem: { template: '<div><slot /></div>' },
  Input: { props: ['value'], template: '<input :value="value" @input="$emit(\'update:value\', $event.target.value)" />' },
}

async function mountPage() {
  const { default: Page } = await import('../../../src/pages/identity/AcceptInvitationPage.vue')
  const wrapper = mount(Page, { global: { stubs } })
  await nextTick()
  await new Promise((r) => setTimeout(r, 10))
  return wrapper
}

async function fillAndSubmit(wrapper: Awaited<ReturnType<typeof mountPage>>) {
  const inputs = wrapper.findAll('input')
  await inputs[0].setValue('Str0ngPassw0rd!')
  await inputs[1].setValue('Str0ngPassw0rd!')
  const submit = wrapper.findAll('button').find((b) => b.text().includes('Activate'))
  await submit!.trigger('click')
  await nextTick()
  await new Promise((r) => setTimeout(r, 10))
}

describe('AcceptInvitationPage', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    routeQuery.value = { token: 'invite.token' }
    runtimeMock.value = { auth: { applyTokenSession: vi.fn(async () => undefined) } }
    acceptance.preview.mockClear()
    acceptance.accept.mockClear()
    acceptance.accept.mockResolvedValue({ completed: true, token: { accessToken: 'at', refreshToken: 'rt', expiresIn: 3600 } } as never)
    routerMock.replace.mockClear()
  })

  it('previews the invitation without consuming the token, masked contact in view', async () => {
    const wrapper = await mountPage()
    expect(acceptance.preview).toHaveBeenCalledWith('invite.token')
    expect(acceptance.accept).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('newhire')
    expect(wrapper.text()).toContain('n***@example.com')
  })

  /**
   * The admin store persists the previous identity (`token`, `userInfo`,
   * `isSuperUser`), and the auth guard skips loading permissions while it
   * holds one. An invitee opening the link in a browser that still carries a
   * prior admin's store (the admin who sent the invitation, testing it) would
   * land on the dashboard as that admin, with every request 403ing.
   */
  it('clears a persisted previous admin identity before applying the invitee session', async () => {
    const store = useAdminAuthStore()
    store.setToken('old-admin-token')
    store.setUserInfo({ id: 'admin-1', username: 'admin' } as never)
    store.setSuperUser(true)

    const wrapper = await mountPage()
    await fillAndSubmit(wrapper)

    expect(store.token).toBe('')
    expect(store.userInfo).toBeNull()
    expect(store.isSuperUser).toBe(false)
    expect(resetAllFileUrlResolvers).toHaveBeenCalled()
    expect(routerMock.replace).toHaveBeenCalledWith({ name: 'dashboard' })
  })

  it('builds the bridge with the runtime delivery mode, so a cookie-mode accept keeps its Set-Cookie', async () => {
    runtimeMock.value = { auth: { applyTokenSession: vi.fn(async () => undefined), cookieDelivery: true } }
    const wrapper = await mountPage()
    await fillAndSubmit(wrapper)
    expect(bridgeDeps.last?.withCredentials).toBe(true)
  })

  it('accepts with the chosen password and applies the issued session, then lands on the dashboard', async () => {
    const wrapper = await mountPage()
    await fillAndSubmit(wrapper)

    expect(acceptance.accept).toHaveBeenCalledWith({ token: 'invite.token', password: 'Str0ngPassw0rd!' })
    const runtime = runtimeMock.value as { auth: { applyTokenSession: ReturnType<typeof vi.fn> } }
    expect(runtime.auth.applyTokenSession).toHaveBeenCalledWith({ accessToken: 'at', refreshToken: 'rt', expiresIn: 3600 })
    expect(routerMock.replace).toHaveBeenCalledWith({ name: 'dashboard' })
  })

  it('without a runtime (host owns the session) it reports success and offers the login page', async () => {
    runtimeMock.value = undefined
    const wrapper = await mountPage()
    await fillAndSubmit(wrapper)

    expect(routerMock.replace).not.toHaveBeenCalledWith({ name: 'dashboard' })
    expect(wrapper.text()).toContain('ready')
  })

  it('completed:false lists the remaining steps and says the same link still works', async () => {
    acceptance.accept.mockResolvedValueOnce({ completed: false, remainingSteps: ['EnrollTotp'] } as never)
    const wrapper = await mountPage()
    await fillAndSubmit(wrapper)

    expect(wrapper.text()).toContain('authenticator app')
    expect(wrapper.text()).toContain('same link')
  })

  it('shows the server refusal instead of a generic line', async () => {
    acceptance.accept.mockRejectedValueOnce(new Error('Password does not meet the policy.'))
    const wrapper = await mountPage()
    await fillAndSubmit(wrapper)

    expect(wrapper.find('[role="alert"]').text()).toContain('Password does not meet the policy.')
  })

  it('says one thing for a dead link', async () => {
    acceptance.preview.mockResolvedValueOnce(null as never)
    const wrapper = await mountPage()
    expect(wrapper.text()).toContain('cannot be used')
    expect(wrapper.findAll('input')).toHaveLength(0)
  })

  it('treats a missing token as a dead link without asking the server', async () => {
    routeQuery.value = {}
    const wrapper = await mountPage()
    expect(acceptance.preview).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('cannot be used')
  })
})
