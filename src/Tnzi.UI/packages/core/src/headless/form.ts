/**
 * @tnzi/core/headless/form
 *
 * Form controller - reactive headless logic.
 */

import { reactive, toRaw } from '@vue/reactivity';
import { deepEqual } from '../utils/deep-equal';
import { useLogger } from '../adapters/logger';
import type { z } from 'zod';

// ============================================
// Helpers
// ============================================

/**
 * Deep clone with structuredClone, falling back to JSON round-trip
 * for values that structuredClone cannot handle (e.g., File, Blob, Symbol).
 */
function safeClone<T>(value: T): T {
  try {
    return structuredClone(value);
  } catch {
    useLogger().warn('[FormController] structuredClone failed, falling back to JSON round-trip');
    return JSON.parse(JSON.stringify(value));
  }
}

/** Best-effort message for anything that can be thrown, not just `Error`. */
function toErrorMessage(error: unknown): string {
  if (error instanceof Error) return error.message;
  if (typeof error === 'string' && error) return error;
  if (error && typeof error === 'object' && 'message' in error) {
    const message = (error as { message?: unknown }).message;
    if (typeof message === 'string' && message) return message;
  }
  return 'Submit failed';
}

// ============================================
// Types
// ============================================

export interface FormFieldError {
  field: string;
  message: string;
}

export interface FormOptions<T extends object> {
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
export class FormController<T extends object> {
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
    this._initialValues = safeClone(options.initialValues);
    this._schema = options.schema;
    this._onSubmit = options.onSubmit;
    this.values = safeClone(options.initialValues);
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
      // Record every failure, not just `Error` instances: a thrown string or a
      // rejected non-Error used to leave `errors` empty, so the form reported
      // "failed" with nothing to show the user.
      this.errors.push({ field: '_form', message: toErrorMessage(error) });
      return false;
    } finally {
      this.isSubmitting = false;
    }
  }

  /** Reset to initial values */
  reset(): void {
    this.values = safeClone(toRaw(this._initialValues));
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

// ============================================
// Form State Types (from types/stores.ts)
// ============================================

/**
 * Form field state
 */
export interface FormFieldState<T = unknown> {
  value: T;
  touched: boolean;
  dirty: boolean;
  error?: string;
}

/**
 * Form state
 */
export interface FormState<T extends Record<string, unknown>> {
  values: T;
  touched: Record<keyof T, boolean>;
  dirty: Record<keyof T, boolean>;
  errors: Partial<Record<keyof T, string>>;
  isValid: boolean;
  isSubmitting: boolean;
  isDirty: boolean;
}
