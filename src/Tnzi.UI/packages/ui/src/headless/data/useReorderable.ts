/**
 * Drag-and-keyboard reordering of a list, as one implementation.
 *
 * ## Why this is shared rather than written per component
 *
 * Three components in this monorepo wrap `vue-draggable-plus`, and before this
 * module existed each had hand-copied the scaffolding from the previous one -
 * the lazy import, the local copy of the list, the mid-gesture guard, the
 * Sortable option bag, the optimistic apply with a `revert`. Copies drift, and
 * these already had: all three disagreed on what happens when the optional peer
 * fails to load, and only one of them could be driven from a keyboard.
 *
 * So the contract lives here and the components supply only what is genuinely
 * theirs: which elements start a gesture, what a move is reported as, and where
 * the order is persisted.
 *
 * ## The three pieces
 *
 * - `useDraggableComponent` - the optional peer, loaded on demand, with a plain
 *   container standing in until (and if) it arrives.
 * - `useReorderable` - the state machine: local copy, drag lifecycle, keyboard
 *   moves, optimistic apply, `revert`.
 * - `reorderKeyTarget` - the keyboard mapping on its own, for a component that
 *   wants the key handling without the drag half.
 *
 * @packageDocumentation
 */
import {
  computed,
  defineComponent,
  h,
  ref,
  shallowRef,
  watch,
  type Component,
  type ComputedRef,
  type Ref,
  type ShallowRef,
} from 'vue'

/**
 * Renders the container and its children, and nothing else.
 *
 * This is what a reorderable surface looks like before `vue-draggable-plus`
 * arrives, and what it keeps looking like if the module never does.
 * `inheritAttrs: false` is for the Sortable options (they are not DOM
 * attributes); `class` and `style` are passed through by hand because the
 * container's layout rides on them.
 */
export const DraggableFallback = defineComponent({
  name: 'TDraggableFallback',
  inheritAttrs: false,
  setup: (_props, { slots, attrs }) => () =>
    h('div', { class: attrs.class, style: attrs.style }, slots.default?.()),
})

/**
 * The resolved `VueDraggable`, or `null` while it is in flight or unavailable.
 * Module-level so several reorderable surfaces on one page share one fetch.
 */
const draggableComponent: ShallowRef<Component | null> = shallowRef(null)
let draggableRequest: Promise<unknown> | null = null

function loadDraggable(): void {
  if (draggableComponent.value || draggableRequest) return
  draggableRequest = import('vue-draggable-plus')
    .then((m) => {
      draggableComponent.value = m.VueDraggable as unknown as Component
    })
    .catch(() => {
      // `vue-draggable-plus` is declared an OPTIONAL peer, so being without it
      // is a legitimate installation, not a bug to report. The surface keeps
      // the plain container: pointer dragging is gone, everything else - the
      // items themselves, and keyboard reordering, which never needed the
      // module - carries on.
    })
}

/**
 * The component to mount as the drag container, loading the optional peer the
 * first time `enabled` is true.
 *
 * ★ Deliberately NOT `defineAsyncComponent`, which is the obvious way to write
 * this and is wrong here. Its `errorComponent` is mounted as
 * `createVNode(errorComponent, { error })` - **with no children** - so a failed
 * load does not degrade the container, it empties it. And since this container
 * holds every item in the list, the symptom of a missing optional peer is not
 * "dragging does not work", it is "the list is blank". (`loadingComponent` does
 * get the children, via `createInnerComp`, which is what makes the trap so easy
 * to miss: it looks correct for the whole time the module is merely slow.)
 * Resolving the module by hand keeps the children in our own template in every
 * state.
 */
export function useDraggableComponent(enabled: () => boolean): ComputedRef<Component> {
  watch(enabled, (on) => on && loadDraggable(), { immediate: true })
  return computed<Component>(() => draggableComponent.value ?? DraggableFallback)
}

/** One completed move, however it was made. */
export interface ReorderMove<T> {
  /** Every item in its new order - this is what you persist. */
  items: T[]
  /** Index the item came from. */
  from: number
  /** Index it landed at. */
  to: number
  /** The item that moved. */
  moved: T
  /**
   * Put the list back the way it was before this move. Call it when the persist
   * call fails: the move is applied optimistically, so the item is already in
   * its new place, and without this the screen keeps showing an order the
   * server rejected while nothing tells the operator their change was lost.
   */
  revert: () => void
}

export interface UseReorderableOptions<T> {
  /** The list to render and reorder. Read on every change. */
  source: () => readonly T[]
  /** Whether reordering is on. Everything below is inert while it is false. */
  enabled: () => boolean
  /** Called once per completed move, by drag or by keyboard. */
  onCommit: (move: ReorderMove<T>) => void
  /**
   * Land an order somewhere beyond this composable's local copy - a writable
   * store projection, say. Called with the new order on a move and with the
   * previous one on `revert`, so a caller does not re-implement roll-back.
   */
  apply?: (next: T[]) => void
  /** Class for the drag container, when it is not stated on the element. */
  containerClass?: () => string | undefined
  /**
   * Selector of elements that must NOT start a gesture (Sortable `filter`).
   *
   * ★ Reach for `handle` instead whenever an item's interactive content can
   * FILL it. `filter` matches the press target and its ancestors, so an anchor
   * stretched over a whole tile silently makes that tile undraggable - and the
   * items that behave this way (a file card that is one big link) are exactly
   * the ones a filter list looks like it covers.
   */
  filter?: () => string | undefined
  /** Selector of the only element that starts a gesture (Sortable `handle`). */
  handle?: () => string | undefined
  /** Class Sortable puts on the placeholder left at the drop target. */
  ghostClass: string
  /** Class Sortable puts on the item being dragged. */
  chosenClass: string
}

