import { describe, it, expect, vi } from 'vitest'
import { useFormModal } from '../../headless/useFormModal'

type Item = { id?: string; name: string }

describe('useFormModal', () => {
  it('initializes closed with empty data', () => {
    const modal = useFormModal<Item>()
    expect(modal.isOpen.value).toBe(false)
    expect(modal.mode.value).toBe('create')
    expect(modal.data.value).toEqual({})
    expect(modal.isSaving.value).toBe(false)
    expect(modal.error.value).toBeNull()
  })

  it('open sets mode and initialData', () => {
    const modal = useFormModal<Item>()
    modal.open('edit', { id: '1', name: 'Alice' })
    expect(modal.isOpen.value).toBe(true)
    expect(modal.mode.value).toBe('edit')
    expect(modal.data.value).toEqual({ id: '1', name: 'Alice' })
  })

  it('open create mode starts with empty data', () => {
    const modal = useFormModal<Item>()
    modal.open('create')
    expect(modal.mode.value).toBe('create')
    expect(modal.data.value).toEqual({})
  })

  it('open creates a copy of initialData (immutable)', () => {
    const modal = useFormModal<Item>()
    const original = { id: '1', name: 'Alice' }
    modal.open('edit', original)
    modal.data.value.name = 'Modified'
    expect(original.name).toBe('Alice')
  })

  it('close resets state', () => {
    const modal = useFormModal<Item>()
    modal.open('edit', { id: '1', name: 'Alice' })
    modal.close()
    expect(modal.isOpen.value).toBe(false)
    expect(modal.data.value).toEqual({})
  })

  it('save calls onSave with data and mode', async () => {
    const onSave = vi.fn().mockResolvedValue(undefined)
    const modal = useFormModal<Item>({ onSave })
    modal.open('create', { name: 'New' })
    await modal.save()
    expect(onSave).toHaveBeenCalledWith({ name: 'New' }, 'create')
    expect(modal.isOpen.value).toBe(false)
  })

  it('save closes modal after success', async () => {
    const modal = useFormModal<Item>({ onSave: vi.fn().mockResolvedValue(undefined) })
    modal.open('create')
    await modal.save()
    expect(modal.isOpen.value).toBe(false)
    expect(modal.isSaving.value).toBe(false)
  })

  it('save captures error and stays open on failure', async () => {
    const onSave = vi.fn().mockRejectedValue(new Error('Validation failed'))
    const modal = useFormModal<Item>({ onSave })
    modal.open('create')
    await modal.save()
    expect(modal.error.value).toBe('Validation failed')
    expect(modal.isOpen.value).toBe(true)
    expect(modal.isSaving.value).toBe(false)
  })

  it('save works without onSave callback', async () => {
    const modal = useFormModal<Item>()
    modal.open('create')
    await expect(modal.save()).resolves.toBeUndefined()
    expect(modal.isOpen.value).toBe(false)
  })
})
