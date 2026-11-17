/**
 * Back closes the top layer.
 *
 * On a phone the back button (Android) and the edge swipe (iOS) close what is
 * open over the page - a drawer, a sheet, a menu - before they leave the page,
 * one layer per press. A web page only hears about back through its history,
 * so each open layer that takes back owns one history entry: same address,
 * the page's own `history.state` (the router's) plus a marker. Back pops that
 * entry, and the layer closes through its own close path.
 *
 * - **Address and router untouched.** A layer's entry repeats the current
 *   address and copies the current `history.state`, so a router (`vue-router`'s
 *   `createWebHistory` included) finds its state on every entry; the only
 *   addition to any state is the marker key. A back between two entries with
 *   the same address is this module's alone: the `popstate` is stopped before
 *   the router's listener (capture phase, which runs first at the window), so
 *   no navigation and no guard runs for it.
 * - **Closed another way, the entry goes.** A layer closed by its close button,
 *   its backdrop, Escape or a choice takes its entry back out (`history.go`),
 *   so one back afterwards still reaches the previous page.
 * - **Nothing is undone by a navigation.** A history traversal is
 *   asynchronous and its target is fixed when it is asked for, so a
 *   `pushState` made while it is on its way would be undone by it (measured in
 *   Chromium: a router push made right after `history.back()` was lost). While
 *   one of this module's traversals is on its way, `pushState`,
 *   `replaceState`, `go`, `back` and `forward` are held and replayed, in
 *   order, once it has landed. That is what lets a menu or a drawer close and
 *   a page open from the same tap: the new page is pushed over the page the
 *   layer was opened on, with nothing stale between them.
 * - **One back, one layer, top first.** Back closes the layer whose entry it
 *   leaves. A layer that does not close (a dialog that cannot be closed, a
 *   `beforeClose` that says no) gets an entry again, so the page stays. A layer
 *   left open under a page pushed over it keeps its entry under that page:
 *   back returns to the page first, then closes the layer.
 * - **A layer that came with the address takes no entry.** When the address
 *   changed between the last tap or key press (or, before any, the start) and
 *   the moment the layer opened
 *   (a settings dialog kept as `?settings=` in the address, opened by a router
 *   push), back already closes it by going back over that address; a second
 *   entry would make it take two presses.
 * - **Stale entries are stepped over.** The entry of a layer that closed while
 *   it could not be taken out (a page had been pushed over it) is skipped when
 *   back or forward lands on it. A forward move that lands on one with nothing
 *   valid further on stays and keeps the entry as a page entry, so a forward
 *   gesture is never bounced back over and over.
 * - **No forward history on a narrow screen.** Taking a layer's entry out, or
 *   going back between pages, leaves entries forward of the current one, and
 *   iOS turns those into a forward edge swipe that lands on a stale entry or
 *   on the page just left. A page cannot delete forward entries, only a push
 *   drops them; so on a narrow screen (where layers take back) the module
 *   steps to the entry before the current one and pushes the current entry
 *   again, its `history.state` and address unchanged. That step is invisible:
 *   the push is made inside the step's own `popstate`, before any other code
 *   runs, so nothing ever reads the address or `history.state` of the entry
 *   stepped to (a router that reads them to rewrite the current entry would
 *   write them over the wrong one); the `popstate` reaches no other listener
 *   (no router navigation, no guard); the document's scroll is held where it
 *   was (the browser restores the scroll of the entry stepped to); calls made
 *   while the step is on its way are held as above. It only ever goes to an
 *   entry of this document the model knows. Closing a layer does it at once, in the same single step that takes
 *   the entry out; after a back it waits until the history has been still for
 *   a moment and no finger is down, so it never runs into a gesture or a quick
 *   series of backs.
 * - **The first entry.** On the first entry the module knows (where the page
 *   was loaded, or came back to from outside the model) there is no entry
 *   before it to step to. Where a forward edge swipe exists (iOS, iPadOS) the
 *   current entry is pushed again on top of itself: forward history is gone,
 *   and leaving the app by back takes one press more, which shows the same
 *   page. Elsewhere (no forward swipe) the forward entries stay there.
 *
 * To know which entry a `popstate` lands on, the module keeps a model of the
 * history from the moment it is first used, and stamps every entry created
 * after that (its own and the page's) with an id in the marker key.
 *
 * `createBackLayer` is the imperative form, `useBackLayer` binds one to an
 * open flag for a component. Both do nothing without `window` (SSR). Enable a
 * layer where back is expected to close it (a narrow screen): on a wide screen
 * back stays a page navigation.
 */

