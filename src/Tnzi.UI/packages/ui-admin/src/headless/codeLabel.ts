/**
 * Whether a code chip adds nothing next to a display name.
 *
 * Returns true when the name and code collapse to the same token once case and
 * separators are ignored — e.g. role "SuperAdmin" / "SUPERADMIN", module
 * "Identity" / "identity", or a surface whose label fell back to its own code.
 * Callers hide the secondary `<code>` chip / tag when this is true so the same
 * word is not printed twice; a code that genuinely differs (e.g. label "Users"
 * vs prefix "user", or a localized "身份管理" vs "identity") is kept because it
 * still carries information.
 */
export function isCodeRedundant(
  name: string | null | undefined,
  code: string | null | undefined,
): boolean {
  if (!name || !code) return false
  const norm = (s: string) => s.toLowerCase().replace(/[^a-z0-9]+/g, '')
  const n = norm(name)
  return n.length > 0 && n === norm(code)
}
