/**
 * Whether a text field counts as not filled in: empty, missing, or only
 * whitespace. The backend's [Required] refuses a whitespace-only string, so a
 * form that treats one as filled only moves the error from the field to a 400.
 * The check never trims the value itself: what the user typed is submitted.
 */
export function isBlank(value: string | null | undefined): boolean {
  return (value ?? '').trim() === ''
}
