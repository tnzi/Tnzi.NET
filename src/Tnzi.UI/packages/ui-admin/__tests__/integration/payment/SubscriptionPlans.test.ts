import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { useAdminAuthStore } from '../../../src/stores/useAdminAuthStore'

/**
 * Subscription plans page - card grid (TCardPage): a card click opens the plan,
 * the Active switch sits in the dialog footer, cards drag to reorder. The bridge is mocked.
 */
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

const plan = (id: string, over: Record<string, unknown> = {}) => ({
  id,
  planCode: `code-${id}`,
  productCode: null,
  planName: `Plan ${id}`,
  description: null,
  price: 10,
  currency: 'USD',
  cycleType: 'Month',
  cycleValue: 1,
  trialDays: 0,
  allowTrial: false,
  trialDiscount: null,
  sortOrder: 0,
  isActive: true,
  ...over,
})

const rows = [
  plan('p1', { planName: 'Basic', price: 9, sortOrder: 0 }),
  plan('p2', { planName: 'Pro', price: 29, sortOrder: 1, allowTrial: true, trialDays: 14, trialDiscount: 5 }),
  plan('p3', { planName: 'Legacy', sortOrder: 2, isActive: false }),
]

const bridgeMock = {
  getDefaultCurrency: vi.fn(async () => 'USD'),
  fetch: vi.fn(async () => ({ items: rows, totalCount: rows.length, pageIndex: 1, pageSize: 100 })),
  create: vi.fn(async (d: unknown) => ({ ...(d as object), id: 'p4' })),
  update: vi.fn(async (id: string, d: unknown) => ({ ...(d as object), id })),
  deactivate: vi.fn(async () => undefined),
  reorder: vi.fn(async () => undefined),
}

vi.mock('../../../src/services/bridges/subscription-plan-bridge', () => ({
  createSubscriptionPlanBridge: () => bridgeMock,
}))

const toast = { success: vi.fn(), error: vi.fn(), warning: vi.fn(), info: vi.fn() }
vi.mock('../../../src/pages/_shared/safe-message', () => ({ useSafeMessage: () => toast }))

import SubscriptionPlans from '../../../src/pages/payment/SubscriptionPlans.vue'

const stubs = {
  DataTable: { template: '<div />' },
  Pagination: { template: '<div />' },
  Input: { props: ['value'], template: '<input :value="value" />' },
  Button: { template: '<button @click="$emit(\'click\')"><slot /></button>' },
  Modal: { props: ['show'], template: '<div v-if="show" class="n-modal-stub"><slot /><slot name="footer" /></div>' },
  Popover: { template: '<div><slot name="trigger" /><slot /></div>' },
  Popconfirm: { template: '<div><slot name="trigger" /><slot /></div>' },
  Form: { template: '<form><slot /></form>' },
  FormItem: { template: '<div><slot /></div>' },
  VueDraggable: { template: '<div><slot /></div>' },
}

type PageVm = {
  onReorder: (e: unknown) => Promise<void>
}

function crudOf(wrapper: ReturnType<typeof mount>) {
  return wrapper.findComponent({ name: 'TCardPage' }).props('state') as {
    items: { value: unknown[] }
    canCreate: boolean
    canUpdate: boolean
    canDelete: boolean
    openCreate: () => void
    openEdit: (row: unknown) => void
    formModal: { visible: { value: boolean }; mode: { value: string }; formData: { value: Record<string, unknown> } }
    submit: () => Promise<void>
  }
}

