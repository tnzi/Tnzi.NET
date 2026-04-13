import { ref, computed, type Ref, type ComputedRef } from 'vue'

export type ValidationRule<T = unknown> = (
  value: T,
  allValues: Record<string, unknown>,
) => string | null | Promise<string | null>

export interface UseFormOptions<T extends Record<string, unknown>> {
  initialValues: T
  rules?: Partial<Record<keyof T, ValidationRule>>
  onSubmit?: (values: T) => void | Promise<void>
}

export interface UseFormReturn<T extends Record<string, unknown>> {
  values: Ref<T>
  errors: Ref<Partial<Record<keyof T, string>>>
  touched: Ref<Partial<Record<keyof T, boolean>>>
  submitting: Ref<boolean>
  isDirty: ComputedRef<boolean>
  isValid: ComputedRef<boolean>
  setValue: <K extends keyof T>(key: K, value: T[K]) => void
  setValues: (newValues: Partial<T>) => void
  validate: () => Promise<boolean>
  validateField: <K extends keyof T>(key: K) => Promise<string | null>
  reset: () => void
  handleSubmit: () => Promise<void>
}

export function useForm<T extends Record<string, unknown>>(
  options: UseFormOptions<T>,
): UseFormReturn<T> {
  const initial = { ...options.initialValues }
  const values: Ref<T> = ref({ ...options.initialValues }) as Ref<T>
  const errors = ref({}) as Ref<Partial<Record<keyof T, string>>>
  const touched = ref({}) as Ref<Partial<Record<keyof T, boolean>>>
  const submitting = ref(false)

  const isDirty = computed(() => {
    for (const key in initial) {
      if (values.value[key] !== initial[key]) return true
    }
    return false
  })

  const isValid = computed(() => Object.keys(errors.value).length === 0)

  function setValue<K extends keyof T>(key: K, value: T[K]): void {
    values.value[key] = value
    touched.value[key] = true
  }

  function setValues(newValues: Partial<T>): void {
    values.value = { ...values.value, ...newValues }
    for (const key in newValues) {
      touched.value[key as keyof T] = true
    }
  }

  async function validateField<K extends keyof T>(key: K): Promise<string | null> {
    const rule = options.rules?.[key]
    if (!rule) return null
    const result = await rule(values.value[key], values.value as Record<string, unknown>)
    if (result) {
      errors.value[key] = result
    } else {
      delete errors.value[key]
    }
    return result
  }

  async function validate(): Promise<boolean> {
    errors.value = {}
    if (!options.rules) return true
    for (const key in options.rules) {
      await validateField(key as keyof T)
    }
    return Object.keys(errors.value).length === 0
  }

  function reset(): void {
    values.value = { ...initial }
    errors.value = {}
    touched.value = {}
  }

  async function handleSubmit(): Promise<void> {
    const ok = await validate()
    if (!ok) return
    submitting.value = true
    try {
      await options.onSubmit?.(values.value)
    } finally {
      submitting.value = false
    }
  }

  return {
    values,
    errors,
    touched,
    submitting,
    isDirty,
    isValid,
    setValue,
    setValues,
    validate,
    validateField,
    reset,
    handleSubmit,
  }
}
