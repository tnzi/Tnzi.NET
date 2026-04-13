import { ref, type Ref } from 'vue'

export interface UseFormModalOptions<T> {
  onSave?: (data: T, mode: 'create' | 'edit') => Promise<void>
}

export interface UseFormModalReturn<T> {
  isOpen: Ref<boolean>
  mode: Ref<'create' | 'edit'>
  data: Ref<Partial<T>>
  isSaving: Ref<boolean>
  error: Ref<string | null>
  open: (mode: 'create' | 'edit', initialData?: Partial<T>) => void
  close: () => void
  save: () => Promise<void>
}

export function useFormModal<T extends Record<string, any> = Record<string, any>>(
  options: UseFormModalOptions<T> = {},
): UseFormModalReturn<T> {
  const isOpen = ref(false)
  const mode = ref<'create' | 'edit'>('create')
  const data = ref<Partial<T>>({}) as Ref<Partial<T>>
  const isSaving = ref(false)
  const error = ref<string | null>(null)

  function open(openMode: 'create' | 'edit', initialData?: Partial<T>): void {
    mode.value = openMode
    data.value = initialData ? { ...initialData } : {}
    error.value = null
    isOpen.value = true
  }

  function close(): void {
    isOpen.value = false
    data.value = {}
    error.value = null
  }

  async function save(): Promise<void> {
    isSaving.value = true
    error.value = null
    try {
      await options.onSave?.(data.value as T, mode.value)
      close()
    } catch (e) {
      error.value = e instanceof Error ? e.message : String(e)
    } finally {
      isSaving.value = false
    }
  }

  return {
    isOpen,
    mode,
    data,
    isSaving,
    error,
    open,
    close,
    save,
  }
}
