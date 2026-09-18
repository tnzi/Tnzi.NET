/**
 * Template Module Metadata
 */

/**
 * Template variable type
 */
export type TemplateVariableType = 'string' | 'number' | 'boolean' | 'date' | 'object' | 'array';

/**
 * Rendering-surface classification of a template.
 * Aligned with backend `TemplateType` (serialized by member name).
 *
 * `Sms` is load-bearing: the render service emits the body as plain text
 * (no HTML encoding) only for this type; every other type is HTML-encoded.
 */
export enum TemplateType {
  Generic = 'Generic',
  Email = 'Email',
  Sms = 'Sms',
  Page = 'Page',
  Print = 'Print',
  Pdf = 'Pdf',
}
