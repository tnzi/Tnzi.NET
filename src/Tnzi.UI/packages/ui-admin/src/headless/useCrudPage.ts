import { ref, computed, type Ref, type ComputedRef } from 'vue'

export interface CrudPageQuery {
  keyword?: string
  pageIndex: number
  pageSize: number
  sortBy?: string
  sortOrder?: 'asc' | 'desc'
}

export interface CrudPageResult<T> {
  items: T[]
  totalCount: number
  pageIndex: number
  pageSize: number
}

export interface UseCrudPageOptions<T extends Record<string, any>> {
  fetchFn: (query: CrudPageQuery) => Promise<CrudPageResult<T>>
  onCreate?: (data: Partial<T>) => Promise<void>
  onUpdate?: (data: Partial<T>) => Promise<void>
  onDelete?: (item: T) => Promise<void>
  onBatchDelete?: (keys: string[]) => Promise<void>
  defaultPageSize?: number
  rowKey?: string
}

export interface UseCrudPageReturn<T extends Record<string, any>> {
  items: Ref<T[]>
  isLoading: Ref<boolean>
  selectedKeys: Ref<string[]>
  searchKeyword: Ref<string>
  isFormModalOpen: Ref<boolean>
  formMode: Ref<'create' | 'edit'>
  formData: Ref<Partial<T>>
  isSaving: Ref<boolean>
  totalCount: Ref<number>
  pageIndex: Ref<number>
  pageSize: Ref<number>
  sortBy: Ref<string | undefined>
  sortOrder: Ref<'asc' | 'desc' | undefined>
  error: Ref<string | null>
  hasSelection: ComputedRef<boolean>
  totalPages: ComputedRef<number>
  fetch: () => Promise<void>
  search: () => Promise<void>
  resetSearch: () => Promise<void>
  changePage: (page: number, size: number) => Promise<void>
  changeSort: (field: string, order: 'asc' | 'desc') => Promise<void>
  openCreate: () => void
  openEdit: (item: T) => void
  closeModal: () => void
  save: () => Promise<void>
  deleteItem: (item: T) => Promise<void>
  batchDelete: () => Promise<void>
}

export function useCrudPage<T extends Record<string, any>>(
  options: UseCrudPageOptions<T>,
): UseCrudPageReturn<T> {
  const rowKey = options.rowKey ?? 'id'
  const defaultPageSize = options.defaultPageSize ?? 20

  const items = ref<T[]>([]) as Ref<T[]>
  const isLoading = ref(false)
  const selectedKeys = ref<string[]>([])
  const searchKeyword = ref('')
  const isFormModalOpen = ref(false)
  const formMode = ref<'create' | 'edit'>('create')
  const formData = ref<Partial<T>>({}) as Ref<Partial<T>>
  const isSaving = ref(false)
  const totalCount = ref(0)
  const pageIndex = ref(1)
  const pageSize = ref(defaultPageSize)
  const sortBy = ref<string | undefined>(undefined)
  const sortOrder = ref<'asc' | 'desc' | undefined>(undefined)
  const error = ref<string | null>(null)

  const hasSelection = computed(() => selectedKeys.value.length > 0)
  const totalPages = computed(() => (pageSize.value > 0 ? Math.ceil(totalCount.value / pageSize.value) : 0))

  function buildQuery(): CrudPageQuery {
    return {
      keyword: searchKeyword.value || undefined,
      pageIndex: pageIndex.value,
      pageSize: pageSize.value,
      sortBy: sortBy.value,
      sortOrder: sortOrder.value,
    }
  }

  async function fetch(): Promise<void> {
    isLoading.value = true
    error.value = null
    try {
      const result = await options.fetchFn(buildQuery())
      items.value = result.items
      totalCount.value = result.totalCount
    } catch (e) {
      error.value = e instanceof Error ? e.message : String(e)
    } finally {
      isLoading.value = false
    }
  }

  async function search(): Promise<void> {
    pageIndex.value = 1
    await fetch()
  }

  async function resetSearch(): Promise<void> {
    searchKeyword.value = ''
    pageIndex.value = 1
    await fetch()
  }

  async function changePage(page: number, size: number): Promise<void> {
    pageIndex.value = page
    pageSize.value = size
    await fetch()
  }

  async function changeSort(field: string, order: 'asc' | 'desc'): Promise<void> {
    sortBy.value = field
    sortOrder.value = order
    pageIndex.value = 1
    await fetch()
  }

  function openCreate(): void {
    formMode.value = 'create'
    formData.value = {}
    error.value = null
    isFormModalOpen.value = true
  }

  function openEdit(item: T): void {
    formMode.value = 'edit'
    formData.value = { ...item }
    error.value = null
    isFormModalOpen.value = true
  }

  function closeModal(): void {
    isFormModalOpen.value = false
    formData.value = {}
    error.value = null
  }

  async function save(): Promise<void> {
    isSaving.value = true
    error.value = null
    try {
      if (formMode.value === 'create') {
        await options.onCreate?.(formData.value)
      } else {
        await options.onUpdate?.(formData.value)
      }
      closeModal()
      await fetch()
    } catch (e) {
      error.value = e instanceof Error ? e.message : String(e)
    } finally {
      isSaving.value = false
    }
  }

  async function deleteItem(item: T): Promise<void> {
    error.value = null
    try {
      await options.onDelete?.(item)
      await fetch()
    } catch (e) {
      error.value = e instanceof Error ? e.message : String(e)
    }
  }

  async function batchDelete(): Promise<void> {
    if (selectedKeys.value.length === 0) return
    error.value = null
    try {
      await options.onBatchDelete?.(selectedKeys.value)
      selectedKeys.value = []
      await fetch()
    } catch (e) {
      error.value = e instanceof Error ? e.message : String(e)
    }
  }

  return {
    items,
    isLoading,
    selectedKeys,
    searchKeyword,
    isFormModalOpen,
    formMode,
    formData,
    isSaving,
    totalCount,
    pageIndex,
    pageSize,
    sortBy,
    sortOrder,
    error,
    hasSelection,
    totalPages,
    fetch,
    search,
    resetSearch,
    changePage,
    changeSort,
    openCreate,
    openEdit,
    closeModal,
    save,
    deleteItem,
    batchDelete,
  }
}
