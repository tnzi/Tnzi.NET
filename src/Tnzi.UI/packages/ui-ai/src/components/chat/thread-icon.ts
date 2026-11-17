/**
 * A conversation's icon in the thread lists: the product maps a thread (usually its
 * `origin`, which the server stores when the thread is created) to an icon and a
 * label, or to nothing.
 *
 * One resolver serves the sidebar (`TThreadList`), the collapsed rail's flyout
 * (`TRailThreadList`) and the Recents page (`TRecentsPage`), so a conversation
 * looks the same in all three; `TChatApp` takes it as `threadIcon` and hands it
 * to each.
 */
import type { ThreadItem } from './TThreadList.vue'

/** What a row shows at its end, in the place of its "..." menu. */
export interface ThreadIcon {
  /** Iconify name; the application bundles it (the framework does not ship product icons). */
  readonly icon: string
  /** Tooltip and accessible name, e.g. "From an email rule". */
  readonly label: string
}

/**
 * Maps a thread to its icon. Return null (or undefined) for none: the row's end
 * is then empty at rest.
 */
export type ThreadIconResolver = (thread: ThreadItem) => ThreadIcon | null | undefined

/**
 * The icon a row shows, or null. A resolver that answers without an icon name
 * counts as none, and one that throws costs its row the icon rather than the
 * whole list its render.
 */
export function resolveThreadIcon(resolver: ThreadIconResolver | undefined, thread: ThreadItem): ThreadIcon | null {
  if (!resolver) return null
  let result: ThreadIcon | null | undefined
  try {
    result = resolver(thread)
  } catch {
    return null
  }
  if (!result || !result.icon) return null
  return { icon: result.icon, label: result.label ?? '' }
}