describe('SubscriptionPlans page', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    for (const fn of Object.values(bridgeMock)) fn.mockClear()
    for (const fn of Object.values(toast)) fn.mockClear()
  })

  it('renders one card per plan with its price and cadence', async () => {
    const wrapper = mount(SubscriptionPlans, { global: { stubs } })
    await flushPromises()
    expect(bridgeMock.fetch).toHaveBeenCalledTimes(1)
    expect(wrapper.findAll('.plan-card')).toHaveLength(3)
    expect(wrapper.text()).toContain('Basic')
    expect(wrapper.text()).toContain('$29.00')
    expect(wrapper.text()).toContain('per month')
    expect(wrapper.text()).toContain('14-day trial')
    expect(wrapper.find('.plan-card--off').text()).toContain('Legacy')
  })

  it('a viewer without write codes gets no create / edit / deactivate and cannot drag', async () => {
    useAdminAuthStore().setUserInfo({ id: 'r', username: 'reader', roles: [], permissions: ['payment.subscription.view'] })
    const wrapper = mount(SubscriptionPlans, { global: { stubs } })
    await flushPromises()
    const crud = crudOf(wrapper)
    expect(crud.canCreate).toBe(false)
    expect(crud.canUpdate).toBe(false)
    expect(crud.canDelete).toBe(false)
    expect(wrapper.findComponent({ name: 'TCardPage' }).props('draggable')).toBe(false)
    expect(wrapper.text()).not.toContain('Deactivate')
  })

  it('each write affordance follows its own method-level code', async () => {
    useAdminAuthStore().setUserInfo({
      id: 'u', username: 'pricing', roles: [],
      permissions: ['payment.subscription.view', 'payment.subscription.update'],
    })
    const wrapper = mount(SubscriptionPlans, { global: { stubs } })
    await flushPromises()
    const crud = crudOf(wrapper)
    expect(crud.canUpdate).toBe(true)
    expect(crud.canCreate).toBe(false)
    expect(crud.canDelete).toBe(false)
    expect(wrapper.findComponent({ name: 'TCardPage' }).props('draggable')).toBe(true)
  })

  it('a price edit sends the whole row back, sort order and active flag included', async () => {
    const wrapper = mount(SubscriptionPlans, { global: { stubs } })
    await flushPromises()
    const crud = crudOf(wrapper)
    crud.openEdit(rows[1])
    // The form only carries what the operator touched; the PUT overwrites every field.
    crud.formModal.formData.value = { id: 'p2', price: 39 }
    await crud.submit()
    await flushPromises()
    expect(bridgeMock.update).toHaveBeenCalledWith('p2', expect.objectContaining({
      id: 'p2', price: 39, planCode: 'code-p2', sortOrder: 1, isActive: true, trialDiscount: 5,
    }))
  })

  it('a new plan lands at the end of the catalogue', async () => {
    const wrapper = mount(SubscriptionPlans, { global: { stubs } })
    await flushPromises()
    const crud = crudOf(wrapper)
    crud.openCreate()
    crud.formModal.formData.value = { ...crud.formModal.formData.value, planCode: ' team ', planName: 'Team', price: 99, currency: 'cad' }
    await crud.submit()
    await flushPromises()
    expect(bridgeMock.create).toHaveBeenCalledWith(expect.objectContaining({
      planCode: 'team', currency: 'CAD', sortOrder: 3, isActive: true, cycleType: 'Month', cycleValue: 1,
    }))
  })

  it('a duplicate plan code is reported as a sentence, not as the server code', async () => {
    bridgeMock.create.mockRejectedValueOnce(Object.assign(new Error('SUBSCRIPTION_PLAN_CODE_ALREADY_EXISTS'), { code: 409 }))
    const wrapper = mount(SubscriptionPlans, { global: { stubs } })
    await flushPromises()
    const crud = crudOf(wrapper)
    crud.openCreate()
    crud.formModal.formData.value = { ...crud.formModal.formData.value, planCode: 'code-p1', planName: 'Dup' }
    await crud.submit().catch(() => undefined)
    await flushPromises()
    expect(toast.error).toHaveBeenCalledWith(expect.stringContaining('already exists'))
    expect(toast.error).not.toHaveBeenCalledWith('SUBSCRIPTION_PLAN_CODE_ALREADY_EXISTS')
  })

  it('a refused reorder puts the cards back and says why', async () => {
    bridgeMock.reorder.mockRejectedValueOnce(new Error('Reorder rejected by the server'))
    const wrapper = mount(SubscriptionPlans, { global: { stubs } })
    await flushPromises()
    const revert = vi.fn()
    await (wrapper.vm as unknown as PageVm).onReorder({ items: [rows[2], rows[0], rows[1]], revert })
    expect(bridgeMock.reorder).toHaveBeenCalledWith(['p3', 'p1', 'p2'])
    expect(revert).toHaveBeenCalledTimes(1)
    expect(toast.error).toHaveBeenCalledWith('Reorder rejected by the server')
  })

  it('clicking a card opens the plan to edit, with no separate edit or deactivate action', async () => {
    const wrapper = mount(SubscriptionPlans, { global: { stubs } })
    await flushPromises()
    expect(wrapper.find('.t-row-actions').exists()).toBe(false)
    expect(wrapper.findAll('.plan-card button')).toHaveLength(0)
    expect(wrapper.text()).not.toContain('Deactivate')

    await wrapper.find('[data-testid="plan-card-code-p2"]').trigger('click')
    await flushPromises()
    const crud = crudOf(wrapper)
    expect(crud.formModal.visible.value).toBe(true)
    expect(crud.formModal.mode.value).toBe('edit')
    expect(crud.formModal.formData.value.id).toBe('p2')
  })

  it('a viewer clicking a card opens it read-only', async () => {
    useAdminAuthStore().setUserInfo({ id: 'r', username: 'reader', roles: [], permissions: ['payment.subscription.view'] })
    const wrapper = mount(SubscriptionPlans, { global: { stubs } })
    await flushPromises()
    await wrapper.find('[data-testid="plan-card-code-p1"]').trigger('click')
    await flushPromises()
    expect(crudOf(wrapper).formModal.mode.value).toBe('view')
    expect(wrapper.find('.t-form-modal__switch').exists()).toBe(false)
  })

  it('the Active switch sits in the dialog footer when editing and is absent on create', async () => {
    const wrapper = mount(SubscriptionPlans, { global: { stubs } })
    await flushPromises()
    const crud = crudOf(wrapper)

    crud.openCreate()
    await flushPromises()
    expect(wrapper.find('.t-form-modal__switch').exists()).toBe(false)
    crud.formModal.visible.value = false
    await flushPromises()

    await wrapper.find('[data-testid="plan-card-code-p1"]').trigger('click')
    await flushPromises()
    const footer = wrapper.find('.t-form-modal__footer')
    expect(footer.element.firstElementChild?.classList.contains('t-form-modal__switch')).toBe(true)
    expect(footer.find('.t-form-modal__switch').text()).toContain('Active')
  })

  it('switching a plan off sends the whole row with isActive false', async () => {
    const wrapper = mount(SubscriptionPlans, { global: { stubs } })
    await flushPromises()
    await wrapper.find('[data-testid="plan-card-code-p2"]').trigger('click')
    await flushPromises()
    await wrapper.find('.t-form-modal__switch-label').trigger('click')
    const crud = crudOf(wrapper)
    expect(crud.formModal.formData.value.isActive).toBe(false)
    await crud.submit()
    await flushPromises()
    expect(bridgeMock.update).toHaveBeenCalledWith('p2', expect.objectContaining({ isActive: false, sortOrder: 1, planName: 'Pro' }))
  })

  it('a refused deactivation says the plan cannot be deactivated', async () => {
    bridgeMock.update.mockRejectedValueOnce(new Error('SUBSCRIPTION_PLAN_HAS_ACTIVE_SUBSCRIPTIONS'))
    const wrapper = mount(SubscriptionPlans, { global: { stubs } })
    await flushPromises()
    const crud = crudOf(wrapper)
    crud.openEdit(rows[0])
    crud.formModal.formData.value = { ...rows[0], isActive: false }
    await crud.submit().catch(() => undefined)
    await flushPromises()
    expect(toast.error).toHaveBeenCalledWith('This plan still has live subscriptions and cannot be deactivated.')
  })

  it('a refused terms change on an active plan says the terms are locked', async () => {
    bridgeMock.update.mockRejectedValueOnce(new Error('SUBSCRIPTION_PLAN_HAS_ACTIVE_SUBSCRIPTIONS'))
    const wrapper = mount(SubscriptionPlans, { global: { stubs } })
    await flushPromises()
    const crud = crudOf(wrapper)
    crud.openEdit(rows[0])
    crud.formModal.formData.value = { ...rows[0], cycleType: 'Year' }
    await crud.submit().catch(() => undefined)
    await flushPromises()
    expect(toast.error).toHaveBeenCalledWith(expect.stringContaining('currency and billing cycle cannot change'))
  })
})

