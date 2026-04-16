import { describe, it, expect, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import TGlobalSearch from '../../../src/components/layout/TGlobalSearch.vue'
import {
  useAdminRouteStore,
  type AdminRouteRecord,
} from '../../../src/stores/useAdminRouteStore'

const stubs = {
  Modal: {
    name: 'Modal',
    props: ['show'],
    emits: ['update:show'],
    template: '<div v-if="show" class="n-modal-stub"><slot /></div>',
  },
  Input: {
    name: 'Input',
    props: ['value', 'placeholder'],
    emits: ['update:value'],
    template:
      '<input class="n-input-stub" :value="value" :placeholder="placeholder" @input="$emit(\'update:value\', $event.target.value)" />',
  },
  Icon: {
    name: 'Icon',
    template: '<i class="n-icon-stub"><slot /></i>',
  },
}

function seedRoutes(): AdminRouteRecord[] {
  return [
    { name: 'users', path: '/identity/users', meta: { title: 'User Management', order: 1 } },
    { name: 'roles', path: '/identity/roles', meta: { title: 'Role Management', order: 2 } },
    { name: 'orders', path: '/payment/orders', meta: { title: 'Order List', order: 3 } },
  ]
}

describe('TGlobalSearch', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    useAdminRouteStore().setConstantRoutes(seedRoutes())
  })

  it('is hidden by default', () => {
    const wrapper = mount(TGlobalSearch, {
      props: { show: false },
      global: { stubs },
    })
    expect(wrapper.find('.n-modal-stub').exists()).toBe(false)
  })

  it('opens via v-model:show', () => {
    const wrapper = mount(TGlobalSearch, {
      props: { show: true },
      global: { stubs },
    })
    expect(wrapper.find('.n-modal-stub').exists()).toBe(true)
    expect(wrapper.find('.n-input-stub').exists()).toBe(true)
  })

  it('filters results by substring (case-insensitive)', async () => {
    const wrapper = mount(TGlobalSearch, {
      props: { show: true },
      global: { stubs },
    })
    await wrapper.find('.n-input-stub').setValue('user')
    const items = wrapper.findAll('.t-global-search__item')
    expect(items).toHaveLength(1)
    expect(items[0].text()).toContain('User Management')
  })

  it('shows all items when query is empty', () => {
    const wrapper = mount(TGlobalSearch, {
      props: { show: true },
      global: { stubs },
    })
    const items = wrapper.findAll('.t-global-search__item')
    expect(items).toHaveLength(3)
  })

  it('arrow keys change highlighted index', async () => {
    const wrapper = mount(TGlobalSearch, {
      props: { show: true },
      global: { stubs },
    })
    const vm = wrapper.vm as unknown as { highlighted: number }
    expect(vm.highlighted).toBe(0)
    const input = wrapper.find('.n-input-stub')
    await input.trigger('keydown', { key: 'ArrowDown' })
    expect(vm.highlighted).toBe(1)
    await input.trigger('keydown', { key: 'ArrowDown' })
    expect(vm.highlighted).toBe(2)
    await input.trigger('keydown', { key: 'ArrowUp' })
    expect(vm.highlighted).toBe(1)
  })

  it('Enter emits select with highlighted item', async () => {
    const wrapper = mount(TGlobalSearch, {
      props: { show: true },
      global: { stubs },
    })
    const input = wrapper.find('.n-input-stub')
    await input.trigger('keydown', { key: 'ArrowDown' })
    await input.trigger('keydown', { key: 'Enter' })
    const selectEvents = wrapper.emitted('select')
    expect(selectEvents).toBeTruthy()
    expect(selectEvents![0][0]).toMatchObject({ path: '/identity/roles' })
  })

  it('Esc closes the modal', async () => {
    const wrapper = mount(TGlobalSearch, {
      props: { show: true },
      global: { stubs },
    })
    await wrapper.find('.n-input-stub').trigger('keydown', { key: 'Escape' })
    const events = wrapper.emitted('update:show')
    expect(events).toBeTruthy()
    expect(events![events!.length - 1]).toEqual([false])
  })

  it('click on result emits select', async () => {
    const wrapper = mount(TGlobalSearch, {
      props: { show: true },
      global: { stubs },
    })
    const items = wrapper.findAll('.t-global-search__item')
    await items[2].trigger('click')
    const selectEvents = wrapper.emitted('select')
    expect(selectEvents).toBeTruthy()
    expect(selectEvents![0][0]).toMatchObject({ path: '/payment/orders' })
  })

  it('custom matcher prop overrides default', async () => {
    const matcher = (_q: string, item: { label: string }) => item.label.endsWith('List')
    const wrapper = mount(TGlobalSearch, {
      props: { show: true, matcher },
      global: { stubs },
    })
    await wrapper.find('.n-input-stub').setValue('anything')
    const items = wrapper.findAll('.t-global-search__item')
    expect(items).toHaveLength(1)
    expect(items[0].text()).toContain('Order List')
  })
})
