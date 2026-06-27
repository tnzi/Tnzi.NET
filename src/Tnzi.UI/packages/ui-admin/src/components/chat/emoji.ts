/**
 * Curated set of common emojis for the composer's emoji picker.
 *
 * Hand-picked (not a full Unicode table) so the popover stays a quick,
 * scannable grid — smileys, gestures, hearts and a few symbols cover the
 * overwhelming majority of everyday chat use. No external dependency: a flat
 * string array keeps the bundle tiny and avoids the Vite optimize-deps / pnpm
 * link friction that a third-party emoji library would introduce.
 */
export const COMMON_EMOJIS: readonly string[] = [
  // Smileys & emotion
  '😀', '😃', '😄', '😁', '😆', '😅', '😂', '🤣', '😊', '😇',
  '🙂', '🙃', '😉', '😌', '😍', '🥰', '😘', '😗', '😙', '😚',
  '😋', '😛', '😝', '😜', '🤪', '🤨', '🧐', '🤓', '😎', '🥳',
  '😏', '😒', '😞', '😔', '😟', '😕', '🙁', '😣', '😖', '😫',
  '😩', '🥺', '😢', '😭', '😤', '😠', '😡', '🤯', '😳', '🥵',
  '🥶', '😱', '😨', '😰', '😥', '😓', '🤗', '🤔', '🤭', '🤫',
  '😶', '😐', '😑', '😬', '🙄', '😮', '😲', '🥱', '😴', '🤤',
  '😪', '😵', '🤐', '🥴', '🤢', '🤮', '🤧', '😷', '🤒', '🤕',
  // Gestures & people
  '👍', '👎', '👌', '✌️', '🤞', '🤟', '🤙', '👏', '🙌', '🙏',
  '👋', '🤝', '💪', '🫶', '🤲', '👀',
  // Hearts
  '❤️', '🧡', '💛', '💚', '💙', '💜', '🖤', '🤍', '💔', '💕',
  '💞', '💓', '💗', '💖', '💝',
  // Symbols & objects
  '🎉', '🎊', '✨', '🔥', '⭐', '🌟', '💯', '✅', '❌', '❓',
  '❗', '💡', '🎁', '☕', '🍺', '🌹', '👌',
]

// ── Recently-used tracking (localStorage, frequency-ranked) ──────────────────
const USAGE_KEY = 'tnzi:chat:emoji-usage'

function readUsage(): Record<string, number> {
  try {
    const raw = localStorage.getItem(USAGE_KEY)
    const parsed = raw ? JSON.parse(raw) : {}
    return parsed && typeof parsed === 'object' ? (parsed as Record<string, number>) : {}
  } catch {
    return {}
  }
}

/** Bump an emoji's use count so it surfaces in the "frequently used" group. */
export function recordEmojiUse(emoji: string): void {
  try {
    const usage = readUsage()
    usage[emoji] = (usage[emoji] ?? 0) + 1
    localStorage.setItem(USAGE_KEY, JSON.stringify(usage))
  } catch {
    /* storage unavailable (private mode / quota) — recents just won't persist */
  }
}

/** The most-used emojis, highest first, capped at `limit`. */
export function getFrequentEmojis(limit = 16): string[] {
  const usage = readUsage()
  return Object.keys(usage)
    .filter((e) => (usage[e] ?? 0) > 0)
    .sort((a, b) => (usage[b] ?? 0) - (usage[a] ?? 0))
    .slice(0, limit)
}
