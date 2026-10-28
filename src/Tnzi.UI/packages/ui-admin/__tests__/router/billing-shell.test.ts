/**
 * The shell's billing behaviour (`defineAdminApp({ billing })`), mounted
 * through the real `AdminShellRoot` with the chrome stubbed out.
 *
 * The billing host and its screens are real; the billing bridge and the
 * HttpClient are fakes, so the tests can drive the standing and fire a gate
 * refusal through the client's result listener.
 */
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { reactive } from 'vue'
import type { ApiResult } from '@tnzi/core'
import type { BillingStandingDto } from '@tnzi/core/services/payment'
import { ADMIN_BILLING_CONFIG_KEY, type ResolvedAdminBillingConfig } from '../../src/plugin/billing-config'
import { useAdminAuthStore } from '../../src/stores/useAdminAuthStore'

type ResultListener = (result: ApiResult<unknown>, context: { method: string; url: string }) => void

const listeners = new Set<ResultListener>()
const fakeClient = {
  get: vi.fn(async () => ({ succeeded: true, code: 200, data: null })),
  post: vi.fn(async () => ({ succeeded: true, code: 200, data: null })),
  getAccessToken: () => 'token',
  addResultListener: (l: ResultListener) => {
    listeners.add(l)
    return () => listeners.delete(l)
  },
}

const getStanding = vi.fn<(productCode?: string) => Promise<BillingStandingDto>>()
const payNow = vi.fn()

vi.mock('../../src/plugin/client', () => ({
  useAdminClient: () => fakeClient,
}))

vi.mock('../../src/services/bridges/billing-bridge', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../src/services/bridges/billing-bridge')>()
  return {
    ...actual,
    createBillingBridge: () => ({
      getStanding: (p?: string) => getStanding(p),
      getAccount: vi.fn(async () => ({ accountType: 'Installation', accountId: 'main', canManage: true, billingEmails: [] })),
      listSubscriptions: vi.fn(async () => [
        {
          id: 'sub-1',
          subscriptionNo: 'S-1',
          planName: 'Pro',
          status: 'Suspended',
          originalPrice: 99,
          currency: 'USD',
          hasPaymentMethod: true,
          paymentMethodBrand: 'visa',
          paymentMethodLast4: '4242',
        },
      ]),
      listPaymentMethods: vi.fn(async () => []),
      payNow,
      getClientConfig: vi.fn(async () => ({ channelCode: 'Stripe', publishableKey: 'pk_test' })),
    }),
  }
})

vi.mock('vue-router', () => ({
  useRoute: () => reactive({ name: 'dashboard', fullPath: '/admin', path: '/admin', query: {}, params: {}, meta: {} }),
  useRouter: () => ({
    push: vi.fn(async () => undefined),
    replace: vi.fn(async () => undefined),
    resolve: () => ({ path: '/admin/user-center', meta: {} }),
    hasRoute: () => false,
    currentRoute: { value: { name: 'dashboard' } },
  }),
}))

// Shell chrome with side effects of its own (theme, realtime hub, presence,
// chat probe) is not what these tests are about.
vi.mock('../../src/headless/useGlobalTheme', () => ({ useGlobalTheme: () => ({ load: vi.fn(async () => undefined) }) }))
const settingsHub = vi.hoisted(() => ({ start: vi.fn(async () => undefined), stop: vi.fn(async () => undefined) }))
vi.mock('../../src/headless/useSettingsRealtime', () => ({
  useSettingsRealtime: () => settingsHub,
}))
vi.mock('../../src/headless/usePresenceActivity', () => ({
  usePresenceActivity: () => ({ loadConfig: vi.fn(async () => undefined), start: vi.fn(), stop: vi.fn() }),
}))
vi.mock('../../src/router/chat-config-probe', () => ({ useChatConfigProbe: vi.fn() }))

// The Stripe element itself: loading it is the form's test, not this one.
vi.mock('../../src/pages/billing/TStripePaymentForm.vue', () => ({
  default: { name: 'TStripePaymentForm', template: '<div class="stripe-form" />' },
}))

const stubs = {
  TAdminShell: { template: '<div class="shell"><slot /></div>' },
  TAdminRouterView: { template: '<div class="router-view" />' },
  TAdminAutoBreadcrumb: true,
  TAdminUserAvatar: true,
  TChatHost: true,
  TThemeDrawer: true,
  TModalShell: { props: ['show'], template: '<div v-if="show" class="modal"><slot /><slot name="footer" /></div>' },
}