import { getCurrentScope, onScopeDispose, toValue, watch, type MaybeRefOrGetter } from 'vue'
import { openPopStateGate } from './popstate-gate'

/**
 * Called when back closes the layer. Close the layer through its usual path.
 * `false` (or a promise of it) keeps the layer open, and it takes an entry again.
 */
export type BackLayerHandler = () => boolean | void | Promise<boolean | void>

export interface BackLayer {
  /** The layer is shown: from now on back closes it. Calling it again does nothing. */
  open(): void
  /** The layer was closed by other means: its history entry is taken out. */
  close(): void
  /** `close()`, and the layer is never opened again. */
  dispose(): void
  /** Whether the layer is open as far as back is concerned. */
  readonly isOpen: boolean
}

export interface UseBackLayerOptions {
  /** Whether back closes the layer at all, e.g. only on a narrow screen. Default true. */
  enabled?: MaybeRefOrGetter<boolean>
}

/** The parts of `window` this module uses; a test passes a stand-in. */
export interface BackLayerWindow {
  readonly history: History
  readonly location: { readonly href: string }
  addEventListener(type: string, listener: (event: Event) => void, options?: boolean | AddEventListenerOptions): void
  removeEventListener(type: string, listener: (event: Event) => void, options?: boolean | EventListenerOptions): void
  setTimeout(handler: () => void, timeout?: number): number
  clearTimeout(id: number | undefined): void
  /** Viewport width; without it the window counts as wide. */
  readonly innerWidth?: number
  /** The Navigation API, where there is one: tells forward entries the model does not know. */
  readonly navigation?: { readonly canGoForward?: boolean }
  readonly navigator?: { readonly userAgent?: string; readonly platform?: string; readonly maxTouchPoints?: number }
  /** The document's scroll, kept through a traversal that clears forward history. */
  readonly scrollX?: number
  readonly scrollY?: number
  scrollTo?(x: number, y: number): void
  requestAnimationFrame?(callback: () => void): number
  cancelAnimationFrame?(id: number): void
}

/** How a controller decides what the screen and the platform are; a test passes its own. */
export interface BackLayerControllerOptions {
  /** Whether forward history is cleared. Default: the viewport is narrower than 768px, where the layers take back. */
  trimForward?: () => boolean
  /**
   * Whether the platform has a forward edge swipe (iOS, iPadOS). Only then is
   * the first entry pushed again on top of itself to clear what is forward of it.
   */
  forwardSwipe?: () => boolean
}

/** The key the marker is kept under in `history.state`. */
export const BACK_LAYER_STATE_KEY = '__tnziBackLayer'

/** Longest a traversal of ours is waited for before the held calls are let through anyway. */
const TRAVERSAL_FALLBACK_MS = 1500

/** Below this width the shell's layers take back (the phone layout), and forward history is cleared. */
const NARROW_BREAKPOINT = 768

/** How long the history must stay still after a back (no `popstate`, no finger down) before forward history is cleared. */
const TRIM_QUIET_MS = 350

/**
 * How long the document's scroll is still held after a traversal that clears
 * forward history has landed: the browser restores the scroll of the entry
 * stepped to a frame or so after the `popstate` (Chromium, measured).
 */
const SCROLL_HOLD_MS = 100

interface Marker {
  readonly sid: string
  readonly eid: number
}

interface LayerRecord {
  readonly onBack: BackLayerHandler
  /** The entry this layer owns, or null. */
  entry: number | null
  /** Back is being handled (waiting for `onBack`): no entry wanted yet. */
  held: boolean
  /** Opened and not yet past the address check: no entry wanted yet. */
  checking: boolean
  /** Came with the address: never takes an entry. */
  addressDriven: boolean
  /** Its entry was lost to a history the model does not know: no entry until reopened. */
  detached: boolean
}

