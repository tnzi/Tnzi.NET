import { describe, it, expect } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import Vant from 'vant'
import type { IDynamicFormField } from '@tnzi/core/types/shared-ui'
import TDynamicForm from '../src/components/form/TDynamicForm.vue'

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

  // A checkbox with `options` holds an array; the text-length rule joined it
  // ("a,b".length) and answered "at least 2" for one two-letter selection.
  it('enforces min/max as a selection count on checkbox groups', async () => {
    const options = [{ label: 'One', value: 'ab' }, { label: 'Two', value: 'c' }, { label: 'Three', value: 'd' }]
    const wrapper = mountForm({
      modelValue: { tags: ['ab'] },
      fields: [{ key: 'tags', type: 'checkbox', label: 'Tags', options, min: 2, max: 2 }],
    })

    await wrapper.find('form').trigger('submit')
    await flushPromises()
    expect(wrapper.emitted('submit')).toBeUndefined()

    await wrapper.setProps({ modelValue: { tags: ['c', 'd'] } })
    await wrapper.find('form').trigger('submit')
    await flushPromises()
    expect(wrapper.emitted('submit')?.[0]).toEqual([{ tags: ['c', 'd'] }])

    await wrapper.setProps({ modelValue: { tags: ['ab', 'c', 'd'] } })
    await wrapper.find('form').trigger('submit')
    await flushPromises()
    expect(wrapper.emitted('submit')).toHaveLength(1)
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

describe('TDynamicForm - required and props on every field type', () => {
  async function submit(wrapper: ReturnType<typeof mountForm>) {
    await wrapper.find('form').trigger('submit')
    await flushPromises()
  }

  it('blocks submit while a required radio has no selection', async () => {
    const wrapper = mountForm({
      modelValue: {},
      fields: [{ key: 'gender', type: 'radio', label: 'Gender', required: true, options: [{ label: 'A', value: 'a' }] }],
    })
    await submit(wrapper)
    expect(wrapper.emitted('submit')).toBeUndefined()
    expect(wrapper.text()).toContain('This field is required')

    await wrapper.setProps({ modelValue: { gender: 'a' } })
    await submit(wrapper)
    expect(wrapper.emitted('submit')?.[0]).toEqual([{ gender: 'a' }])
  })

  it('blocks submit while a required file field holds no file', async () => {
    const wrapper = mountForm({
      modelValue: { doc: [] },
      fields: [{ key: 'doc', type: 'file', label: 'Document', required: true }],
    })
    await submit(wrapper)
    expect(wrapper.emitted('submit')).toBeUndefined()

    await wrapper.setProps({ modelValue: { doc: [{ url: 'x.png' }] } })
    await submit(wrapper)
    expect(wrapper.emitted('submit')).toHaveLength(1)
  })

  it('treats a required switch or checkbox as one that must be on', async () => {
    const wrapper = mountForm({
      modelValue: { consent: false, tos: false },
      fields: [
        { key: 'consent', type: 'switch', label: 'Consent', required: true },
        { key: 'tos', type: 'checkbox', label: 'Terms', required: true },
      ],
    })
    await submit(wrapper)
    expect(wrapper.emitted('submit')).toBeUndefined()

    await wrapper.setProps({ modelValue: { consent: true, tos: true } })
    await submit(wrapper)
    expect(wrapper.emitted('submit')).toHaveLength(1)
  })

  it('renders a checkbox group when a checkbox field declares options', async () => {
    const wrapper = mountForm({
      modelValue: { tags: [] },
      fields: [{ key: 'tags', type: 'checkbox', label: 'Tags', options: [{ label: 'One', value: 1 }, { label: 'Two', value: 2 }] }],
    })
    expect(wrapper.findAll('.van-checkbox')).toHaveLength(2)

    await wrapper.findAll('.van-checkbox')[1]!.trigger('click')
    expect(wrapper.emitted('update:modelValue')?.[0]).toEqual([{ tags: [2] }])
  })

  it('forwards field.props to the underlying control', () => {
    const wrapper = mountForm({
      modelValue: { code: '', bio: '' },
      fields: [
        { key: 'code', type: 'text', label: 'Code', props: { maxlength: 5, showWordLimit: true } },
        { key: 'bio', type: 'textarea', label: 'Bio', props: { rows: 6 } },
      ],
    })
    // Vant enforces maxlength in script, so the word-limit counter is the visible proof.
    expect(wrapper.find('.van-field__word-limit').text()).toContain('/5')
    expect(wrapper.find('textarea').attributes('rows')).toBe('6')
  })
})
