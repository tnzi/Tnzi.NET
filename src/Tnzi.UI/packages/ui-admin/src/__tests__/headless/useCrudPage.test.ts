import { describe, it, expect, vi, beforeEach } from 'vitest'
import { useCrudPage, type CrudPageResult } from '../../headless/useCrudPage'

type Item = { id: string; name: string }

function makeResult(items: Item[], total = items.length, page = 1, size = 20): CrudPageResult<Item> {
  return { items, totalCount: total, pageIndex: page, pageSize: size }
}

function makeOptions(overrides: Partial<Parameters<typeof useCrudPage<Item>>[0]> = {}) {
  const fetchFn = vi.fn().mockResolvedValue(makeResult([{ id: '1', name: 'Alice' }], 1))
  return { fetchFn, ...overrides }
}

describe('useCrudPage', () => {
  it('initializes with default state', () => {
    const opts = makeOptions()
    const page = useCrudPage(opts)
    expect(page.items.value).toEqual([])
    expect(page.isLoading.value).toBe(false)
    expect(page.selectedKeys.value).toEqual([])
    expect(page.searchKeyword.value).toBe('')
    expect(page.isFormModalOpen.value).toBe(false)
    expect(page.formMode.value).toBe('create')
    expect(page.isSaving.value).toBe(false)
    expect(page.totalCount.value).toBe(0)
    expect(page.pageIndex.value).toBe(1)
    expect(page.pageSize.value).toBe(20)
    expect(page.sortBy.value).toBeUndefined()
    expect(page.sortOrder.value).toBeUndefined()
    expect(page.error.value).toBeNull()
    expect(page.hasSelection.value).toBe(false)
    expect(page.totalPages.value).toBe(0)
  })

  it('respects defaultPageSize option', () => {
    const page = useCrudPage(makeOptions({ defaultPageSize: 50 }))
    expect(page.pageSize.value).toBe(50)
  })

  it('fetch loads items and updates totalCount', async () => {
    const fetchFn = vi.fn().mockResolvedValue(makeResult([{ id: '1', name: 'Alice' }, { id: '2', name: 'Bob' }], 2))
    const page = useCrudPage({ fetchFn })
    await page.fetch()
    expect(page.items.value).toHaveLength(2)
    expect(page.totalCount.value).toBe(2)
    expect(page.isLoading.value).toBe(false)
  })

  it('fetch passes correct query to fetchFn', async () => {
    const fetchFn = vi.fn().mockResolvedValue(makeResult([]))
    const page = useCrudPage({ fetchFn })
    page.searchKeyword.value = 'test'
    page.sortBy.value = 'name'
    page.sortOrder.value = 'asc'
    await page.fetch()
    expect(fetchFn).toHaveBeenCalledWith(
      expect.objectContaining({ keyword: 'test', sortBy: 'name', sortOrder: 'asc', pageIndex: 1, pageSize: 20 }),
    )
  })

  it('fetch captures error on failure', async () => {
    const fetchFn = vi.fn().mockRejectedValue(new Error('Network error'))
    const page = useCrudPage({ fetchFn })
    await page.fetch()
    expect(page.error.value).toBe('Network error')
    expect(page.isLoading.value).toBe(false)
  })

  it('search resets pageIndex to 1 and fetches', async () => {
    const fetchFn = vi.fn().mockResolvedValue(makeResult([]))
    const page = useCrudPage({ fetchFn })
    page.pageIndex.value = 3
    await page.search()
    expect(page.pageIndex.value).toBe(1)
    expect(fetchFn).toHaveBeenCalledOnce()
  })

  it('resetSearch clears keyword and resets to page 1', async () => {
    const fetchFn = vi.fn().mockResolvedValue(makeResult([]))
    const page = useCrudPage({ fetchFn })
    page.searchKeyword.value = 'foo'
    page.pageIndex.value = 5
    await page.resetSearch()
    expect(page.searchKeyword.value).toBe('')
    expect(page.pageIndex.value).toBe(1)
    expect(fetchFn).toHaveBeenCalledOnce()
  })

  it('changePage updates pageIndex and pageSize then fetches', async () => {
    const fetchFn = vi.fn().mockResolvedValue(makeResult([]))
    const page = useCrudPage({ fetchFn })
    await page.changePage(3, 50)
    expect(page.pageIndex.value).toBe(3)
    expect(page.pageSize.value).toBe(50)
    expect(fetchFn).toHaveBeenCalledOnce()
  })

  it('changeSort updates sort state and resets to page 1', async () => {
    const fetchFn = vi.fn().mockResolvedValue(makeResult([]))
    const page = useCrudPage({ fetchFn })
    page.pageIndex.value = 4
    await page.changeSort('name', 'desc')
    expect(page.sortBy.value).toBe('name')
    expect(page.sortOrder.value).toBe('desc')
    expect(page.pageIndex.value).toBe(1)
    expect(fetchFn).toHaveBeenCalledOnce()
  })

  it('openCreate sets form mode to create and opens modal', () => {
    const page = useCrudPage(makeOptions())
    page.openCreate()
    expect(page.isFormModalOpen.value).toBe(true)
    expect(page.formMode.value).toBe('create')
    expect(page.formData.value).toEqual({})
  })

  it('openEdit sets form mode to edit and copies item data', () => {
    const page = useCrudPage(makeOptions())
    const item: Item = { id: '42', name: 'Bob' }
    page.openEdit(item)
    expect(page.isFormModalOpen.value).toBe(true)
    expect(page.formMode.value).toBe('edit')
    expect(page.formData.value).toEqual({ id: '42', name: 'Bob' })
    expect(page.formData.value).not.toBe(item) // immutable copy
  })

  it('closeModal closes form and clears data', () => {
    const page = useCrudPage(makeOptions())
    page.openCreate()
    page.closeModal()
    expect(page.isFormModalOpen.value).toBe(false)
    expect(page.formData.value).toEqual({})
  })

  it('save in create mode calls onCreate and closes modal', async () => {
    const onCreate = vi.fn().mockResolvedValue(undefined)
    const fetchFn = vi.fn().mockResolvedValue(makeResult([]))
    const page = useCrudPage({ fetchFn, onCreate })
    page.openCreate()
    page.formData.value = { name: 'New Item' }
    await page.save()
    expect(onCreate).toHaveBeenCalledWith({ name: 'New Item' })
    expect(page.isFormModalOpen.value).toBe(false)
    expect(fetchFn).toHaveBeenCalledOnce()
  })

  it('save in edit mode calls onUpdate and closes modal', async () => {
    const onUpdate = vi.fn().mockResolvedValue(undefined)
    const fetchFn = vi.fn().mockResolvedValue(makeResult([]))
    const page = useCrudPage({ fetchFn, onUpdate })
    page.openEdit({ id: '1', name: 'Old' })
    page.formData.value = { id: '1', name: 'Updated' }
    await page.save()
    expect(onUpdate).toHaveBeenCalledWith({ id: '1', name: 'Updated' })
    expect(page.isFormModalOpen.value).toBe(false)
    expect(fetchFn).toHaveBeenCalledOnce()
  })

  it('save captures error and does not close modal on failure', async () => {
    const onCreate = vi.fn().mockRejectedValue(new Error('Save failed'))
    const fetchFn = vi.fn().mockResolvedValue(makeResult([]))
    const page = useCrudPage({ fetchFn, onCreate })
    page.openCreate()
    await page.save()
    expect(page.error.value).toBe('Save failed')
    expect(page.isFormModalOpen.value).toBe(true)
    expect(page.isSaving.value).toBe(false)
  })

  it('deleteItem calls onDelete and refreshes', async () => {
    const onDelete = vi.fn().mockResolvedValue(undefined)
    const fetchFn = vi.fn().mockResolvedValue(makeResult([]))
    const page = useCrudPage({ fetchFn, onDelete })
    const item: Item = { id: '1', name: 'Alice' }
    await page.deleteItem(item)
    expect(onDelete).toHaveBeenCalledWith(item)
    expect(fetchFn).toHaveBeenCalledOnce()
  })

  it('deleteItem captures error on failure', async () => {
    const onDelete = vi.fn().mockRejectedValue(new Error('Delete failed'))
    const fetchFn = vi.fn().mockResolvedValue(makeResult([]))
    const page = useCrudPage({ fetchFn, onDelete })
    await page.deleteItem({ id: '1', name: 'Alice' })
    expect(page.error.value).toBe('Delete failed')
  })

  it('batchDelete calls onBatchDelete with selectedKeys and clears selection', async () => {
    const onBatchDelete = vi.fn().mockResolvedValue(undefined)
    const fetchFn = vi.fn().mockResolvedValue(makeResult([]))
    const page = useCrudPage({ fetchFn, onBatchDelete })
    page.selectedKeys.value = ['1', '2', '3']
    await page.batchDelete()
    expect(onBatchDelete).toHaveBeenCalledWith(['1', '2', '3'])
    expect(page.selectedKeys.value).toEqual([])
    expect(fetchFn).toHaveBeenCalledOnce()
  })

  it('batchDelete does nothing when no selection', async () => {
    const onBatchDelete = vi.fn()
    const fetchFn = vi.fn()
    const page = useCrudPage({ fetchFn: vi.fn().mockResolvedValue(makeResult([])), onBatchDelete })
    await page.batchDelete()
    expect(onBatchDelete).not.toHaveBeenCalled()
    expect(fetchFn).not.toHaveBeenCalled()
  })

  it('hasSelection computed reflects selectedKeys', () => {
    const page = useCrudPage(makeOptions())
    expect(page.hasSelection.value).toBe(false)
    page.selectedKeys.value = ['1']
    expect(page.hasSelection.value).toBe(true)
  })

  it('totalPages computed calculates correctly', () => {
    const page = useCrudPage(makeOptions({ defaultPageSize: 10 }))
    page.totalCount.value = 45
    expect(page.totalPages.value).toBe(5)
  })
})