/** One history entry as far as the model knows. `layer` entries belong to layers; the others are the page's. */
interface ModelEntry {
  readonly eid: number
  href: string
  readonly layer: boolean
  /** The entry's `history.state` as last written or seen, marker included. */
  state: unknown
  /** The page of the entry below, pushed again on top of the first entry to clear forward history. */
  readonly duplicate: boolean
}

/** A push to make once a traversal of ours has landed on `base`. */
interface PendingPush {
  readonly base: number
  readonly baseEid: number
  readonly entry: ModelEntry
  /** The scroll restoration mode of the entry pushed again (it is kept per entry). */
  readonly mode: ScrollRestoration | null
}

type Deferred = () => void

interface HistoryMethods {
  pushState: History['pushState']
  replaceState: History['replaceState']
  go: History['go']
  back: History['back']
  forward: History['forward']
}

function defaultTrimForward(win: BackLayerWindow): boolean {
  return typeof win.innerWidth === 'number' && win.innerWidth < NARROW_BREAKPOINT
}

function defaultForwardSwipe(win: BackLayerWindow): boolean {
  const nav = win.navigator
  if (!nav) return false
  if (/iPhone|iPad|iPod/.test(nav.userAgent ?? '')) return true
  // iPadOS reports itself as a Mac; a touch screen tells them apart.
  return nav.platform === 'MacIntel' && (nav.maxTouchPoints ?? 0) > 1
}

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function readMarker(state: unknown): Marker | null {
  if (!isObject(state)) return null
  const marker = state[BACK_LAYER_STATE_KEY]
  if (!isObject(marker)) return null
  return typeof marker.sid === 'string' && typeof marker.eid === 'number' ? { sid: marker.sid, eid: marker.eid } : null
}

/** `state` with the marker set to `marker`, as a new object. A non-object state is wrapped only to carry one. */
function withMarker(state: unknown, marker: Marker): unknown {
  if (!isObject(state)) return state == null ? { [BACK_LAYER_STATE_KEY]: marker } : state
  return { ...state, [BACK_LAYER_STATE_KEY]: marker }
}

/**
 * The back layers of one window. One per window; `createBackLayer` uses the
 * page's. Exported for tests, which drive it with a stand-in window.
 */
export class BackLayerController {
  private readonly win: BackLayerWindow
  private readonly trimForward: () => boolean
  private readonly forwardSwipe: () => boolean
  private readonly sid = Math.random().toString(36).slice(2)
  private layers: LayerRecord[] = []
  /** The history since the module was first used; `index` is the current entry. */
  private model: ModelEntry[] = []
  private index = 0
  private nextEntryId = 0
  private installed = false
  private listening = false
  private original: HistoryMethods | null = null
  /** A traversal of ours is on its way. */
  private traversing = false
  private traversalTimer: number | undefined
  /** The last traversal of ours did not land (nothing that way): do not try it again at once. */
  private stuck = false
  /** Which way the history last moved (-1 back, 1 forward), to step over a stale entry the same way. */
  private direction = -1
  private held: Deferred[] = []
  private settleWaiters: Array<() => void> = []
  private reconcileQueued = false
  /** The address when the user last pressed a key or touched the page. */
  private inputHref: string | null = null
  /** The address when listening started, standing in for `inputHref` before any input. */
  private listenHref = ''
  /** The traversal on its way is one that clears forward history: its `popstate` reaches nobody else. */
  private silentTraversal = false
  /** The push that follows the traversal on its way, clearing forward history. */
  private pendingPush: PendingPush | null = null
  /** Ends the hold on the document's scroll, while one is on. */
  private releaseScroll: (() => void) | null = null
  private trimTimer: number | undefined
  /** Forward history is to be cleared once the history is still and no finger is down. */
  private trimWanted = false
  private pointerDown = false
  /**
   * Forward history was just cleared: until the history moves again, a
   * Navigation API that still reports a forward entry is not believed, so a
   * wrong answer cannot start the clearing over and over.
   */
  private justTrimmed = false

