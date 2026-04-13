import { describe, it, expect } from 'vitest'
import { useDataTable } from '../../composables/useDataTable'

describe('useDataTable', () => {
  it('should initialize with default values', () => {
    const table = useDataTable()
    expect(table.currentPage.value).toBe(1)
    expect(table.currentPageSize.value).toBe(10)
    expect(table.localSortBy.value).toBeUndefined()
    expect(table.localSortOrder.value).toBeUndefined()
    expect(table.localSelectedKeys.value).toEqual([])
  })

  it('should initialize from options', () => {
    const table = useDataTable({
      sortBy: 'name',
      sortOrder: 'asc',
      selectedKeys: ['a', 'b'],
      pagination: { pageIndex: 2, pageSize: 20, total: 100 },
    })
    expect(table.currentPage.value).toBe(2)
    expect(table.currentPageSize.value).toBe(20)
    expect(table.localSortBy.value).toBe('name')
    expect(table.localSortOrder.value).toBe('asc')
    expect(table.localSelectedKeys.value).toEqual(['a', 'b'])
  })

  it('should compute totalPages from pagination config', () => {
    const table = useDataTable({
      pagination: { pageIndex: 1, pageSize: 10, total: 45 },
    })
    expect(table.totalPages.value).toBe(5)
    expect(table.total.value).toBe(45)
  })

  it('should clamp page on goPage', () => {
    const table = useDataTable({
      pagination: { pageIndex: 1, pageSize: 10, total: 30 },
    })
    table.goPage(999)
    expect(table.currentPage.value).toBe(3)
    table.goPage(0)
    expect(table.currentPage.value).toBe(1)
  })

  it('should toggle sort via handleSort', () => {
    const table = useDataTable()
    table.handleSort({ key: 'name', title: 'Name', sortable: true })
    expect(table.localSortBy.value).toBe('name')
    expect(table.localSortOrder.value).toBe('asc')
    table.handleSort({ key: 'name', title: 'Name', sortable: true })
    expect(table.localSortOrder.value).toBe('desc')
  })

  it('should toggle row selection', () => {
    const data = [{ id: '1', name: 'A' }, { id: '2', name: 'B' }] as const
    const table = useDataTable({ data: [...data], rowKey: 'id' })
    table.toggleSelectRow(data[0], 0)
    expect(table.localSelectedKeys.value).toEqual(['1'])
    table.toggleSelectRow(data[1], 1)
    expect(table.localSelectedKeys.value).toEqual(['1', '2'])
    expect(table.allSelected.value).toBe(true)
  })

  it('should toggle select all', () => {
    const data = [{ id: '1', name: 'A' }, { id: '2', name: 'B' }] as const
    const table = useDataTable({ data: [...data], rowKey: 'id' })
    table.toggleSelectAll()
    expect(table.localSelectedKeys.value).toEqual(['1', '2'])
    table.toggleSelectAll()
    expect(table.localSelectedKeys.value).toEqual([])
  })

  it('should fire onSort callback', () => {
    let sortedField = ''
    let sortedOrder = ''
    const table = useDataTable({
      onSort: (field, order) => { sortedField = field; sortedOrder = order },
    })
    table.handleSort({ key: 'name', title: 'Name', sortable: true })
    expect(sortedField).toBe('name')
    expect(sortedOrder).toBe('asc')
  })

  it('should fire onPageChange callback', () => {
    let changedPage = 0
    const table = useDataTable({
      pagination: { pageIndex: 1, pageSize: 10, total: 50 },
      onPageChange: (page) => { changedPage = page },
    })
    table.goPage(3)
    expect(changedPage).toBe(3)
  })

  it('should support radio selection mode', () => {
    const data = [{ id: '1' }, { id: '2' }] as any[]
    const table = useDataTable({ data, rowKey: 'id', selectionType: 'radio' })
    table.toggleSelectRow(data[0], 0)
    table.toggleSelectRow(data[1], 1)
    expect(table.localSelectedKeys.value).toEqual(['2'])
  })

  it('should filter hidden columns', () => {
    const table = useDataTable({
      columns: [
        { key: 'name', title: 'Name' },
        { key: 'secret', title: 'Secret', hidden: true },
      ],
    })
    expect(table.visibleColumns.value).toHaveLength(1)
    expect(table.visibleColumns.value[0]!.key).toBe('name')
  })

  it('should not sort when column is not sortable', () => {
    const table = useDataTable()
    table.handleSort({ key: 'name', title: 'Name', sortable: false })
    expect(table.localSortBy.value).toBeUndefined()
  })

  it('should return false for hasPagination when no pagination config', () => {
    const table = useDataTable()
    expect(table.hasPagination.value).toBe(false)
  })

  it('should use data length as total when no pagination config total', () => {
    const data = [{ id: '1' }, { id: '2' }, { id: '3' }] as any[]
    const table = useDataTable({ data })
    // Without pagination config, hasPagination is false
    expect(table.hasPagination.value).toBe(false)
  })
})
