/**
 * @tnzi/core/adapters/validation/zod
 *
 * Zod validation adapter.
 * Creates Zod schemas from declarative rule objects.
 */

import { z } from 'zod';

/**
 * Field type for schema generation.
 */
export type FieldType = 'string' | 'number' | 'boolean' | 'date';

/**
 * Field rule definition for schema generation.
 */
export interface FieldRule {
  /** Field type (default: 'string') */
  type?: FieldType;
  /** Whether the field is required */
  required?: boolean;
  /** Custom error message for required validation */
  requiredMessage?: string;
  /** Minimum length (string) or minimum value (number) */
  min?: number;
  /** Maximum length (string) or maximum value (number) */
  max?: number;
  /** Regular expression pattern (string only) */
  pattern?: RegExp;
  /** Validate as email (string only) */
  email?: boolean;
  /** Validate as URL (string only) */
  url?: boolean;
}

/**
 * Create a Zod schema from a declarative rules object.
 *
 * Rules are properly chained so all constraints apply to the same field.
 *
 * @example
 * ```ts
 * const schema = createZodSchema<{ name: string; age: number; email: string }>({
 *   name: { required: true, min: 2, max: 50 },
 *   age: { type: 'number', required: true, min: 0, max: 150 },
 *   email: { required: true, email: true },
 * });
 * ```
 */
export function createZodSchema<T extends Record<string, unknown>>(
  rules: { [K in keyof T]?: FieldRule },
): z.ZodObject<Record<string, z.ZodTypeAny>> {
  const shape: Record<string, z.ZodTypeAny> = {};

  for (const [key, rule] of Object.entries(rules)) {
    if (!rule) continue;
    shape[key] = buildFieldSchema(key, rule);
  }

  return z.object(shape);
}

/**
 * Build a single field schema by chaining all rules on the same instance.
 */
function buildFieldSchema(key: string, rule: FieldRule): z.ZodTypeAny {
  const fieldType = rule.type ?? 'string';

  switch (fieldType) {
    case 'number':
      return buildNumberSchema(key, rule);
    case 'boolean':
      return buildBooleanSchema(rule);
    case 'date':
      return buildDateSchema(rule);
    case 'string':
    default:
      return buildStringSchema(key, rule);
  }
}

function buildStringSchema(key: string, rule: FieldRule): z.ZodTypeAny {
  let schema = z.string({
    required_error: rule.requiredMessage ?? `${key} is required`,
  });

  if (rule.min !== undefined) {
    schema = schema.min(rule.min, `${key} must be at least ${rule.min} characters`);
  }
  if (rule.max !== undefined) {
    schema = schema.max(rule.max, `${key} must be at most ${rule.max} characters`);
  }
  if (rule.pattern) {
    schema = schema.regex(rule.pattern, `${key} format is invalid`);
  }
  if (rule.email) {
    schema = schema.email(`${key} must be a valid email`);
  }
  if (rule.url) {
    schema = schema.url(`${key} must be a valid URL`);
  }

  return rule.required ? schema : schema.optional();
}

function buildNumberSchema(key: string, rule: FieldRule): z.ZodTypeAny {
  let schema = z.number({
    required_error: rule.requiredMessage ?? `${key} is required`,
    invalid_type_error: `${key} must be a number`,
  });

  if (rule.min !== undefined) {
    schema = schema.min(rule.min, `${key} must be at least ${rule.min}`);
  }
  if (rule.max !== undefined) {
    schema = schema.max(rule.max, `${key} must be at most ${rule.max}`);
  }

  return rule.required ? schema : schema.optional();
}

function buildBooleanSchema(rule: FieldRule): z.ZodTypeAny {
  const schema = z.boolean();
  return rule.required ? schema : schema.optional();
}

function buildDateSchema(rule: FieldRule): z.ZodTypeAny {
  const schema = z.date();
  return rule.required ? schema : schema.optional();
}

export { z };