function standing(over: Partial<BillingStandingDto>): BillingStandingDto {
  return {
    standing: 'Good' as BillingStandingDto['standing'],
    canView: true,
    canManage: false,
    gateEnabled: true,
    isExempt: false,
    ...over,
  }
}

function billingConfig(): ResolvedAdminBillingConfig {
  return { productCode: 'crm', settingsSection: 'billing-subscription', state: reactive({ view: 'normal' as const }) }
}

/** Let the lazy billing host load and its first read land. */
async function settle(): Promise<void> {
  for (let i = 0; i < 3; i++) {
    await vi.dynamicImportSettled()
    await flushPromises()
  }
}

async function mountShell(config: ResolvedAdminBillingConfig | null) {
  const { default: AdminShellRoot } = await import('../../src/router/AdminShellRoot.vue')
  const wrapper = mount(AdminShellRoot, {
    global: {
      stubs,
      provide: config ? { [ADMIN_BILLING_CONFIG_KEY as symbol]: config } : {},
    },
  })
  await settle()
  return wrapper
}

function signIn(): void {
  const auth = useAdminAuthStore()
  auth.setToken('token')
  auth.setUserInfo({ id: 'u1', username: 'manager', permissions: [] } as never)
}

describe('AdminShellRoot billing', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    listeners.clear()
    getStanding.mockReset()
    payNow.mockReset()
    signIn()
  })

  it('makes no billing request and mounts nothing when the option is absent', async () => {
    const wrapper = await mountShell(null)
    expect(getStanding).not.toHaveBeenCalled()
    expect(listeners.size).toBe(0)
    expect(wrapper.find('.router-view').exists()).toBe(true)
    expect(wrapper.find('[data-testid="billing-banner"]').exists()).toBe(false)
  })

  it('renders the normal shell for a good standing, after reading it once for the product', async () => {
    getStanding.mockResolvedValue(standing({ standing: 'Good' as never, canManage: true }))
    const wrapper = await mountShell(billingConfig())
    expect(getStanding).toHaveBeenCalledTimes(1)
    expect(getStanding).toHaveBeenCalledWith('crm')
    expect(wrapper.find('.router-view').exists()).toBe(true)
    expect(wrapper.find('[data-testid="billing-banner"]').exists()).toBe(false)
  })

  it('shows the banner above the pages to a billing manager while a payment is overdue', async () => {
    getStanding.mockResolvedValue(standing({ standing: 'Warning' as never, canManage: true, subscriptionId: 'sub-1' }))
    const wrapper = await mountShell(billingConfig())
    expect(wrapper.find('[data-testid="billing-banner"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="billing-banner-pay"]').exists()).toBe(true)
    expect(wrapper.find('.router-view').exists()).toBe(true)
  })

  it('shows nothing to a user who cannot manage billing while a payment is overdue', async () => {
    getStanding.mockResolvedValue(standing({ standing: 'Warning' as never, canManage: false }))
    const wrapper = await mountShell(billingConfig())
    expect(wrapper.find('[data-testid="billing-banner"]').exists()).toBe(false)
    expect(wrapper.find('.router-view').exists()).toBe(true)
  })

  it('replaces the pages with the payment screen for a suspended billing manager', async () => {
    getStanding.mockResolvedValue(standing({ standing: 'Suspended' as never, canManage: true, subscriptionId: 'sub-1' }))
    const wrapper = await mountShell(billingConfig())
    expect(wrapper.find('.router-view').exists()).toBe(false)
    expect(wrapper.find('[data-testid="billing-suspended"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="billing-pay-now"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="billing-sign-out"]').exists()).toBe(false)
  })

  it('tells a suspended member to contact the administrators and offers sign out only', async () => {
    getStanding.mockResolvedValue(standing({ standing: 'Suspended' as never, canManage: false, canView: false }))
    const wrapper = await mountShell(billingConfig())
    expect(wrapper.find('.router-view').exists()).toBe(false)
    expect(wrapper.find('[data-testid="billing-suspended-text"]').text()).toContain('Contact your')
    expect(wrapper.find('[data-testid="billing-sign-out"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="billing-pay-now"]').exists()).toBe(false)
  })

  it('does not suspend a user the gate does not apply to', async () => {
    getStanding.mockResolvedValue(standing({ standing: 'Suspended' as never, canManage: true, isExempt: true }))
    const wrapper = await mountShell(billingConfig())
    expect(wrapper.find('.router-view').exists()).toBe(true)
    expect(wrapper.find('[data-testid="billing-suspended"]').exists()).toBe(false)
  })

  it('re-reads the standing when any request is refused by the billing gate, and takes over the shell', async () => {
    getStanding.mockResolvedValueOnce(standing({ standing: 'Good' as never }))
    const wrapper = await mountShell(billingConfig())
    expect(wrapper.find('.router-view').exists()).toBe(true)

    getStanding.mockResolvedValue(standing({ standing: 'Suspended' as never, canManage: false }))
    for (const l of listeners) {
      l({ succeeded: false, code: 402, errorCode: 'SUBSCRIPTION_SUSPENDED', message: 'Suspended' } as never, { method: 'GET', url: '/x' })
    }
    await flushPromises()

    expect(getStanding).toHaveBeenCalledTimes(2)
    expect(wrapper.find('.router-view').exists()).toBe(false)
    expect(wrapper.find('[data-testid="billing-suspended"]').exists()).toBe(true)
  })

  it('ignores other failures', async () => {
    getStanding.mockResolvedValue(standing({ standing: 'Good' as never }))
    await mountShell(billingConfig())
    for (const l of listeners) {
      l({ succeeded: false, code: 402, errorCode: 'CARD_DECLINED' } as never, { method: 'POST', url: '/x' })
      l({ succeeded: false, code: 500, message: 'boom' } as never, { method: 'GET', url: '/y' })
    }
    await flushPromises()
    expect(getStanding).toHaveBeenCalledTimes(1)
  })

  it('renders the pages again without a reload once a payment restores the service', async () => {
    getStanding.mockResolvedValue(standing({ standing: 'Suspended' as never, canManage: true, subscriptionId: 'sub-1' }))
    payNow.mockResolvedValue({ subscriptionId: 'sub-1', amount: 0, taxAmount: 0, currency: 'USD', channelCode: 'Stripe', settledWithoutPayment: true })
    const wrapper = await mountShell(billingConfig())
    expect(wrapper.find('[data-testid="billing-suspended"]').exists()).toBe(true)

    getStanding.mockResolvedValue(standing({ standing: 'Good' as never, canManage: true }))
    await wrapper.find('[data-testid="billing-pay-now"]').trigger('click')
    await settle()

    expect(payNow).toHaveBeenCalledWith('sub-1')
    expect(wrapper.find('[data-testid="billing-suspended"]').exists()).toBe(false)
    expect(wrapper.find('.router-view').exists()).toBe(true)
  })

  it('keeps the settings hub down while suspended and starts it again on recovery', async () => {
    getStanding.mockResolvedValue(standing({ standing: 'Good' as never, canManage: true }))
    const healthy = await mountShell(billingConfig())
    expect(settingsHub.start).toHaveBeenCalled()
    healthy.unmount()

    settingsHub.start.mockClear()
    settingsHub.stop.mockClear()
    getStanding.mockResolvedValue(standing({ standing: 'Suspended' as never, canManage: true, subscriptionId: 'sub-1' }))
    payNow.mockResolvedValue({ subscriptionId: 'sub-1', amount: 0, taxAmount: 0, currency: 'USD', channelCode: 'Stripe', settledWithoutPayment: true })
    const wrapper = await mountShell(billingConfig())
    expect(wrapper.find('[data-testid="billing-suspended"]').exists()).toBe(true)
    // Every negotiation would be refused with 402: the hub is stopped, not left retrying
    expect(settingsHub.stop).toHaveBeenCalled()
    const startsWhileSuspended = settingsHub.start.mock.invocationCallOrder.filter((n) => n > Math.max(...settingsHub.stop.mock.invocationCallOrder))
    expect(startsWhileSuspended).toEqual([])

    getStanding.mockResolvedValue(standing({ standing: 'Good' as never, canManage: true }))
    await wrapper.find('[data-testid="billing-pay-now"]').trigger('click')
    await settle()

    expect(wrapper.find('.router-view').exists()).toBe(true)
    expect(settingsHub.start.mock.invocationCallOrder.some((n) => n > Math.max(...settingsHub.stop.mock.invocationCallOrder))).toBe(true)
  })

  it('forgets the standing on sign-out', async () => {
    getStanding.mockResolvedValue(standing({ standing: 'Suspended' as never, canManage: false }))
    const config = billingConfig()
    const wrapper = await mountShell(config)
    expect(config.state.view).toBe('suspended')

    useAdminAuthStore().logout()
    await flushPromises()

    expect(config.state.view).toBe('normal')
    expect(wrapper.find('[data-testid="billing-suspended"]').exists()).toBe(false)
  })
})
