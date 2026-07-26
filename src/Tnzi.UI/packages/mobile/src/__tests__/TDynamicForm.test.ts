import { describe, it, expect } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import Vant from 'vant'
import type { IDynamicFormField } from '@tnzi/core/types/shared-ui'
import TDynamicForm from '../components/form/TDynamicForm.vue'

function mountForm(props: { modelValue: Record<string, unknown>; fields: IDynamicFormField[]; disabled?: boolean }) {
  return mount(TDynamicForm, { props, global: { plugins: [Vant] } })
}

describe('TDynamicForm', () => {
  it('is v-model compatible: prop is modelValue, event is update:modelValue', async () => {
    const wrapper = mountForm({
      modelValue: { name: '' },
      fields: [{ key: 'name', type: 'text', label: 'Name' }],
    })

    await wrapper.find('input').setValue('alice')

    expect(wrapper.emitted('update:modelValue')?.[0]).toEqual([{ name: 'alice' }])
    expect(wrapper.emitted('fieldChange')?.[0]).toEqual(['name', 'alice'])
  })

  it('blocks submit while a required field is empty', async () => {
    const wrapper = mountForm({
      modelValue: { name: '' },
      fields: [{ key: 'name', type: 'text', label: 'Name', required: true }],
    })

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.emitted('submit')).toBeUndefined()
  })

  it('submits once the required field is filled', async () => {
    const wrapper = mountForm({
      modelValue: { name: 'alice' },
      fields: [{ key: 'name', type: 'text', label: 'Name', required: true }],
    })

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.emitted('submit')?.[0]).toEqual([{ name: 'alice' }])
  })

  it('enforces min/max as a numeric range on number fields', async () => {
    const wrapper = mountForm({
      modelValue: { age: 5 },
      fields: [{ key: 'age', type: 'number', label: 'Age', min: 10, max: 20 }],
    })

    await wrapper.find('form').trigger('submit')
    await flushPromises()
    expect(wrapper.emitted('submit')).toBeUndefined()

    await wrapper.setProps({ modelValue: { age: 15 } })
    await wrapper.find('form').trigger('submit')
    await flushPromises()
    expect(wrapper.emitted('submit')?.[0]).toEqual([{ age: 15 }])
  })

  it('enforces min/max as text length on string fields', async () => {
    const wrapper = mountForm({
      modelValue: { code: 'ab' },
      fields: [{ key: 'code', type: 'text', label: 'Code', min: 3 }],
    })

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.emitted('submit')).toBeUndefined()
  })

  it('applies rules declared on the field contract', async () => {
    const wrapper = mountForm({
      modelValue: { email: 'nope' },
      fields: [
        {
          key: 'email',
          type: 'email',
          label: 'Email',
          rules: [{ pattern: /^[^\s@]+@[^\s@]+\.[^\s@]+$/, message: 'Invalid email' }],
        },
      ],
    })

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.emitted('submit')).toBeUndefined()
  })

  it('renders a date picker instead of a free-text box', async () => {
    const wrapper = mountForm({
      modelValue: {},
      fields: [{ key: 'due', type: 'date', label: 'Due' }],
    })

    expect(wrapper.find('input').attributes('readonly')).toBeDefined()
    expect(wrapper.find('.van-picker').exists()).toBe(false)

    await wrapper.find('.van-cell').trigger('click')
    await flushPromises()

    expect(wrapper.find('.van-picker').exists()).toBe(true)
  })

  it('writes back an ISO date when the date picker is confirmed', async () => {
    const wrapper = mountForm({
      modelValue: { due: '2024-03-05' },
      fields: [{ key: 'due', type: 'date', label: 'Due' }],
    })

    await wrapper.find('.van-cell').trigger('click')
    await flushPromises()
    await wrapper.find('.van-picker__confirm').trigger('click')

    expect(wrapper.emitted('update:modelValue')?.[0]).toEqual([{ due: '2024-03-05' }])
  })

  it('renders a two-step picker group for datetime fields', async () => {
    const wrapper = mountForm({
      modelValue: { startAt: '2024-03-05 08:30' },
      fields: [{ key: 'startAt', type: 'datetime', label: 'Start' }],
    })

    await wrapper.find('.van-cell').trigger('click')
    await flushPromises()

    expect(wrapper.find('.van-picker-group').exists()).toBe(true)
  })

  it('renders an uploader for file fields', () => {
    const wrapper = mountForm({
      modelValue: {},
      fields: [{ key: 'attachment', type: 'file', label: 'Attachment' }],
    })

    expect(wrapper.find('.van-uploader').exists()).toBe(true)
  })

  it('forwards a consumer class to the root element', () => {
    const wrapper = mount(TDynamicForm, {
      props: { modelValue: {}, fields: [] },
      attrs: { class: 'consumer-class' },
      global: { plugins: [Vant] },
    })

    expect(wrapper.classes()).toContain('consumer-class')
  })
})