export interface UseReorderableReturn<T> {
  /** Mount this as the drag container - see `useDraggableComponent`. */
  dragContainer: ComputedRef<Component>
  /** The working copy, live while reordering is on. */
  localItems: Ref<T[]>
  /** What the component should render: the working copy, or the source as-is. */
  renderItems: ComputedRef<T[]>
  /** Bind to the drag container. Empty while reordering is off. */
  dragBindings: ComputedRef<Record<string, unknown>>
  /** True between `onStart` and `onEnd` of a pointer gesture. */
  dragging: Ref<boolean>
  /**
   * Handle a keydown on item `index`. Returns the index the item moved to, or
   * `null` when the press was not a move - so the caller can restore focus.
   */
  moveByKeyboard: (event: KeyboardEvent, index: number) => number | null
}

/**
 * Resolve a keypress to the index an item should move to.
 *
 * Alt + arrows, Alt + Home / End. Returns `null` when the press is not a
 * reorder gesture at all; `-1` or `> last` when it is one that runs off the end
 * (which the caller still swallows - see below).
 *
 * ★ Alt is not decoration. These lists are grids, where the bare arrow keys
 * already mean "look at the next item"; binding those to a move makes people
 * rearrange content when they only meant to browse it.
 */
export function reorderKeyTarget(event: KeyboardEvent, index: number, last: number): number | null {
  if (!event.altKey || event.ctrlKey || event.metaKey) return null
  switch (event.key) {
    case 'ArrowLeft':
    case 'ArrowUp':
      return index - 1
    case 'ArrowRight':
    case 'ArrowDown':
      return index + 1
    case 'Home':
      return 0
    case 'End':
      return last
    default:
      return null
  }
}

export function useReorderable<T>(options: UseReorderableOptions<T>): UseReorderableReturn<T> {
  /**
   * VueDraggable reorders its bound array in place, and the source is usually a
   * prop or a computed. So a working copy is rendered while reordering is on,
   * and that copy is what gets handed back. With reordering off the source is
   * rendered directly and nothing about the host component changes.
   */
  const localItems = ref<T[]>([]) as Ref<T[]>
  const dragging = ref(false)
  /** Order captured at gesture start, so a failed save can put the list back. */
  let beforeDrag: T[] = []

  watch(
    () => options.source(),
    (next) => {
      // Reassigning mid-gesture severs VueDraggable's pointer tracking, which
      // reads as the item being dropped somewhere the operator never let go.
      if (dragging.value) return
      localItems.value = [...next]
    },
    { immediate: true },
  )

  const renderItems = computed<T[]>(() =>
    options.enabled() ? localItems.value : ([...options.source()] as T[]),
  )

  function commit(next: T[], from: number, to: number, snapshot: T[]): void {
    const moved = next[to]
    if (moved === undefined) return

    localItems.value = next
    options.apply?.(next)

    options.onCommit({
      items: next,
      from,
      to,
      moved,
      revert: () => {
        const restored = [...snapshot]
        localItems.value = restored
        options.apply?.(restored)
      },
    })
  }

  function onDragStart(): void {
    dragging.value = true
    beforeDrag = [...localItems.value]
  }

  function onDragEnd(evt: { oldIndex?: number | null; newIndex?: number | null }): void {
    dragging.value = false

    const from = evt?.oldIndex ?? -1
    const to = evt?.newIndex ?? -1
    if (from < 0 || to < 0 || from === to) return

    // By now VueDraggable has already written the new order back through
    // `onUpdate:modelValue` - its internal `onUpdate` runs before Sortable's
    // `onEnd` - so the working copy is the order the operator dropped.
    commit([...localItems.value], from, to, beforeDrag)
  }

  const dragBindings = computed<Record<string, unknown>>(() => {
    if (!options.enabled()) return {}

    const bindings: Record<string, unknown> = {
      modelValue: localItems.value,
      'onUpdate:modelValue': (next: T[]) => {
        localItems.value = next
      },
      animation: 180,
      // Touch only: hold briefly before a press becomes a drag, or a finger
      // swiping to scroll the page picks an item up instead. The mouse keeps
      // zero delay (`delayOnTouchOnly`), so pointer dragging stays immediate.
      delay: 180,
      delayOnTouchOnly: true,
      touchStartThreshold: 5,
      // Without this a press that lands on a filtered control is swallowed as
      // the start of a gesture and the control never fires.
      preventOnFilter: false,
      ghostClass: options.ghostClass,
      chosenClass: options.chosenClass,
      onStart: onDragStart,
      onEnd: onDragEnd,
    }

    const containerClass = options.containerClass?.()
    if (containerClass) bindings.class = containerClass
    const filter = options.filter?.()
    if (filter) bindings.filter = filter
    const handle = options.handle?.()
    if (handle) bindings.handle = handle

    return bindings
  })

  function moveByKeyboard(event: KeyboardEvent, index: number): number | null {
    if (!options.enabled()) return null

    const last = localItems.value.length - 1
    const to = reorderKeyTarget(event, index, last)
    if (to === null) return null

    // ★ BEFORE the bounds check, deliberately. Alt + Left / Right is Back /
    // Forward in the browser: preventing it only on a successful move means
    // Alt + Left on the first item navigates away from the page.
    event.preventDefault()
    if (to < 0 || to > last || to === index) return null

    const snapshot = [...localItems.value]
    const next = [...snapshot]
    const [moved] = next.splice(index, 1)
    if (moved === undefined) return null
    next.splice(to, 0, moved)

    commit(next, index, to, snapshot)
    return to
  }

  const dragContainer = useDraggableComponent(() => options.enabled())

  return { dragContainer, localItems, renderItems, dragBindings, dragging, moveByKeyboard }
}