  constructor(win: BackLayerWindow, options: BackLayerControllerOptions = {}) {
    this.win = win
    this.trimForward = options.trimForward ?? (() => defaultTrimForward(win))
    this.forwardSwipe = options.forwardSwipe ?? (() => defaultForwardSwipe(win))
  }

  createLayer(onBack: BackLayerHandler): BackLayer {
    this.listen()
    /* On a narrow screen the model starts now rather than at the first layer,
       so going back between pages finds the entries it needs to clear forward
       history. A wide screen leaves the history alone until a layer opens. */
    if (this.trimForward()) this.install()
    const record: LayerRecord = { onBack, entry: null, held: false, checking: false, addressDriven: false, detached: false }
    let disposed = false
    const isOpen = (): boolean => this.layers.includes(record)
    return {
      open: () => {
        if (disposed || isOpen()) return
        this.openLayer(record)
      },
      close: () => {
        this.closeLayer(record)
      },
      dispose: () => {
        disposed = true
        this.closeLayer(record)
      },
      get isOpen() {
        return isOpen()
      },
    }
  }

  /** Resolves once no traversal of ours is on its way and the held calls have run. */
  whenSettled(): Promise<void> {
    if (!this.traversing) return Promise.resolve()
    return new Promise((resolve) => this.settleWaiters.push(resolve))
  }

  /** For tests: open layers, layer entries in the model, whether a traversal is on its way, forward entries the model knows. */
  get debugState(): { layers: number; entries: number; traversing: boolean; forward: number } {
    const owned = this.model.filter((entry) => this.ownerOf(entry.eid) != null).length
    return { layers: this.layers.length, entries: owned, traversing: this.traversing, forward: this.model.length - 1 - this.index }
  }

  // ── Layers ──────────────────────────────────────────────────────────────

  private openLayer(record: LayerRecord): void {
    this.install()
    record.entry = null
    record.held = false
    record.addressDriven = false
    record.detached = false
    record.checking = true
    this.layers = [...this.layers, record]
    /* Decided a task later, so a router navigation that opened the layer (a
       query-driven dialog) has finished changing the address by then. */
    const inputHref = this.inputHref ?? this.listenHref
    this.win.setTimeout(() => {
      if (!this.layers.includes(record) || !record.checking) return
      record.checking = false
      record.addressDriven = this.win.location.href !== inputHref
      this.scheduleReconcile()
    }, 0)
  }

  /* The layer is gone; its entry, if any, is now stale and is taken out (or
     stepped over later) by `reconcile`. */
  private closeLayer(record: LayerRecord): void {
    if (!this.layers.includes(record)) return
    this.layers = this.layers.filter((layer) => layer !== record)
    if (record.entry != null) {
      record.entry = null
      this.scheduleReconcile()
    }
  }

  private ownerOf(eid: number): LayerRecord | undefined {
    return this.layers.find((layer) => layer.entry === eid)
  }

  /** Back left the entries `left` (top first): the layers owning them are asked to close. */
  private backThrough(left: readonly ModelEntry[]): void {
    for (const entry of left) {
      const record = this.ownerOf(entry.eid)
      if (!record) continue
      record.entry = null
      record.held = true
      let verdict: ReturnType<BackLayerHandler>
      try {
        verdict = record.onBack()
      } catch {
        verdict = false
      }
      Promise.resolve(verdict).then(
        (result) => this.afterBack(record, result !== false),
        () => this.afterBack(record, false),
      )
    }
  }

  /**
   * The layer answered. Refused: it takes an entry again. Accepted: the layer
   * closes through its own state a moment later; if it is still open after a
   * task (its owner kept it), it takes an entry again too.
   */
  private afterBack(record: LayerRecord, accepted: boolean): void {
    const release = (): void => {
      record.held = false
      if (this.layers.includes(record)) this.scheduleReconcile()
    }
    if (accepted) this.win.setTimeout(release, 0)
    else release()
  }

  // ── Entries ─────────────────────────────────────────────────────────────

  private scheduleReconcile(): void {
    if (this.reconcileQueued) return
    this.reconcileQueued = true
    queueMicrotask(() => {
      this.reconcileQueued = false
      this.reconcile()
    })
  }

