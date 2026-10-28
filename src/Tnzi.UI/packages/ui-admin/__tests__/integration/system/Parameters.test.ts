import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import Parameters from '../../../src/pages/system/Parameters.vue'

vi.mock('../../../src/plugin/client', () => ({ useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }) }))
// Shared with the tests so a create / update body can be read back.
const settingWrites = vi.hoisted(() => ({
  create: vi.fn(async (d: unknown) => ({ ...(d as object), id: 'new' })),
  update: vi.fn(async (id: string, d: unknown) => ({ id, ...(d as object) })),
}))

vi.mock('../../../src/services/bridges/system-bridge', () => ({
  createSystemBridge: () => ({
    settings: {
      fetch: vi.fn(async () => ({
        items: [
          // valueType is the SettingValueType member name (JsonStringEnumConverter wire shape).
          { id: 'p1', key: 'app.maxUploadSize', value: '10', valueType: 'Integer', group: 'app' },
          { id: 'p2', key: 'app.enableSignup', value: 'true', valueType: 'Boolean', group: 'app' },
        ],
        totalCount: 2,
        pageIndex: 1,
        pageSize: 20,
      })),
      create: settingWrites.create,
      update: settingWrites.update,
      delete: vi.fn(async () => undefined),
    },
    accessLogs: { fetch: vi.fn() },
    scheduledJobs: { fetch: vi.fn(), trigger: vi.fn() },
  }),
}))

const stubs = {
  DataTable: { props: ['data'], template: '<div class="dt" :data-rows="data.length" />' },
  Pagination: { template: '<div />' },
  Input: { props: ['value'], template: '<input />' },
  Button: { template: '<button @click="$emit(\'click\')"><slot /></button>' },
  Modal: { props: ['show'], template: '<div v-if="show"><slot /></div>' },
  Popover: { template: '<div><slot name="trigger" /></div>' },
  Checkbox: { template: '<input type="checkbox" />' },
  Form: { template: '<form><slot /></form>' },
  FormItem: { template: '<div><slot /></div>' },
  // The typed editors report what they were handed and answer with a typed value.
  InputNumber: {
    props: ['value'],
    emits: ['update:value'],
    template: '<input class="num" :data-type="typeof value" :data-value="String(value)" @click="$emit(\'update:value\', 42)" />',
  },
  Switch: {
    props: ['value'],
    emits: ['update:value'],
    template: '<button class="sw" :data-type="typeof value" :data-value="String(value)" @click="$emit(\'update:value\', !value)" />',
  },
  Select: { template: '<select />' },
  DatePicker: { template: '<input type="date" />' },
  VueDraggable: { template: '<div><slot /></div>' },
}

type ParametersVm = {
  crud: {
    openCreate: (seed?: Record<string, unknown>) => void
    openEdit: (row: Record<string, unknown>) => void
    submit: () => Promise<unknown>
  }
}

async function mountPage() {
  const wrapper = mount(Parameters, { global: { stubs } })
  await nextTick()
  await new Promise(r => setTimeout(r, 10))
  return { wrapper, vm: wrapper.vm as unknown as ParametersVm }
}

describe('Parameters page (Phase 3.14)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    settingWrites.create.mockClear()
    settingWrites.update.mockClear()
  })

  it('mounts and fetches parameters on load', async () => {
    const { wrapper } = await mountPage()
    expect(wrapper.find('.dt').exists()).toBe(true)
    expect(wrapper.find('.dt').attributes('data-rows')).toBe('2')
  })

  it('opens form modal when Create button is clicked', async () => {
    const { wrapper } = await mountPage()
    await wrapper.find('.t-crud-page__create').trigger('click')
    expect(wrapper.find('form').exists()).toBe(true)
  })

  // A setting value is a string on the wire (`Value` is a string property).
  // A stored 'true' handed to the switch as a string rendered OFF, and the
  // switch's boolean answer went out as a JSON bool, which the string binder
  // refuses with 400.
  it('edits a Boolean value as a switch over its stored string and saves a string', async () => {
    const { wrapper, vm } = await mountPage()
    vm.crud.openEdit({ id: 'p2', key: 'app.enableSignup', value: 'true', valueType: 'Boolean', group: 'app' })
    await nextTick()

    const sw = wrapper.find('.sw')
    expect(sw.attributes('data-type')).toBe('boolean')
    expect(sw.attributes('data-value')).toBe('true')

    await sw.trigger('click')
    await vm.crud.submit()
    expect(settingWrites.update).toHaveBeenLastCalledWith('p2', expect.objectContaining({ value: 'false' }))
  })

  it('edits an Integer value as a number over its stored string and saves a string', async () => {
    const { wrapper, vm } = await mountPage()
    vm.crud.openEdit({ id: 'p1', key: 'app.maxUploadSize', value: '10', valueType: 'Integer', group: 'app' })
    await nextTick()

    const num = wrapper.find('.num')
    expect(num.attributes('data-type')).toBe('number')
    expect(num.attributes('data-value')).toBe('10')

    await num.trigger('click')
    await vm.crud.submit()
    expect(settingWrites.update).toHaveBeenLastCalledWith('p1', expect.objectContaining({ value: '42' }))
  })

  // The switch shows OFF until it is touched, and a switch is never "missing";
  // a new Boolean parameter left at OFF is created as 'false' rather than
  // without a value (`Value` is required).
  it('creates an untouched Boolean parameter as false', async () => {
    const { vm } = await mountPage()
    vm.crud.openCreate({ key: 'app.newFlag', valueType: 'Boolean' })
    await vm.crud.submit()
    expect(settingWrites.create).toHaveBeenLastCalledWith(expect.objectContaining({ key: 'app.newFlag', value: 'false' }))
  })
})