describe('subscription-plan-config', () => {
  it('describes the cadence the way a reader says it', async () => {
    const { describeCycle } = await import('../../../src/pages/payment/subscription-plan-config')
    const t = (k: string, p?: Record<string, unknown>) => (p ? `${k}:${String(p.n)}` : k)
    expect(describeCycle({ cycleType: 'Month', cycleValue: 1 } as never, t)).toBe('cycle.monthOne')
    expect(describeCycle({ cycleType: 'Month', cycleValue: 3 } as never, t)).toBe('cycle.monthMany:3')
    expect(describeCycle({ cycleType: 'OneTime', cycleValue: 0 } as never, t)).toBe('cycle.oneTime')
  })

  it('drops the trial discount when no trial is offered', async () => {
    const { toPlanWrite } = await import('../../../src/pages/payment/subscription-plan-config')
    const dto = toPlanWrite({ allowTrial: false, trialDays: 14, trialDiscount: 5, price: 10 } as never)
    expect(dto.trialDiscount).toBeNull()
    expect(dto.trialDays).toBe(0)
  })

  it('sends included storage in bytes, and clears the overage price when nothing is included', async () => {
    const { toPlanWrite } = await import('../../../src/pages/payment/subscription-plan-config')
    const gb = 1073741824
    const metered = toPlanWrite({ cycleType: 'Month', cycleValue: 1, includedStorageBytes: 250 * gb, storageOveragePricePerGbMonth: 0.2 } as never)
    expect(metered.includedStorageBytes).toBe(250 * gb)
    expect(metered.storageOveragePricePerGbMonth).toBe(0.2)

    const unlimited = toPlanWrite({ cycleType: 'Month', cycleValue: 1, includedStorageBytes: null, storageOveragePricePerGbMonth: 0.2 } as never)
    expect(unlimited.includedStorageBytes).toBeNull()
    expect(unlimited.storageOveragePricePerGbMonth).toBeNull()

    // A one-time plan has no renewal to bill an overage with
    const oneTime = toPlanWrite({ cycleType: 'OneTime', cycleValue: 1, includedStorageBytes: 250 * gb, storageOveragePricePerGbMonth: 0.2 } as never)
    expect(oneTime.includedStorageBytes).toBeNull()
  })

  it('keeps the form flat: no sections, and Active is not a form field', async () => {
    const { planFormSchema, planActiveSwitch } = await import('../../../src/pages/payment/subscription-plan-config')
    expect(planFormSchema.filter((f) => f.section).map((f) => f.key)).toEqual([])
    expect(planFormSchema.some((f) => f.key === 'isActive')).toBe(false)
    expect(planActiveSwitch).toMatchObject({ key: 'isActive' })
    expect(planActiveSwitch.modes).toBeUndefined()
  })

  it('puts every field explanation behind an info icon, not under the field', async () => {
    const { planFormSchema } = await import('../../../src/pages/payment/subscription-plan-config')
    const withHints = planFormSchema.filter((f) => f.hintKey || f.hint)
    expect(withHints.length).toBeGreaterThan(0)
    expect(withHints.filter((f) => f.hintMode !== 'popover').map((f) => f.key)).toEqual([])
  })

  it('edits included storage in GB and stores bytes', async () => {
    const { h } = await import('vue')
    const { mount: mountField } = await import('@vue/test-utils')
    const wrapper = mount(SubscriptionPlans, { global: { stubs } })
    await flushPromises()
    const { STORAGE_GB_TYPE } = await import('../../../src/pages/payment/subscription-plan-config')
    const model: Record<string, unknown> = { includedStorageBytes: 250 * 1073741824 }
    const page = wrapper.vm as unknown as { $: { setupState: { fieldRenderers: Record<string, (ctx: unknown) => unknown> } } }
    const render = page.$.setupState.fieldRenderers[STORAGE_GB_TYPE]
    const field = mountField({
      render: () =>
        render({ item: { key: 'includedStorageBytes' }, value: model.includedStorageBytes, readonly: false, onUpdate: (v: unknown) => (model.includedStorageBytes = v), translate: (k: string) => k }) as ReturnType<typeof h>,
    })
    const input = field.findComponent({ name: 'InputNumber' })
    expect(input.props('value')).toBe(250)
    input.vm.$emit('update:value', 300)
    expect(model.includedStorageBytes).toBe(300 * 1073741824)
    input.vm.$emit('update:value', null)
    expect(model.includedStorageBytes).toBeNull()
  })

  it('maps the live-subscriptions refusal by operation', async () => {
    const { describePlanError } = await import('../../../src/pages/payment/subscription-plan-config')
    const t = (k: string) => k
    const err = new Error('SUBSCRIPTION_PLAN_HAS_ACTIVE_SUBSCRIPTIONS')
    expect(describePlanError(err, 'update', t)).toBe('errors.hasSubscribersTerms')
    expect(describePlanError(err, 'delete', t)).toBe('errors.hasSubscribersDeactivate')
    expect(describePlanError(err, 'update', t, true)).toBe('errors.hasSubscribersDeactivate')
    expect(describePlanError(new Error('Something else'), 'update', t)).toBe('Something else')
  })
})