  /**
   * Standing on a layer entry no open layer owns: step off it (back, or the way
   * the history was going), clearing forward history in the same step where
   * that is done. Otherwise give each open layer that wants one an entry, on
   * top of the current one, and plan to clear forward history.
   */
  private reconcile(): void {
    if (this.traversing || !this.original) return
    const current = this.model[this.index]
    if (current && this.isStale(current) && !this.stuck) {
      this.stepOffStale()
      return
    }
    for (const layer of this.layers) {
      if (layer.entry == null && !layer.held && !layer.checking && !layer.addressDriven && !layer.detached) {
        this.pushEntry(layer)
      }
    }
    if (this.hasForward() && this.trimForward()) this.planTrim()
  }

  private isStale(entry: ModelEntry): boolean {
    return entry.layer && !this.ownerOf(entry.eid)
  }

  private stepOffStale(): void {
    let below = this.index - 1
    while (below >= 0 && this.isStale(this.model[below]!)) below--
    if (below >= 0 && this.canTrimTo(below)) {
      this.trimTo(below)
      return
    }
    if (this.direction > 0) {
      let above = this.index + 1
      while (above < this.model.length && this.isStale(this.model[above]!)) above++
      if (above < this.model.length) this.traverse(1)
      // Nothing valid further forward: going back would only meet the same
      // forward step again. Stay, and keep the entry as the page below it.
      else if (below >= 0) this.adoptStale(below)
      return
    }
    this.traverse(-1)
  }

  /** The current entry, a stale layer entry, becomes a page entry repeating `below`. */
  private adoptStale(below: number): void {
    const original = this.original
    const current = this.model[this.index]
    const source = this.model[below]
    if (!original || !current || !source) return
    const marker: Marker = { sid: this.sid, eid: current.eid }
    const state = withMarker(source.state, marker)
    original.replaceState.call(this.win.history, state, '', source.href)
    this.model = this.model.map((entry, i) =>
      i === this.index ? { eid: current.eid, href: this.win.location.href, layer: false, state, duplicate: true } : entry,
    )
  }

  // ── Forward history ─────────────────────────────────────────────────────

  private hasForward(): boolean {
    if (this.index < this.model.length - 1) return true
    return !this.justTrimmed && this.win.navigation?.canGoForward === true
  }

  /**
   * Whether the model entry `target` can become the current entry with nothing
   * forward of it. Needs an entry of the model before it to push from, or, for
   * the first entry, a platform with a forward swipe (it is pushed again on top
   * of itself) and not already standing on such a repeat's base.
   */
  private canTrimTo(target: number): boolean {
    if (!this.trimForward()) return false
    if (target >= 1) return true
    if (!this.forwardSwipe()) return false
    const next = this.model[1]
    return !(next?.duplicate && this.index === 0 && !this.isStale(this.model[0]!))
  }

  /**
   * Makes the model entry `target` current with nothing forward of it: steps
   * to the entry before it (or, for the first entry, stays on it) and pushes
   * `target` again with its state and address. One traversal at most.
   */
  private trimTo(target: number): void {
    const source = this.model[target]
    if (!source) return
    const state = target === this.index ? this.win.history.state : source.state
    const base = Math.max(target - 1, 0)
    const entry: ModelEntry =
      target === 0
        ? { eid: ++this.nextEntryId, href: source.href, layer: false, state: null, duplicate: true }
        : { eid: source.eid, href: source.href, layer: source.layer, state: null, duplicate: source.duplicate }
    const marked: ModelEntry = { ...entry, state: withMarker(state, { sid: this.sid, eid: entry.eid }) }
    const push: PendingPush = { base, baseEid: this.model[base]!.eid, entry: marked, mode: this.scrollMode() }
    if (base === this.index) {
      this.applyPush(push)
      return
    }
    this.pendingPush = push
    this.silentTraversal = true
    this.holdScroll()
    this.traverse(base - this.index)
  }

