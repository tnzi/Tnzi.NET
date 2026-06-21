/**
 * Chat time formatting utilities.
 * - Today → HH:mm
 * - Yesterday → yesterdayLabel (i18n-supplied; defaults to 'Yesterday')
 * - Earlier → MM/DD
 * - null/undefined/empty → ''
 */

export function formatChatTime(iso: string | null | undefined, yesterdayLabel = 'Yesterday'): string {
  if (!iso) return ''
  const date = new Date(iso)
  if (isNaN(date.getTime())) return ''

  const now = new Date()

  // Strip to midnight for comparison
  const today = new Date(now.getFullYear(), now.getMonth(), now.getDate())
  const yesterday = new Date(today.getTime() - 86400000)
  const msgDay = new Date(date.getFullYear(), date.getMonth(), date.getDate())

  if (msgDay.getTime() === today.getTime()) {
    const hh = String(date.getHours()).padStart(2, '0')
    const mm = String(date.getMinutes()).padStart(2, '0')
    return `${hh}:${mm}`
  }

  if (msgDay.getTime() === yesterday.getTime()) {
    return yesterdayLabel
  }

  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')
  return `${month}/${day}`
}

/**
 * Full timestamp shown as a centered separator inside a thread:
 * - Today → HH:mm
 * - Yesterday → "<yesterdayLabel> HH:mm"
 * - This year → MM/DD HH:mm
 * - Earlier → YYYY/MM/DD HH:mm
 */
export function formatMessageSeparator(iso: string | null | undefined, yesterdayLabel = 'Yesterday'): string {
  if (!iso) return ''
  const date = new Date(iso)
  if (isNaN(date.getTime())) return ''

  const now = new Date()
  const today = new Date(now.getFullYear(), now.getMonth(), now.getDate())
  const yesterday = new Date(today.getTime() - 86400000)
  const msgDay = new Date(date.getFullYear(), date.getMonth(), date.getDate())
  const hh = String(date.getHours()).padStart(2, '0')
  const mm = String(date.getMinutes()).padStart(2, '0')
  const time = `${hh}:${mm}`

  if (msgDay.getTime() === today.getTime()) return time
  if (msgDay.getTime() === yesterday.getTime()) return `${yesterdayLabel} ${time}`

  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')
  if (date.getFullYear() === now.getFullYear()) return `${month}/${day} ${time}`
  return `${date.getFullYear()}/${month}/${day} ${time}`
}

/** Decide whether a time separator should precede a message given the previous one. */
export function shouldShowSeparator(
  prevIso: string | null | undefined,
  currIso: string | null | undefined,
  gapMinutes = 5,
): boolean {
  if (!currIso) return false
  if (!prevIso) return true
  const prev = new Date(prevIso).getTime()
  const curr = new Date(currIso).getTime()
  if (isNaN(prev) || isNaN(curr)) return false
  return curr - prev >= gapMinutes * 60000
}
