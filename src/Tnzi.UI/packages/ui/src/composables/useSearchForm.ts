import { computed, type WritableComputedRef } from 'vue'

export interface UseSearchFormOptions {
  modelValue?: Record<string, unknown>
  onUpdateModelValue?: (value: Record<string, unknown>) => void
  onSearch?: (value: Record<string, unknown>) => void
  onReset?: () => void
}

export interface UseSearchFormReturn {
  keyword: WritableComputedRef<string>
  handleSearch: () => void
  handleReset: () => void
}

export function useSearchForm(options: UseSearchFormOptions = {}): UseSearchFormReturn {
  const modelValue = options.modelValue ?? { keyword: '' }

  const keyword = computed<string>({
    get: () => (modelValue.keyword as string) ?? '',
    set: (value: string) => {
      options.onUpdateModelValue?.({ ...modelValue, keyword: value })
    },
  })

  function handleSearch(): void {
    options.onSearch?.(modelValue)
  }

  function handleReset(): void {
    options.onUpdateModelValue?.({ ...modelValue, keyword: '' })
    options.onReset?.()
  }

  return {
    keyword,
    handleSearch,
    handleReset,
  }
}
