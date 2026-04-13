import { describe, it, expect, vi } from 'vitest'
import { useForm } from '../../../src/composables/form/useForm'

describe('useForm', () => {
  it('initializes with initial values', () => {
    const { values } = useForm({ initialValues: { name: 'Alice', age: 30 } })
    expect(values.value.name).toBe('Alice')
    expect(values.value.age).toBe(30)
  })
  it('setValue updates a single field', () => {
    const { values, setValue } = useForm({ initialValues: { name: 'A' } })
    setValue('name', 'B')
    expect(values.value.name).toBe('B')
  })
  it('setValues replaces all values', () => {
    const { values, setValues } = useForm({ initialValues: { name: 'A', age: 1 } })
    setValues({ name: 'B', age: 2 })
    expect(values.value).toEqual({ name: 'B', age: 2 })
  })
  it('reset returns values to initialValues', () => {
    const { values, setValue, reset } = useForm({ initialValues: { name: 'A' } })
    setValue('name', 'B')
    reset()
    expect(values.value.name).toBe('A')
  })
  it('validate runs rules and sets errors', async () => {
    const { errors, validate } = useForm({
      initialValues: { email: '' },
      rules: { email: (v) => (!v ? 'Email required' : null) },
    })
    const ok = await validate()
    expect(ok).toBe(false)
    expect(errors.value.email).toBe('Email required')
  })
  it('validate returns true when all rules pass', async () => {
    const { validate } = useForm({
      initialValues: { name: 'Alice' },
      rules: { name: (v) => (!v ? 'required' : null) },
    })
    expect(await validate()).toBe(true)
  })
  it('isDirty reflects whether values differ from initial', () => {
    const { isDirty, setValue } = useForm({ initialValues: { name: 'A' } })
    expect(isDirty.value).toBe(false)
    setValue('name', 'B')
    expect(isDirty.value).toBe(true)
  })
  it('handleSubmit calls onSubmit when valid', async () => {
    const onSubmit = vi.fn()
    const { handleSubmit } = useForm({ initialValues: { name: 'A' }, onSubmit })
    await handleSubmit()
    expect(onSubmit).toHaveBeenCalledWith({ name: 'A' })
  })
  it('handleSubmit skips onSubmit when invalid', async () => {
    const onSubmit = vi.fn()
    const { handleSubmit } = useForm({
      initialValues: { name: '' },
      rules: { name: (v) => (!v ? 'required' : null) },
      onSubmit,
    })
    await handleSubmit()
    expect(onSubmit).not.toHaveBeenCalled()
  })
})
