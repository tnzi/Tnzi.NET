import { describe, it, expect, vi, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useChildCollection } from '../../src/headless/useChildCollection'
import { useAdminAuthStore } from '../../src/stores/useAdminAuthStore'

interface Item { id: string; name: string }

describe('useChildCollection', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('load() populates items and toggles loading', async () => {
    const fetch = vi.fn(async () => [{ id: '1', name: 'a' }] as Item[])
    const c = useChildCollection<Item>({ fetch, autoLoad: false })
    expect(c.items.value).toEqual([])
    const p = c.load()
    expect(c.loading.value).toBe(true)
    await p
    expect(c.loading.value).toBe(false)
    expect(c.items.value).toEqual([{ id: '1', name: 'a' }])
  })

  it('autoLoad (default) fetches on creation', async () => {
    const fetch = vi.fn(async () => [{ id: '1', name: 'a' }] as Item[])
    useChildCollection<Item>({ fetch })
    await Promise.resolve()
    await Promise.resolve()
    expect(fetch).toHaveBeenCalledTimes(1)
  })

  it('openCreate / openEdit set mode + editing state', () => {
    const c = useChildCollection<Item>({ fetch: async () => [], autoLoad: false })
    c.openCreate()
    expect(c.mode.value).toBe('create')
    expect(c.editingItem.value).toBeNull()
    expect(c.editingId.value).toBeNull()
    expect(c.formOpen.value).toBe(true)
    c.openEdit({ id: '7', name: 'x' })
    expect(c.mode.value).toBe('edit')
    expect(c.editingId.value).toBe('7')
    c.close()
    expect(c.formOpen.value).toBe(false)
  })

  it('save (create mode) calls create, reloads, and closes the form', async () => {
    const create = vi.fn(async () => undefined)
    const fetch = vi.fn(async () => [] as Item[])
    const c = useChildCollection<Item>({ fetch, create, autoLoad: false })
    c.openCreate()
    await c.save({ name: 'new' })
    expect(create).toHaveBeenCalledWith({ name: 'new' })
    expect(fetch).toHaveBeenCalledTimes(1) // reload after write
    expect(c.formOpen.value).toBe(false)
  })

  it('save (edit mode) calls update with the editing id', async () => {
    const update = vi.fn(async () => undefined)
    const c = useChildCollection<Item>({ fetch: async () => [], update, autoLoad: false })
    c.openEdit({ id: '5', name: 'old' })
    await c.save({ name: 'renamed' })
    expect(update).toHaveBeenCalledWith('5', { name: 'renamed' })
  })

  it('remove accepts an id OR an item and reloads', async () => {
    const remove = vi.fn(async () => undefined)
    const fetch = vi.fn(async () => [] as Item[])
    const c = useChildCollection<Item>({ fetch, remove, autoLoad: false })
    await c.remove('9')
    expect(remove).toHaveBeenLastCalledWith('9')
    await c.remove({ id: '10', name: 'z' })
    expect(remove).toHaveBeenLastCalledWith('10')
    expect(fetch).toHaveBeenCalledTimes(2) // one reload per remove
  })

  it('load failure sets error + calls onError instead of throwing (no unhandled rejection)', async () => {
    const onError = vi.fn()
    const fetch = vi.fn(async () => {
      throw new Error('boom')
    })
    const c = useChildCollection<Item>({ fetch, onError, autoLoad: false })
    await c.load() // must NOT throw
    expect(c.error.value).toBeInstanceOf(Error)
    expect(onError).toHaveBeenCalledTimes(1)
    expect(c.loading.value).toBe(false)
  })

  it('save in edit mode with no update callback does NOT fall through to create', async () => {
    const create = vi.fn(async () => undefined)
    const c = useChildCollection<Item>({ fetch: async () => [], create, autoLoad: false })
    c.openEdit({ id: '5', name: 'x' }) // edit mode, but no `update` callback provided
    await c.save({ name: 'y' })
    expect(create).not.toHaveBeenCalled() // must not create from an edit form
  })

  it('canCreate/canUpdate/canDelete require BOTH callback and (fail-open) permission', () => {
    // No auth store user loaded → canAction fails open, so gating rests on callbacks.
    const c = useChildCollection<Item>({
      fetch: async () => [],
      create: async () => undefined,
      autoLoad: false,
      // no update / remove callbacks
    })
    expect(c.canCreate.value).toBe(true) // callback + fail-open
    expect(c.canUpdate.value).toBe(false) // no update callback
    expect(c.canDelete.value).toBe(false) // no remove callback
  })

  it('canDelete hides when the delete permission is not held', () => {
    const auth = useAdminAuthStore()
    auth.setUserInfo({ id: 'u', username: 'u', roles: [], permissions: ['thing.create'] }) // has create, not delete
    const c = useChildCollection<Item>({
      fetch: async () => [],
      create: async () => undefined,
      remove: async () => undefined,
      permission: 'thing',
      autoLoad: false,
    })
    expect(c.canCreate.value).toBe(true) // callback + thing.create held
    expect(c.canDelete.value).toBe(false) // callback present but thing.delete NOT held
  })
})
