import { computed, inject, provide, ref, toValue, type ComputedRef, type InjectionKey, type MaybeRefOrGetter } from 'vue'

/** What the panel remembers for one conversation. Every field is optional: a missing one falls back to the default. */
interface AsideEntry {
  readonly open?: boolean
  readonly width?: number
  readonly tab?: string
}

export interface UseAsidePanelOptions {
  /**
   * The conversation the state belongs to. Empty (a conversation that has no id yet) uses a shared
   * draft slot, which is handed to the real id by `adoptDraft` once the id exists.
   */
  key?: MaybeRefOrGetter<string | null | undefined>
  /** localStorage key the per-conversation map is kept under. `null` keeps it in memory only. */
  storageKey?: string | null
  /** Whether a conversation that was never opened or closed starts with the panel open. */
  defaultOpen?: boolean
  defaultWidth?: number
  minWidth?: number
  maxWidth?: number
  /** The tab a conversation starts on. */
  defaultTab?: string
  /** How many conversations are remembered; the least recently changed are forgotten first. */
  maxEntries?: number
}

export interface UseAsidePanelReturn {
  open: ComputedRef<boolean>
  width: ComputedRef<number>
  tab: ComputedRef<string | undefined>
  minWidth: number
  maxWidth: number
  setOpen: (open: boolean) => void
  toggle: () => void
  /** Open the panel, optionally on a given tab. */
  show: (tab?: string) => void
  hide: () => void
  setWidth: (width: number) => void
  setTab: (tab: string) => void
  /** Move what the draft slot holds to `key`, and clear the draft. A no-op when the draft is untouched. */
  adoptDraft: (key: string) => void
}

/** The part of the panel's state a product can drive from inside the conversation (a tool call row, for instance). */
export interface ChatAsideControl {
  readonly open: ComputedRef<boolean>
  readonly tab: ComputedRef<string | undefined>
  show: (tab?: string) => void
  hide: () => void
  toggle: () => void
  setTab: (tab: string) => void
}

export const CHAT_ASIDE_KEY: InjectionKey<ChatAsideControl> = Symbol('tnzi-chat-aside')

export function provideChatAside(control: ChatAsideControl): void {
  provide(CHAT_ASIDE_KEY, control)
}

/**
 * The right-side panel of the surrounding `TChatApp`, or `null` when there is none (or it has no
 * `aside` content). Lets a renderer inside the conversation open the panel: a "show changes" button
 * on a tool call calls `useChatAside()?.show('changes')`.
 */
export function useChatAside(): ChatAsideControl | null {
  return inject(CHAT_ASIDE_KEY, null)
}

const DRAFT_KEY = '\u0000draft'
const DEFAULT_STORAGE_KEY = 'tnzi-ui-ai-aside'

function readStore(storageKey: string | null): Record<string, AsideEntry> {
  if (!storageKey || typeof localStorage === 'undefined') return {}
  try {
    const parsed: unknown = JSON.parse(localStorage.getItem(storageKey) ?? '{}')
    if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) return {}
    const clean: Record<string, AsideEntry> = {}
    for (const [id, raw] of Object.entries(parsed as Record<string, unknown>)) {
      if (!raw || typeof raw !== 'object') continue
      const entry = raw as Record<string, unknown>
      clean[id] = {
        ...(typeof entry.open === 'boolean' ? { open: entry.open } : {}),
        ...(typeof entry.width === 'number' && Number.isFinite(entry.width) ? { width: entry.width } : {}),
        ...(typeof entry.tab === 'string' ? { tab: entry.tab } : {}),
      }
    }
    return clean
  } catch {
    return {}
  }
}

function writeStore(storageKey: string | null, store: Record<string, AsideEntry>): void {
  if (!storageKey || typeof localStorage === 'undefined') return
  try {
    // The draft slot is per page view; it is never worth carrying into a later session.
    const { [DRAFT_KEY]: _draft, ...persisted } = store
    localStorage.setItem(storageKey, JSON.stringify(persisted))
  } catch {
    // Storage is blocked or full: the choice still holds for this page.
  }
}

/**
 * @experimental
 * Open / width / active-tab state of a chat's right-side panel, remembered per conversation.
 *
 * A conversation that was never touched uses the defaults (`defaultOpen`, `defaultWidth`,
 * `defaultTab`); the first change to any of the three records an entry for it. Switching the `key`
 * switches the state with it, so opening a panel in one conversation does not open it in another.
 * State is held immutably: each change replaces the map.
 */
export function useAsidePanel(options: UseAsidePanelOptions = {}): UseAsidePanelReturn {
  const storageKey = options.storageKey === undefined ? DEFAULT_STORAGE_KEY : options.storageKey
  const defaultOpen = options.defaultOpen ?? false
  const minWidth = options.minWidth ?? 280
  const maxWidth = Math.max(minWidth, options.maxWidth ?? 720)
  const clampWidth = (w: number): number => Math.round(Math.min(maxWidth, Math.max(minWidth, w)))
  const defaultWidth = clampWidth(options.defaultWidth ?? 420)
  const maxEntries = Math.max(1, options.maxEntries ?? 200)

  const store = ref<Record<string, AsideEntry>>(readStore(storageKey))
  const currentKey = computed(() => toValue(options.key) || DRAFT_KEY)
  const entry = computed<AsideEntry | undefined>(() => store.value[currentKey.value])

  const open = computed(() => entry.value?.open ?? defaultOpen)
  const width = computed(() => clampWidth(entry.value?.width ?? defaultWidth))
  const tab = computed(() => entry.value?.tab ?? options.defaultTab)

  function commit(next: Record<string, AsideEntry>): void {
    const ids = Object.keys(next)
    const trimmed = ids.length > maxEntries
      ? Object.fromEntries(ids.slice(ids.length - maxEntries).map((id) => [id, next[id]!]))
      : next
    store.value = trimmed
    writeStore(storageKey, trimmed)
  }

  function patch(change: AsideEntry): void {
    const id = currentKey.value
    // Re-inserting moves the conversation to the end, which is what "least recently changed" trims by.
    const { [id]: previous, ...others } = store.value
    commit({ ...others, [id]: { ...previous, ...change } })
  }

  function setOpen(next: boolean): void {
    if (next !== open.value || entry.value?.open === undefined) patch({ open: next })
  }

  function show(nextTab?: string): void {
    patch({ open: true, ...(nextTab !== undefined ? { tab: nextTab } : {}) })
  }

  function adoptDraft(key: string): void {
    const draft = store.value[DRAFT_KEY]
    if (!draft || !key) return
    const { [DRAFT_KEY]: _draft, [key]: existing, ...others } = store.value
    commit({ ...others, [key]: { ...draft, ...existing } })
  }

  return {
    open,
    width,
    tab,
    minWidth,
    maxWidth,
    setOpen,
    toggle: () => setOpen(!open.value),
    show,
    hide: () => setOpen(false),
    setWidth: (w) => patch({ width: clampWidth(w) }),
    setTab: (t) => patch({ tab: t }),
    adoptDraft,
  }
}
