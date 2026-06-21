/** Build the preview URL for a stored file (avatar, image or file message). */
export function resolveChatAvatarUrl(fileId?: string | null): string | null {
  return fileId ? `/api/files/${fileId}/preview` : null
}

/**
 * Muted, desaturated palette for initials-avatars. White text reads cleanly on
 * every entry. Kept neutral on purpose — the chat window is its own visual world
 * and should not borrow the admin theme's accent (which can be a loud teal/purple).
 */
const AVATAR_COLORS = [
  '#5b8c9e', // steel blue
  '#6b8e7f', // sage
  '#a8829e', // mauve
  '#c2906b', // clay
  '#7e8bb0', // periwinkle
  '#8f9a5b', // olive
  '#b07e8a', // rosewood
  '#5fa3a3', // teal
  '#9a8bb5', // lavender
  '#c79a5b', // amber
]

/** Deterministic avatar background for a seed (user id or name). Stable across renders. */
export function avatarColor(seed?: string | null): string {
  const fallback = AVATAR_COLORS[0] ?? '#5b8c9e'
  const s = (seed ?? '').trim()
  if (!s) return fallback
  let hash = 0
  for (let i = 0; i < s.length; i++) hash = (hash * 31 + s.charCodeAt(i)) >>> 0
  return AVATAR_COLORS[hash % AVATAR_COLORS.length] ?? fallback
}

/** First visible character of a name, upper-cased; falls back to '?'. */
export function avatarInitial(name?: string | null): string {
  const ch = (name ?? '').trim().charAt(0)
  return ch ? ch.toUpperCase() : '?'
}