  /**
   * Keeps the document's scroll where it is through a traversal that clears
   * forward history: the browser restores the scroll of the entry stepped to
   * (when its restoration mode is `auto`, decided before any script hears of
   * the step), and the page must not move. It is put back in every frame
   * before that frame is painted (the restored scroll is there by the frame's
   * animation callbacks, its `scroll` event only a frame later), and on any
   * `scroll` in between.
   */
  private holdScroll(): void {
    const win = this.win
    if (this.releaseScroll || typeof win.scrollTo !== 'function') return
    const x = win.scrollX ?? 0
    const y = win.scrollY ?? 0
    const restore = (): void => {
      if (win.scrollX !== x || win.scrollY !== y) win.scrollTo?.(x, y)
    }
    let frame: number | undefined
    const everyFrame = (): void => {
      restore()
      frame = win.requestAnimationFrame?.(everyFrame)
    }
    win.addEventListener('scroll', restore, { capture: true, passive: true })
    frame = win.requestAnimationFrame?.(everyFrame)
    this.releaseScroll = () => {
      restore()
      if (frame !== undefined) win.cancelAnimationFrame?.(frame)
      win.removeEventListener('scroll', restore, { capture: true })
      this.releaseScroll = null
    }
  }

  /**
   * The traversal that clears forward history is on `base`: the current entry
   * is pushed again at once, inside the `popstate` of the step, so no other
   * code ever runs while the history stands on `base`. Anything read in that
   * time would be `base`'s: a router that reads `history.state` to replace
   * the current entry before a push (vue-router does, once its guards are
   * done) would write `base`'s address and state over the entry put back. The
   * pushed entry takes `base`'s scroll restoration mode, so it is given the
   * one the entry had.
   */
  private settleTrim(): void {
    const push = this.pendingPush
    this.pendingPush = null
    this.win.clearTimeout(this.traversalTimer)
    this.traversalTimer = undefined
    if (!push || this.index !== push.base || this.model[push.base]?.eid !== push.baseEid) {
      this.releaseScroll?.()
      this.landed()
      return
    }
    this.applyPush(push)
    if (push.mode && this.scrollMode() !== push.mode) this.win.history.scrollRestoration = push.mode
    this.landed()
    this.win.setTimeout(() => this.releaseScroll?.(), SCROLL_HOLD_MS)
  }

  private scrollMode(): ScrollRestoration | null {
    const history = this.win.history
    return 'scrollRestoration' in history ? history.scrollRestoration : null
  }

  private applyPush(push: PendingPush): void {
    const original = this.original
    if (!original) return
    original.pushState.call(this.win.history, push.entry.state, '', push.entry.href)
    this.model = [...this.model.slice(0, push.base + 1), { ...push.entry, href: this.win.location.href }]
    this.index = this.model.length - 1
    this.stuck = false
    this.justTrimmed = true
  }

  /** Clears forward history once the history has been still for a moment and no finger is down. */
  private planTrim(): void {
    this.trimWanted = true
    this.win.clearTimeout(this.trimTimer)
    this.trimTimer = this.win.setTimeout(() => {
      this.trimTimer = undefined
      this.runTrim()
    }, TRIM_QUIET_MS)
  }

  private cancelTrim(): void {
    this.trimWanted = false
    this.win.clearTimeout(this.trimTimer)
    this.trimTimer = undefined
  }

  private runTrim(): void {
    if (!this.trimWanted || this.traversing || this.pointerDown || this.reconcileQueued) return
    if (this.layers.some((layer) => layer.held || layer.checking)) return
    this.trimWanted = false
    const current = this.model[this.index]
    if (!current || this.isStale(current) || !this.hasForward()) return
    if (this.canTrimTo(this.index)) this.trimTo(this.index)
  }

  private stamp(): Marker {
    return { sid: this.sid, eid: ++this.nextEntryId }
  }

  /** Adds an entry to the model at the current position, dropping what was forward of it. */
  private addToModel(entry: ModelEntry): void {
    this.model = [...this.model.slice(0, this.index + 1), entry]
    this.index = this.model.length - 1
  }

