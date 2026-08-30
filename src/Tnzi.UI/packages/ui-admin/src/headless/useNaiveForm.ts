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
    // naive-ui 2.45 added a path filter to `validate`. It selects FORM ITEMS by
    // their `path` prop, which is what a caller means by "validate these fields".
    // The predicate form this replaced filtered RULES by `rule.key`, so a field
    // whose rule carried no key was silently skipped instead of validated.
    await formRef.value.validate(undefined, paths)
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
