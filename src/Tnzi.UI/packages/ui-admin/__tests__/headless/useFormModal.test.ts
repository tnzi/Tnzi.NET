import { describe, expect, it } from 'vitest'
import { useFormModal } from '../../src/headless/useFormModal'

interface UserForm {
  id?: string
  name: string
}

describe('useFormModal', () => {
  it('starts closed with null data', () => {
    const m = useFormModal<UserForm>()
    expect(m.visible.value).toBe(false)
    expect(m.mode.value).toBe(null)
    expect(m.formData.value).toBe(null)
  })

  it('open(create) sets visible/mode/data', () => {
    const m = useFormModal<UserForm>()
    m.open('create')
    expect(m.visible.value).toBe(true)
    expect(m.mode.value).toBe('create')
    expect(m.formData.value).toBe(null)
  })

  it('open(edit) sets mode to edit with existing data', () => {
    const m = useFormModal<UserForm>()
    const initial: UserForm = { id: '1', name: 'Alice' }
    m.open('edit', initial)
    expect(m.visible.value).toBe(true)
    expect(m.mode.value).toBe('edit')
    expect(m.formData.value).toEqual(initial)
    // ensure cloned (not same reference)
    expect(m.formData.value).not.toBe(initial)
  })

  it('close resets visible and data', () => {
    const m = useFormModal<UserForm>()
    m.open('edit', { id: '1', name: 'Alice' })
    m.close()
    expect(m.visible.value).toBe(false)
    expect(m.mode.value).toBe(null)
    expect(m.formData.value).toBe(null)
  })

  it('confirm() resolves with current formData', async () => {
    const m = useFormModal<UserForm>()
    const initial: UserForm = { id: '1', name: 'Alice' }
    m.open('edit', initial)
    const result = await m.confirm()
    expect(result).toEqual(initial)
  })

  it('confirm() returns null when not open', async () => {
    const m = useFormModal<UserForm>()
    const result = await m.confirm()
    expect(result).toBe(null)
  })
})
