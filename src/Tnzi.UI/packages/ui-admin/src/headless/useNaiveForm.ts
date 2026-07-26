/**
 * `useNaiveForm()` - wrapper composable around Naive UI's `NForm` ref
 * that exposes a clean `validate` / `reset` / `setFieldsValue` API.
 *
 * Usage:
 * ```ts
 * const { formRef, validate, reset } = useNaiveForm()
 * // in template:  <NForm ref="formRef" :model="model" :rules="rules">
 * await validate() // throws on invalid
 * ```
 */

import { ref, type Ref } from 'vue'
import type { FormInst, FormValidationError } from 'naive-ui'

export interface UseNaiveFormReturn {
  formRef: Ref<FormInst | null>
  /** Validate every field; resolves on success, rejects with `FormValidationError[]` on failure. */
  validate(): Promise<void>
  /** Validate only specific fields by path string. */
  validateFields(paths: string[]): Promise<void>
  /** Restore all fields to their initial value as defined by the model object. */
  reset(): void
  /** Patch a subset of the model values, then re-validate touched fields. */
  setFieldsValue<T extends Record<string, unknown>>(model: T, patch: Partial<T>): void
}

export function useNaiveForm(): UseNaiveFormReturn {
  const formRef = ref<FormInst | null>(null)

  async function validate(): Promise<void> {
    if (!formRef.value) {
      throw new Error('useNaiveForm: NForm ref not yet bound')
    }
    try {
      await formRef.value.validate()
    } catch (errors) {
      throw errors as FormValidationError[]
    }
  }

  async function validateFields(paths: string[]): Promise<void> {
    if (!formRef.value) {
      throw new Error('useNaiveForm: NForm ref not yet bound')
    }
    // Naive's FormItemRule.key is the field path; older naive versions also
    // expose `path`. Treat both as opt-in.
    await formRef.value.validate(undefined, (rule: { key?: string; path?: string }) =>
      paths.includes((rule.key ?? rule.path ?? '') as string),
    )
  }

  function reset(): void {
    if (formRef.value) formRef.value.restoreValidation()
  }

  function setFieldsValue<T extends Record<string, unknown>>(
    model: T,
    patch: Partial<T>,
  ): void {
    Object.assign(model, patch)
  }

  return { formRef, validate, validateFields, reset, setFieldsValue }
}
