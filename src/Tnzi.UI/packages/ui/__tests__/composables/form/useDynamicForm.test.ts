import { describe, it, expect, vi } from 'vitest'
import { useDynamicForm, type DynamicFormField } from '../../../src/composables/form/useDynamicForm'

describe('useDynamicForm', () => {
  it('seeds values from initialValues, then defaultValue, then empty string', () => {
    const fields: DynamicFormField[] = [
      { key: 'a', type: 'text', label: 'A' },
      { key: 'b', type: 'text', label: 'B', defaultValue: 'bee' },
      { key: 'c', type: 'text', label: 'C', defaultValue: 'cee' },
    ]
    const form = useDynamicForm({ fields, initialValues: { a: 'apple', c: 'override' } })
    expect(form.values.value).toEqual({ a: 'apple', b: 'bee', c: 'override' })
  })

  it('generates required-field rule when field.required=true and no explicit rule', async () => {
    const fields: DynamicFormField[] = [
      { key: 'name', type: 'text', label: 'Name', required: true },
    ]
    const form = useDynamicForm({ fields })
    const ok = await form.validate()
    expect(ok).toBe(false)
    expect(form.errors.value.name).toMatch(/required/i)
    form.setValue('name', 'Alice')
    expect(await form.validate()).toBe(true)
  })

  it('uses explicit field.rule when provided', async () => {
    const rule = vi.fn((v: unknown) => (String(v).length > 3 ? null : 'too short'))
    const fields: DynamicFormField[] = [
      { key: 'code', type: 'text', label: 'Code', rule },
    ]
    const form = useDynamicForm({ fields, initialValues: { code: 'ab' } })
    await form.validate()
    expect(form.errors.value.code).toBe('too short')
    expect(rule).toHaveBeenCalled()
  })

  it('visibleFields filters by visibleWhen predicate', () => {
    const fields: DynamicFormField[] = [
      { key: 'type', type: 'select', label: 'Type' },
      { key: 'detail', type: 'text', label: 'Detail', visibleWhen: (v) => v.type === 'advanced' },
    ]
    const form = useDynamicForm({ fields, initialValues: { type: 'basic' } })
    expect(form.visibleFields.value.map((f) => f.key)).toEqual(['type'])
    form.setValue('type', 'advanced')
    expect(form.visibleFields.value.map((f) => f.key)).toEqual(['type', 'detail'])
  })

  it('exposes all original fields via .fields', () => {
    const fields: DynamicFormField[] = [
      { key: 'a', type: 'text', label: 'A' },
      { key: 'b', type: 'text', label: 'B' },
    ]
    const form = useDynamicForm({ fields })
    expect(form.fields).toBe(fields)
  })

  it('handleSubmit delegates to useForm and invokes onSubmit with merged values', async () => {
    const onSubmit = vi.fn().mockResolvedValue(undefined)
    const fields: DynamicFormField[] = [
      { key: 'name', type: 'text', label: 'Name', defaultValue: 'x' },
    ]
    const form = useDynamicForm({ fields, onSubmit })
    await form.handleSubmit()
    expect(onSubmit).toHaveBeenCalledWith({ name: 'x' })
  })
})
