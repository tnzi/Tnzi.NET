import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { ref } from 'vue'
import TFormSchemaRenderer, { type FormSchemaItem } from '../../../src/pages/_shared/form-schema'

// Naive UI component names are without the 'N' prefix (e.g. 'Form', not 'NForm').
// vue-test-utils matches stubs by the component's internal `name` property.
const stubs = {
  Form: { template: '<form><slot /></form>' },
  FormItem: { props: ['label', 'path'], template: '<div class="form-item" :data-label="label"><slot /></div>' },
  Input: { props: ['value', 'disabled', 'readonly'], emits: ['update:value'], template: '<input class="ipt" :value="value" :disabled="disabled" :readonly="readonly" @input="$emit(\'update:value\', $event.target.value)" />' },
  InputNumber: { props: ['value', 'disabled', 'readonly'], emits: ['update:value'], template: '<input type="number" class="ipt-num" :value="value" :disabled="disabled" :readonly="readonly" @input="$emit(\'update:value\', Number($event.target.value))" />' },
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

  it('readonly=true renders the record as description rows, not switched-off inputs', () => {
    // View-mode design: a "view" action shows a RECORD, so the default readonly
    // layout is a `label: value` description list. A column of greyed-out
    // controls reads as a database row editor someone disabled, which is
    // exactly the impression the admin pages are meant to avoid.
    const schema: FormSchemaItem[] = [
      { key: 'name', label: 'Name', type: 'text' },
      { key: 'age',  label: 'Age',  type: 'number' },
    ]
    const wrapper = mount(TFormSchemaRenderer, {
      props: { schema, model: { name: 'Ada', age: 36 }, readonly: true },
      global: { stubs },
    })
    // No editors at all in the default readonly layout.
    expect(wrapper.find('.ipt').exists()).toBe(false)
    expect(wrapper.find('.ipt-num').exists()).toBe(false)
    expect(wrapper.findAll('.t-desc__row')).toHaveLength(2)
    expect(wrapper.text()).toContain('Name')
    expect(wrapper.text()).toContain('Ada')
    expect(wrapper.text()).toContain('36')
  })

  it('readonly + readonlyLayout="inputs" keeps text/number editors non-editable', () => {
    // Opt-out shape for pages whose view mode is really "edit, temporarily
    // locked": text-style fields (text / textarea / number) use the native
    // `readonly` attribute (keeps normal text colour, stays readable for long
    // content) rather than `disabled`. Non-text widgets (switch / select /
    // date) use `disabled` since they lack `readonly`.
    const schema: FormSchemaItem[] = [
      { key: 'name', label: 'Name', type: 'text' },
      { key: 'age',  label: 'Age',  type: 'number' },
    ]
    const wrapper = mount(TFormSchemaRenderer, {
      props: { schema, model: {}, readonly: true, readonlyLayout: 'inputs' },
      global: { stubs },
    })
    expect((wrapper.find('.ipt').element as HTMLInputElement).readOnly).toBe(true)
    expect((wrapper.find('.ipt-num').element as HTMLInputElement).readOnly).toBe(true)
  })

  it('shows a readonly select as its option LABEL and a blank field as the dash', () => {
    const schema: FormSchemaItem[] = [
      {
        key: 'status',
        label: 'Status',
        type: 'select',
        options: [
          { label: 'Active', value: 'A' },
          { label: 'Closed', value: 'C' },
        ],
      },
      { key: 'note', label: 'Note', type: 'text' },
    ]
    const wrapper = mount(TFormSchemaRenderer, {
      props: { schema, model: { status: 'C', note: '' }, readonly: true },
      global: { stubs },
    })
    expect(wrapper.text()).toContain('Closed')
    // "no value" renders the one shared placeholder, never an empty cell.
    expect(wrapper.text()).toContain('-')
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
