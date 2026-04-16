import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { ref } from 'vue'
import TFormSchemaRenderer, { type FormSchemaItem } from '../../../src/pages/_shared/form-schema'

// Naive UI component names are without the 'N' prefix (e.g. 'Form', not 'NForm').
// vue-test-utils matches stubs by the component's internal `name` property.
const stubs = {
  Form: { template: '<form><slot /></form>' },
  FormItem: { props: ['label', 'path'], template: '<div class="form-item" :data-label="label"><slot /></div>' },
  Input: { props: ['value', 'disabled'], emits: ['update:value'], template: '<input class="ipt" :value="value" :disabled="disabled" @input="$emit(\'update:value\', $event.target.value)" />' },
  InputNumber: { props: ['value', 'disabled'], emits: ['update:value'], template: '<input type="number" class="ipt-num" :value="value" :disabled="disabled" @input="$emit(\'update:value\', Number($event.target.value))" />' },
  Switch: { props: ['value', 'disabled'], emits: ['update:value'], template: '<button class="sw" :data-value="value" @click="$emit(\'update:value\', !value)"></button>' },
  Select: { props: ['value', 'options', 'disabled'], emits: ['update:value'], template: '<select class="sel" :disabled="disabled"></select>' },
  DatePicker: { props: ['value', 'disabled'], emits: ['update:value'], template: '<input type="date" class="dt" />' },
}

describe('TFormSchemaRenderer', () => {
  it('renders one NFormItem per schema entry', () => {
    const schema: FormSchemaItem[] = [
      { key: 'name',  label: 'Name',  type: 'text' },
      { key: 'email', label: 'Email', type: 'text' },
    ]
    const model = ref<Record<string, unknown>>({})
    const wrapper = mount(TFormSchemaRenderer, {
      props: { schema, model: model.value, readonly: false },
      global: { stubs },
    })
    const items = wrapper.findAll('.form-item')
    expect(items).toHaveLength(2)
    expect(items[0].attributes('data-label')).toBe('Name')
  })

  it('renders number fields with NInputNumber', () => {
    const schema: FormSchemaItem[] = [{ key: 'age', label: 'Age', type: 'number' }]
    const wrapper = mount(TFormSchemaRenderer, {
      props: { schema, model: {}, readonly: false },
      global: { stubs },
    })
    expect(wrapper.find('.ipt-num').exists()).toBe(true)
  })

  it('renders switch fields', () => {
    const schema: FormSchemaItem[] = [{ key: 'active', label: 'Active', type: 'switch' }]
    const wrapper = mount(TFormSchemaRenderer, {
      props: { schema, model: { active: true }, readonly: false },
      global: { stubs },
    })
    expect(wrapper.find('.sw').attributes('data-value')).toBe('true')
  })

  it('readonly=true disables all inputs', () => {
    const schema: FormSchemaItem[] = [
      { key: 'name', label: 'Name', type: 'text' },
      { key: 'age',  label: 'Age',  type: 'number' },
    ]
    const wrapper = mount(TFormSchemaRenderer, {
      props: { schema, model: {}, readonly: true },
      global: { stubs },
    })
    expect((wrapper.find('.ipt').element as HTMLInputElement).disabled).toBe(true)
    expect((wrapper.find('.ipt-num').element as HTMLInputElement).disabled).toBe(true)
  })

  it('skips items where `visible(model)` returns false', () => {
    const schema: FormSchemaItem[] = [
      { key: 'a', label: 'A', type: 'text' },
      { key: 'b', label: 'B', type: 'text', visible: (m) => (m as { a?: string }).a === 'show' },
    ]
    const wrapper = mount(TFormSchemaRenderer, {
      props: { schema, model: { a: 'hide' }, readonly: false },
      global: { stubs },
    })
    expect(wrapper.findAll('.form-item')).toHaveLength(1)
  })

  it('updates model value on input', async () => {
    const schema: FormSchemaItem[] = [{ key: 'name', label: 'Name', type: 'text' }]
    const model = { name: '' }
    const wrapper = mount(TFormSchemaRenderer, {
      props: { schema, model, readonly: false },
      global: { stubs },
    })
    await wrapper.find('.ipt').setValue('hello')
    expect(model.name).toBe('hello')
  })
})