  private pushEntry(layer: LayerRecord): void {
    const original = this.original
    if (!original) return
    const marker = this.stamp()
    const href = this.win.location.href
    const state = withMarker(this.win.history.state, marker)
    original.pushState.call(this.win.history, state, '', href)
    this.addToModel({ eid: marker.eid, href, layer: true, state, duplicate: false })
    layer.entry = marker.eid
    this.stuck = false
  }

  private traverse(delta: number): void {
    const original = this.original
    if (!original || delta === 0) return
    this.traversing = true
    this.win.clearTimeout(this.traversalTimer)
    this.traversalTimer = this.win.setTimeout(() => {
      // Nothing came back: the traversal had nowhere to go.
      this.stuck = true
      this.pendingPush = null
      this.landed()
    }, TRAVERSAL_FALLBACK_MS)
    original.go.call(this.win.history, delta)
  }

  /** A traversal of ours has landed: the held calls run, then the entries are reconciled again. */
  private landed(): void {
    if (!this.traversing) return
    this.traversing = false
    this.silentTraversal = false
    this.pendingPush = null
    this.win.clearTimeout(this.traversalTimer)
    this.traversalTimer = undefined
    /* The held calls run after the push that clears forward history (in
       `settleTrim`): they were made on top of the entry it puts back. */
    const held = this.held
    this.held = []
    for (const call of held) call()
    if (!this.traversing) {
      const waiters = this.settleWaiters
      this.settleWaiters = []
      for (const resolve of waiters) resolve()
    }
    this.scheduleReconcile()
  }

  // ── Window ──────────────────────────────────────────────────────────────

  /**
   * Starts listening: the tap or key press before a layer opens (the address
   * then tells a layer that came with the address), and `popstate`. The
   * `popstate` listener has to be added before the router's: listeners at the
   * window run in the order they were added (capture or not, measured in
   * Chromium), and only one that runs first can keep a back between two
   * entries of the same address from reaching the router.
   */
  listen(): void {
    if (this.listening) return
    this.listening = true
    this.listenHref = this.win.location.href
    const record = (): void => {
      this.inputHref = this.win.location.href
    }
    const press = (): void => {
      record()
      this.pointerDown = true
    }
    /* A finger lifted (or taken over by the browser for a gesture): a clearing
       of forward history put off for it waits for a still moment again. */
    const release = (): void => {
      this.pointerDown = false
      if (this.trimWanted) this.planTrim()
    }
    this.win.addEventListener('pointerdown', press, { capture: true, passive: true })
    this.win.addEventListener('pointerup', release, { capture: true, passive: true })
    this.win.addEventListener('pointercancel', release, { capture: true, passive: true })
    this.win.addEventListener('keydown', record, { capture: true })
    openPopStateGate(this.win).setHandler((event) => {
      if (this.installed) this.onPopState(event)
    })
  }

  private install(): void {
    if (this.installed) return
    this.installed = true
    const history = this.win.history
    const original: HistoryMethods = {
      pushState: history.pushState,
      replaceState: history.replaceState,
      go: history.go,
      back: history.back,
      forward: history.forward,
    }
    this.original = original
    this.resetModel()

    /* Own properties on the history object shadow the prototype's methods, so
       a router that calls `history.pushState(...)` comes through here. */
    Object.assign(history, {
      pushState: (state: unknown, unused: string, url?: string | URL | null) => {
        if (this.traversing) {
          this.held.push(() => history.pushState(state, unused, url))
          return
        }
        const marker = this.stamp()
        const marked = withMarker(state, marker)
        original.pushState.call(history, marked, unused, url)
        this.addToModel({ eid: marker.eid, href: this.win.location.href, layer: false, state: marked, duplicate: false })
        this.stuck = false
      },
      replaceState: (state: unknown, unused: string, url?: string | URL | null) => {
        if (this.traversing) {
          this.held.push(() => history.replaceState(state, unused, url))
          return
        }
        /* The caller cannot know the marker: the entry keeps its own. */
        const current = this.model[this.index]
        const next = current ? withMarker(state, { sid: this.sid, eid: current.eid }) : state
        original.replaceState.call(history, next, unused, url)
        if (current) {
          current.href = this.win.location.href
          current.state = next
        }
      },
      go: (delta?: number) => {
        if (this.traversing) {
          this.held.push(() => history.go(delta))
          return
        }
        original.go.call(history, delta)
      },
      back: () => {
        if (this.traversing) {
          this.held.push(() => history.back())
          return
        }
        original.back.call(history)
      },
      forward: () => {
        if (this.traversing) {
          this.held.push(() => history.forward())
          return
        }
        original.forward.call(history)
      },
    })
  }

