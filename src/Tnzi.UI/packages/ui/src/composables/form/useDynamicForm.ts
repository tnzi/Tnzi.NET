import { computed } from 'vue'
import { useForm, type ValidationRule } from './useForm'

export type DynamicFieldType =
  | 'text'
  | 'password'
  | 'number'
  | 'email'
  | 'tel'
  | 'url'
  | 'textarea'
  | 'select'
  | 'radio'
  | 'checkbox'
  | 'switch'
  | 'date'
  | 'datetime'
  | 'time'

export interface DynamicFieldOption {
  value: string | number | boolean
  label: string
  disabled?: boolean
}

export interface DynamicFormField {
  key: string
  type: DynamicFieldType
  label: string
  placeholder?: string
  required?: boolean
  disabled?: boolean
  options?: DynamicFieldOption[]
  defaultValue?: unknown
  rule?: ValidationRule
  visibleWhen?: (values: Record<string, unknown>) => boolean
  colspan?: number
}

export interface UseDynamicFormOptions {
  fields: DynamicFormField[]
  initialValues?: Record<string, unknown>
  onSubmit?: (values: Record<string, unknown>) => void | Promise<void>
}

export function useDynamicForm(options: UseDynamicFormOptions) {
  const initialValues: Record<string, unknown> = {}
  const rules: Partial<Record<string, ValidationRule>> = {}

  for (const field of options.fields) {
    initialValues[field.key] = options.initialValues?.[field.key] ?? field.defaultValue ?? ''
    if (field.rule) {
      rules[field.key] = field.rule
    } else if (field.required) {
      rules[field.key] = (v: unknown) => {
        if (v === null || v === undefined || v === '') return `${field.label} is required`
        return null
      }
    }
  }

  const form = useForm({
    initialValues,
    rules,
    onSubmit: options.onSubmit,
  })

  const visibleFields = computed(() =>
    options.fields.filter((f) => !f.visibleWhen || f.visibleWhen(form.values.value)),
  )

  return {
    ...form,
    fields: options.fields,
    visibleFields,
  }
}
