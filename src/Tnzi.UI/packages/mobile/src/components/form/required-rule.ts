import type { FieldRule } from 'vant'

/**
 * Vant's `required` counts '', null / undefined, an empty array and `false` as
 * missing, but a string of spaces as filled. The backend's [Required] refuses a
 * whitespace-only string, so a required rule looks at the trimmed text. The
 * formatter only feeds this rule's check: the submitted value is not trimmed.
 * Non-string values (a file list, a switch) pass through unchanged.
 */
function trimForRequired(value: unknown): unknown {
  return typeof value === 'string' ? value.trim() : value
}

/** A Vant required rule that treats a whitespace-only string as empty. */
export function requiredRule(message: string, trigger?: FieldRule['trigger']): FieldRule {
  return {
    required: true,
    message,
    formatter: trimForRequired as FieldRule['formatter'],
    ...(trigger ? { trigger } : {}),
  }
}
