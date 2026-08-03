import { describe, it, expect, vi } from 'vitest'
import { useDynamicForm } from '../src/headless/useDynamicForm'

describe('useDynamicForm', () => {
  it('currentModel defaults to empty object', () => {
    const { currentModel } = useDynamicForm()
    expect(currentModel.value).toEqual({})
  })

  it('currentModel uses model option', () => {
    const model = { name: 'Alice', age: 30 }
    const { currentModel } = useDynamicForm({ model })
    expect(currentModel.value).toEqual(model)
  })

  it('currentModel prefers modelValue over model', () => {
    const { currentModel } = useDynamicForm({
      model: { name: 'model' },
      modelValue: { name: 'modelValue' },
    })
    expect(currentModel.value).toEqual({ name: 'modelValue' })
  })

  it('updateField calls onUpdateModelValue with merged data', () => {
    const onUpdateModelValue = vi.fn()
    const model = { name: 'Alice' }
    const { updateField } = useDynamicForm({ model, onUpdateModelValue })
    updateField({ key: 'age' }, 25)
    expect(onUpdateModelValue).toHaveBeenCalledWith({ name: 'Alice', age: 25 })
  })

  it('updateField calls onFieldChange with key and value', () => {
    const onFieldChange = vi.fn()
    const { updateField } = useDynamicForm({ model: {}, onFieldChange })
    updateField({ key: 'email' }, 'test@test.com')
    expect(onFieldChange).toHaveBeenCalledWith('email', 'test@test.com')
  })

  it('updateField does not mutate original model (immutable)', () => {
    const model = { name: 'Alice' }
    const onUpdateModelValue = vi.fn()
    const { updateField } = useDynamicForm({ model, onUpdateModelValue })
    updateField({ key: 'name' }, 'Bob')
    expect(model.name).toBe('Alice')
    const updated = onUpdateModelValue.mock.calls[0]?.[0]
    expect(updated.name).toBe('Bob')
  })

  it('handleSubmit calls onSubmit with current model', () => {
    const onSubmit = vi.fn()
    const model = { name: 'Alice', active: true }
    const { handleSubmit } = useDynamicForm({ model, onSubmit })
    handleSubmit()
    expect(onSubmit).toHaveBeenCalledWith(model)
  })

  it('handleReset calls onReset', () => {
    const onReset = vi.fn()
    const { handleReset } = useDynamicForm({ onReset })
    handleReset()
    expect(onReset).toHaveBeenCalledOnce()
  })

  it('handleSubmit does nothing when no onSubmit provided', () => {
    const { handleSubmit } = useDynamicForm()
    expect(() => handleSubmit()).not.toThrow()
  })

  it('handleReset does nothing when no onReset provided', () => {
    const { handleReset } = useDynamicForm()
    expect(() => handleReset()).not.toThrow()
  })
})
