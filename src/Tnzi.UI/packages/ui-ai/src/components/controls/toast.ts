/**
 * Short-lived notes at the top of the screen ("Saved", "Could not wake the
 * computer: ..."). Pages call `useToast()` and the app's one `TToastHost`
 * (mounted by `TChatApp`; an app with its own shell mounts it once) shows them.
 *
 * Drawn by this package, not by naive-ui's message provider, so it works in an
 * app built on `@tnzi/ui-ai/controls` alone. A note leaves by itself after
 * `TOAST_DURATION_MS`, an error after `TOAST_ERROR_DURATION_MS` since it
 * usually says what to do next; pointing at a note or focusing it holds it.
 * At most `TOAST_MAX_VISIBLE_WIDE` (3) are on screen at once, and on a phone
 * `TOAST_MAX_VISIBLE_NARROW` (1), since two stacked notes cover the page's
 * header there; past that the oldest makes room. The host sets which applies
 * (`setMaxVisible`) from the viewport.
 *
 * A note can carry up to two actions ("Undo", "Send now"). An action's button
 * runs it and takes the note away, unless the action says `keepOpen` (the
 * note then stays for the action to change it). A note with actions stays
 * `TOAST_ACTION_DURATION_MS` (6 s) unless told otherwise, so there is time to
 * reach them.
 *
 * Each call returns a handle: `update()` changes what the note says, its
 * tone, its actions, whether it shows a spinner (`busy`) and how long it still
 * stays; `dismiss()` takes it away. That is enough for a note that counts
 * down and then reports progress ("Sending in 5 s" with Undo / Send now, then
 * "Sending..." with a spinner, then "Sent"): the page owns the clock and the
 * sending, the note only shows where it stands. A countdown's ticks pass
 * `quiet: true`, so a screen reader hears the first words and the outcome,
 * not every second.
 *
 * `useToast()` works outside a component too (a store, a request helper): it
 * talks to the package-wide toaster, which is what a host shows unless it is
 * given another (`createToaster()`, for a test or a second, separate surface).
 */
import { shallowRef, type ShallowRef } from 'vue'

export type ToastTone = 'success' | 'info' | 'warning' | 'error'

/** One of at most two actions on a note ("Undo", "Send now"). */
export interface ToastAction {
  /** The button's words. */
  readonly label: string
  /** What it does. */
  readonly run: () => unknown
  /** The note stays when it is pressed (the action updates it); by default it goes away. */
  readonly keepOpen?: boolean
}

/** At most this many actions on one note. */
export const TOAST_MAX_ACTIONS = 2

export interface ToastItem {
  readonly id: number
  readonly tone: ToastTone
  readonly text: string
  /** Milliseconds on screen; 0 stays until dismissed. */
  readonly duration: number
  readonly actions: readonly ToastAction[]
  /** Work in progress: a spinner in place of the tone's icon. */
  readonly busy: boolean
  /** The last change of text is not announced (a countdown's tick). */
  readonly quiet: boolean
}

export interface ToastOptions {
  /** Milliseconds on screen; 0 stays until dismissed. Defaults by tone, or to `TOAST_ACTION_DURATION_MS` with actions. */
  duration?: number
  /** Up to two buttons on the note. */
  actions?: readonly ToastAction[]
  /** Work in progress: a spinner in place of the tone's icon. */
  busy?: boolean
}

/** What a note can change into while it is on screen. */
export interface ToastUpdate {
  text?: string
  tone?: ToastTone
  actions?: readonly ToastAction[]
  busy?: boolean
  /** Milliseconds from now; 0 stays until dismissed. Unset keeps the time it had left. */
  duration?: number
  /** Do not announce this change of text (a countdown's tick). */
  quiet?: boolean
}

/** One note on screen, as the call that raised it holds it. */
export interface ToastHandle {
  readonly id: number
  /** Changes the note; does nothing once it has gone. */
  update(changes: ToastUpdate): void
  dismiss(): void
  /** Whether the note is still on screen. */
  readonly shown: boolean
}

/** What a page calls. Each returns the note's handle. */
export interface Toast {
  success(text: string, options?: ToastOptions): ToastHandle
  info(text: string, options?: ToastOptions): ToastHandle
  warning(text: string, options?: ToastOptions): ToastHandle
  error(text: string, options?: ToastOptions): ToastHandle
  dismiss(note: number | ToastHandle): void
  /** Takes every note off the screen. */
  clear(): void
}

export interface Toaster {
  /** The notes on screen, oldest first. */
  readonly items: Readonly<ShallowRef<readonly ToastItem[]>>
  readonly toast: Toast
  show(tone: ToastTone, text: string, options?: ToastOptions): ToastHandle
  update(id: number, changes: ToastUpdate): void
  dismiss(id: number): void
  /** How many notes may be on screen at once (at least 1); the oldest beyond it leave now and on every new one. */
  readonly maxVisible: Readonly<ShallowRef<number>>
  setMaxVisible(count: number): void
  /** Holds a note's timer (pointer over it, focus in it); `resume` lets it run out what was left. */
  pause(id: number): void
  resume(id: number): void
  /**
   * The host that draws this toaster. The first host mounted owns it and the
   * others draw nothing, so a second host (an app that mounts its own beside
   * `TChatApp`'s) does not show every note twice.
   */
  readonly owner: Readonly<ShallowRef<symbol | null>>
  claim(host: symbol): void
  release(host: symbol): void
}

