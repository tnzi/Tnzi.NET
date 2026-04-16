import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import TCrudToolbar from '../../../src/components/crud/TCrudToolbar.vue'

const buttonStub = {
  name: 'Button',
  template: '<button class="btn" @click="$emit(\'click\')"><slot /></button>',
}

const stubs = { Button: buttonStub }

describe('TCrudToolbar', () => {
  it('renders primary slot on the left', () => {
    const wrapper = mount(TCrudToolbar, {
      global: { stubs },
      slots: {
        primary: '<button class="primary">Create</button>',
      },
    })
    const left = wrapper.find('.t-crud-toolbar__left')
    expect(left.exists()).toBe(true)
    expect(left.find('.primary').exists()).toBe(true)
  })

  it('emits refresh/export/import/openColumns on button clicks', async () => {
    const wrapper = mount(TCrudToolbar, {
      props: {
        showRefresh: true,
        showExport: true,
        showImport: true,
        showColumns: true,
      },
      global: { stubs },
    })
    await wrapper.find('.t-crud-toolbar__refresh button').trigger('click')
    await wrapper.find('.t-crud-toolbar__export button').trigger('click')
    await wrapper.find('.t-crud-toolbar__import button').trigger('click')
    await wrapper.find('.t-crud-toolbar__columns button').trigger('click')
    expect(wrapper.emitted('refresh')).toBeTruthy()
    expect(wrapper.emitted('export')).toBeTruthy()
    expect(wrapper.emitted('import')).toBeTruthy()
    expect(wrapper.emitted('openColumns')).toBeTruthy()
  })

  it('hides buttons when show* flags are false', () => {
    const wrapper = mount(TCrudToolbar, {
      props: {
        showRefresh: false,
        showExport: false,
        showImport: false,
        showColumns: false,
      },
      global: { stubs },
    })
    expect(wrapper.find('.t-crud-toolbar__refresh').exists()).toBe(false)
    expect(wrapper.find('.t-crud-toolbar__export').exists()).toBe(false)
    expect(wrapper.find('.t-crud-toolbar__import').exists()).toBe(false)
    expect(wrapper.find('.t-crud-toolbar__columns').exists()).toBe(false)
  })

  it('renders left and right slots', () => {
    const wrapper = mount(TCrudToolbar, {
      global: { stubs },
      slots: {
        left: '<span class="extra-left">L</span>',
        right: '<span class="extra-right">R</span>',
      },
    })
    expect(wrapper.find('.extra-left').exists()).toBe(true)
    expect(wrapper.find('.extra-right').exists()).toBe(true)
  })
})
