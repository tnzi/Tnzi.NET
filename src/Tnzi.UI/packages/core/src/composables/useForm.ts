/**
 * @tnzi/core/composables/useForm
 *
 * Composable factory for FormController.
 * Returns a fully reactive form instance ready for Vue templates.
 *
 * @example
 * ```ts
 * const form = useForm({
 *   initialValues: { name: '', email: '' },
 *   schema: z.object({ name: z.string().min(1), email: z.string().email() }),
 *   onSubmit: async (values) => await api.saveUser(values),
 * });
 *
 * // In template: form.values.name, form.errors, form.isDirty, etc.
 * // Actions: form.submit(), form.reset(), form.setFieldValue('name', 'test')
 * ```
 */

import { FormController } from '../headless/form';
import type { FormOptions, FormFieldError } from '../headless/form';

/**
 * Create a reactive form controller with optional Zod schema validation.
 *
 * The returned instance is fully reactive — values, errors, isDirty, isValid,
 * isSubmitting and all other properties update automatically in Vue templates.
 */
export function useForm<T extends Record<string, unknown>>(
  options: FormOptions<T>,
): FormController<T> {
  return new FormController<T>(options);
}

export type { FormOptions, FormFieldError };
