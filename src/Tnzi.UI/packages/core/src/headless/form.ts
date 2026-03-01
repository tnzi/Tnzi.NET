/**
 * @tnzi/core/headless/form
 *
 * Form controller - reactive headless logic.
 */

import { reactive, toRaw } from '@vue/reactivity';
import { deepEqual } from '../utils/deep-equal';
import type { z } from 'zod';

// ============================================
// Types
// ============================================

export interface FormFieldError {
  field: string;
  message: string;
}

export interface FormOptions<T extends Record<string, unknown>> {
  /** Initial values */
  initialValues: T;
  /** Zod schema (optional, for validation) */
  schema?: z.ZodType<T>;
  /** Submit callback */
  onSubmit?: (values: T) => Promise<void> | void;
}

// ============================================
// FormController
// ============================================

/**
 * Reactive form controller.
 *
 * ```ts
 * const form = new FormController({
 *   initialValues: { name: '', email: '' },
 *   schema: mySchema,
 *   onSubmit: async (values) => { await api.save(values); },
 * });
 *
 * form.values.name = 'test';
 * form.isDirty  // true
 * await form.submit();
 * ```
 */
export class FormController<T extends Record<string, unknown>> {
  /** Current form values (reactive) */
  values: T;
  /** Field-level errors */
  errors: FormFieldError[];
  /** Whether the form is currently submitting */
  isSubmitting: boolean;
  /** Whether the form has been submitted */
  isSubmitted: boolean;
  /** Set of touched fields */
  touchedFields: Set<string>;

  private readonly _initialValues: T;
  private readonly _schema?: z.ZodType<T>;
  private readonly _onSubmit?: (values: T) => Promise<void> | void;

  constructor(options: FormOptions<T>) {
    this._initialValues = structuredClone(options.initialValues);
    this._schema = options.schema;
    this._onSubmit = options.onSubmit;
    this.values = structuredClone(options.initialValues);
    this.errors = [];
    this.isSubmitting = false;
    this.isSubmitted = false;
    this.touchedFields = new Set<string>();
    return reactive(this) as this;
  }

  // Getters
  get isDirty(): boolean {
    return !deepEqual(this.values, this._initialValues);
  }

  get isValid(): boolean {
    return this.errors.length === 0;
  }

  get hasErrors(): boolean {
    return this.errors.length > 0;
  }

  get canSubmit(): boolean {
    return !this.isSubmitting && this.isDirty && !this.hasErrors;
  }

  // Actions

  /** Set a field value */
  setFieldValue<K extends keyof T>(field: K, value: T[K]): void {
    this.values[field] = value;
    this.touchedFields.add(field as string);
    // Clear errors for this field
    this.errors = this.errors.filter(e => e.field !== field);
  }

  /** Mark a field as touched */
  touchField(field: string): void {
    this.touchedFields.add(field);
  }

  /** Get error message for a field */
  getFieldError(field: string): string | null {
    return this.errors.find(e => e.field === field)?.message ?? null;
  }

  /** Check if a field has been touched */
  isFieldTouched(field: string): boolean {
    return this.touchedFields.has(field);
  }

  /** Validate the form */
  validate(): boolean {
    this.errors = [];

    if (this._schema) {
      const result = this._schema.safeParse(this.values);
      if (!result.success) {
        this.errors = result.error.issues.map(issue => ({
          field: issue.path.join('.'),
          message: issue.message,
        }));
        return false;
      }
    }

    return true;
  }

  /** Validate a single field (uses field-level pick when schema supports it) */
  validateField(field: string): boolean {
    if (!this._schema) return true;

    // Clear old errors for this field
    this.errors = this.errors.filter(e => e.field !== field);

    // Try field-level validation via ZodObject.pick for efficiency
    const schema = this._schema as unknown as Record<string, unknown>;
    if (typeof schema.pick === 'function') {
      try {
        const fieldSchema = (schema.pick as (mask: Record<string, true>) => z.ZodType<unknown>)({ [field]: true });
        const result = fieldSchema.safeParse({ [field]: (this.values as Record<string, unknown>)[field] });
        if (!result.success) {
          const fieldErrors = result.error.issues.map(issue => ({
            field,
            message: issue.message,
          }));
          this.errors.push(...fieldErrors);
          return false;
        }
        return true;
      } catch {
        // Fall through to full schema validation
      }
    }

    // Fallback: validate entire schema and filter to target field
    const result = this._schema.safeParse(this.values);
    if (!result.success) {
      const fieldErrors = result.error.issues
        .filter(issue => issue.path.join('.') === field)
        .map(issue => ({ field, message: issue.message }));
      this.errors.push(...fieldErrors);
      return fieldErrors.length === 0;
    }

    return true;
  }

  /** Submit the form */
  async submit(): Promise<boolean> {
    this.isSubmitted = true;

    if (!this.validate()) return false;
    if (!this._onSubmit) return true;

    this.isSubmitting = true;
    try {
      await this._onSubmit(this.values);
      return true;
    } catch (error) {
      if (error instanceof Error) {
        this.errors.push({ field: '_form', message: error.message });
      }
      return false;
    } finally {
      this.isSubmitting = false;
    }
  }

  /** Reset to initial values */
  reset(): void {
    this.values = structuredClone(toRaw(this._initialValues));
    this.errors = [];
    this.isSubmitting = false;
    this.isSubmitted = false;
    this.touchedFields.clear();
  }

  /** Set errors from server response */
  setErrors(errors: FormFieldError[]): void {
    this.errors = errors;
  }

  /** Set form values (batch) */
  setValues(values: Partial<T>): void {
    Object.assign(this.values, values);
  }
}