export const TOAST_DURATION_MS = 3000
export const TOAST_ERROR_DURATION_MS = 6000
/** How long a note with an action stays: long enough to reach its button. */
export const TOAST_ACTION_DURATION_MS = 6000
/** Notes on screen at once on a wide screen; past this many, the oldest makes room. */
export const TOAST_MAX_VISIBLE_WIDE = 3
/** Notes on screen at once on a phone: a new one replaces the one shown. */
export const TOAST_MAX_VISIBLE_NARROW = 1

interface Timer {
  handle: ReturnType<typeof setTimeout> | null
  remaining: number
  startedAt: number
}

export function createToaster(): Toaster {
  const items = shallowRef<readonly ToastItem[]>([])
  const owner = shallowRef<symbol | null>(null)
  const maxVisible = shallowRef(TOAST_MAX_VISIBLE_WIDE)
  const claims: symbol[] = []
  const timers = new Map<number, Timer>()
  /* Notes held by the pointer or focus: a new duration waits for them to be let go. */
  const held = new Set<number>()
  let nextId = 1

  function dismiss(id: number): void {
    held.delete(id)
    const timer = timers.get(id)
    if (timer?.handle) clearTimeout(timer.handle)
    timers.delete(id)
    if (items.value.some((item) => item.id === id)) items.value = items.value.filter((item) => item.id !== id)
  }

  function run(id: number, timer: Timer): void {
    timer.startedAt = Date.now()
    timer.handle = setTimeout(() => dismiss(id), timer.remaining)
  }

  /** (Re)starts a note's timer for `duration` ms from now; 0 stops it. */
  function startTimer(id: number, duration: number): void {
    const old = timers.get(id)
    if (old?.handle) clearTimeout(old.handle)
    timers.delete(id)
    if (duration <= 0) return
    const timer: Timer = { handle: null, remaining: duration, startedAt: 0 }
    timers.set(id, timer)
    if (!held.has(id)) run(id, timer)
  }

  function checkedActions(actions: readonly ToastAction[] | undefined): readonly ToastAction[] {
    const list = actions ?? []
    if (list.length > TOAST_MAX_ACTIONS) throw new RangeError(`A toast takes at most ${TOAST_MAX_ACTIONS} actions; got ${list.length}.`)
    return list
  }

  function handleFor(id: number): ToastHandle {
    return {
      id,
      update: (changes) => update(id, changes),
      dismiss: () => dismiss(id),
      get shown() {
        return items.value.some((item) => item.id === id)
      },
    }
  }

  function show(tone: ToastTone, text: string, options: ToastOptions = {}): ToastHandle {
    const actions = checkedActions(options.actions)
    const id = nextId++
    const fallback = actions.length > 0
      ? Math.max(TOAST_ACTION_DURATION_MS, tone === 'error' ? TOAST_ERROR_DURATION_MS : 0)
      : tone === 'error' ? TOAST_ERROR_DURATION_MS : TOAST_DURATION_MS
    const duration = options.duration ?? fallback
    makeRoom(1)
    const item: ToastItem = { id, tone, text, duration, actions, busy: options.busy ?? false, quiet: false }
    items.value = [...items.value, item]
    startTimer(id, duration)
    return handleFor(id)
  }

  function update(id: number, changes: ToastUpdate): void {
    const current = items.value.find((item) => item.id === id)
    if (!current) return
    const next: ToastItem = {
      ...current,
      text: changes.text ?? current.text,
      tone: changes.tone ?? current.tone,
      actions: changes.actions ? checkedActions(changes.actions) : current.actions,
      busy: changes.busy ?? current.busy,
      duration: changes.duration ?? current.duration,
      quiet: changes.text !== undefined ? changes.quiet === true : current.quiet,
    }
    items.value = items.value.map((item) => (item.id === id ? next : item))
    if (changes.duration !== undefined) startTimer(id, changes.duration)
  }

  /** Dismisses the oldest notes so that `incoming` more still fit under the limit. */
  function makeRoom(incoming: number): void {
    const excess = items.value.length + incoming - maxVisible.value
    if (excess > 0) for (const old of items.value.slice(0, excess)) dismiss(old.id)
  }

  function setMaxVisible(count: number): void {
    maxVisible.value = Math.max(1, Math.floor(count))
    makeRoom(0)
  }

  function pause(id: number): void {
    if (items.value.some((item) => item.id === id)) held.add(id)
    const timer = timers.get(id)
    if (!timer?.handle) return
    clearTimeout(timer.handle)
    timer.handle = null
    timer.remaining = Math.max(0, timer.remaining - (Date.now() - timer.startedAt))
  }

  function resume(id: number): void {
    held.delete(id)
    const timer = timers.get(id)
    if (!timer || timer.handle) return
    run(id, timer)
  }

  function clear(): void {
    for (const item of items.value) dismiss(item.id)
  }

  return {
    items,
    owner,
    maxVisible,
    setMaxVisible,
    show,
    update,
    dismiss,
    pause,
    resume,
    claim(host) {
      if (!claims.includes(host)) claims.push(host)
      owner.value = claims[0] ?? null
    },
    release(host) {
      const index = claims.indexOf(host)
      if (index >= 0) claims.splice(index, 1)
      owner.value = claims[0] ?? null
    },
    toast: {
      success: (text, options) => show('success', text, options),
      info: (text, options) => show('info', text, options),
      warning: (text, options) => show('warning', text, options),
      error: (text, options) => show('error', text, options),
      dismiss: (note) => dismiss(typeof note === 'number' ? note : note.id),
      clear,
    },
  }
}

/** The package-wide toaster: what `useToast()` talks to and a host shows by default. */
export const toaster: Toaster = createToaster()

/** The app's notes. Usable in and outside components. */
export function useToast(): Toast {
  return toaster.toast
}
