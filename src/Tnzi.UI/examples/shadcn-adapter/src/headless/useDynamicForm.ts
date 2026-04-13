import { computed, type ComputedRef } from 'vue'
import type { IDynamicFormField } from '@tnzi/core/types/shared-ui'

export interface UseDynamicFormOptions {
  model?: Record<string, unknown>
  modelValue?: Record<string, unknown>
  onUpdateModelValue?: (value: Record<string, unknown>) => void
  onFieldChange?: (key: string, value: unknown) => void
  onSubmit?: (data: Record<string, unknown>) => void
  onReset?: () => void
}

export interface UseDynamicFormReturn {
  currentModel: ComputedRef<Record<string, unknown>>
  updateField: (field: IDynamicFormField, value: unknown) => void
  handleSubmit: () => void
  handleReset: () => void
}

export function useDynamicForm(options: UseDynamicFormOptions = {}): UseDynamicFormReturn {
  const currentModel = computed(() => options.modelValue ?? options.model ?? {})

  function updateField(field: IDynamicFormField, value: unknown): void {
    const next = { ...currentModel.value, [field.key]: value }
    options.onUpdateModelValue?.(next)
    options.onFieldChange?.(field.key, value)
  }

  function handleSubmit(): void {
    options.onSubmit?.(currentModel.value)
  }

  function handleReset(): void {
    options.onReset?.()
  }

  return {
    currentModel,
    updateField,
    handleSubmit,
    handleReset,
  }
}