  /* Starts the model at the current entry, stamped so a later pop back to it
     is recognised. */
  private resetModel(): void {
    const original = this.original
    if (!original) return
    const marker = this.stamp()
    const href = this.win.location.href
    const state = withMarker(this.win.history.state, marker)
    original.replaceState.call(this.win.history, state, '', href)
    this.model = [{ eid: marker.eid, href, layer: false, state, duplicate: false }]
    this.index = 0
  }

  private onPopState(event: PopStateEvent): void {
    this.cancelTrim()
    this.justTrimmed = false
    /* A traversal that clears forward history comes back to where it started
       at once: nobody else is told of it. */
    if (this.traversing && this.silentTraversal) event.stopImmediatePropagation()
    const marker = readMarker(event.state)
    const to = marker && marker.sid === this.sid ? this.model.findIndex((entry) => entry.eid === marker.eid) : -1
    const from = this.index
    const fromEntry = this.model[from]

    if (to < 0) {
      // An entry from before the module knew the history (or an earlier page
      // load): start over from here. Open layers' entries are out of reach.
      for (const layer of this.layers) {
        if (layer.entry != null) {
          layer.entry = null
          layer.detached = true
        }
      }
      this.resetModel()
      if (this.traversing) this.landed()
      return
    }

    this.index = to
    this.model[to]!.state = event.state
    if (to !== from) this.direction = to < from ? -1 : 1
    if (fromEntry && fromEntry.href === this.model[to]!.href) event.stopImmediatePropagation()

    if (this.traversing) {
      if (this.pendingPush) this.settleTrim()
      else this.landed()
      return
    }
    if (to < from) this.backThrough(this.model.slice(to + 1, from + 1).reverse())
    this.stuck = false
    this.scheduleReconcile()
  }
}

let pageController: BackLayerController | null = null

function controllerForPage(): BackLayerController | null {
  if (typeof window === 'undefined') return null
  pageController ??= new BackLayerController(window as unknown as BackLayerWindow)
  return pageController
}

const INERT_LAYER: BackLayer = {
  open() {},
  close() {},
  dispose() {},
  get isOpen() {
    return false
  },
}

/**
 * Starts the page's back layers listening. Call it before the router is
 * created (`defineChatApp` does): the router must not hear a back that only
 * closes a layer, and only a listener added before the router's can stop it.
 * Layers work without it, but then such a back also reaches the router, which
 * runs its guards for the same address and, if a navigation is held at that
 * moment, can be sent back to the previous address. Calling it again does nothing.
 */
export function installBackLayers(): void {
  controllerForPage()?.listen()
}

/** A layer back closes, for code outside a component. Nothing happens without `window`. */
export function createBackLayer(onBack: BackLayerHandler): BackLayer {
  return controllerForPage()?.createLayer(onBack) ?? INERT_LAYER
}

/**
 * Back closes this layer while `open` (and `enabled`) is true. Disposed with
 * the calling component or effect scope.
 */
export function useBackLayer(
  open: MaybeRefOrGetter<boolean>,
  onBack: BackLayerHandler,
  options: UseBackLayerOptions = {},
): BackLayer {
  const layer = createBackLayer(onBack)
  watch(
    () => toValue(open) && (options.enabled === undefined || toValue(options.enabled)),
    (active) => (active ? layer.open() : layer.close()),
    { immediate: true },
  )
  if (getCurrentScope()) onScopeDispose(() => layer.dispose())
  return layer
}

/**
 * Resolves once a history step taken to close a layer has landed. Calls to
 * `history` made before that are held and replayed anyway; await this before
 * reading `history.state` or `location` right after closing a layer.
 */
export function whenBackLayersSettled(): Promise<void> {
  return controllerForPage()?.whenSettled() ?? Promise.resolve()
}
