import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { ref, computed, defineComponent, h } from 'vue'

/**
 * Handlers the stubbed VueDraggable captured on its last render, so a test can
 * drive a gesture without a real pointer.
 */
const drag: {
  onStart?: () => void
  onEnd?: (evt: { oldIndex?: number | null; newIndex?: number | null }) => void
  setModel?: (next: unknown[]) => void
  attrs?: Record<string, unknown>
} = {}

vi.mock('vue-draggable-plus', () => ({
  VueDraggable: defineComponent({
    name: 'VueDraggableStub',
    props: { modelValue: { type: Array, default: () => [] } },
    setup(_props, { attrs, slots }) {
      return () => {
        drag.attrs = attrs as Record<string, unknown>
        drag.onStart = attrs.onStart as typeof drag.onStart
        drag.onEnd = attrs.onEnd as typeof drag.onEnd
        drag.setModel = attrs['onUpdate:modelValue'] as typeof drag.setModel
        return h('div', { class: 'vd-stub' }, slots.default?.())
      }
    },
  }),
}))

import TCardRenderer from '../../../src/components/crud/renderers/TCardRenderer.vue'

type Row = { id: number; name: string }

const ROWS: Row[] = [
  { id: 1, name: 'A' },
  { id: 2, name: 'B' },
  { id: 3, name: 'C' },
]

function makeState(items: Row[] = [...ROWS], sortField?: string) {
  const selected = ref<Set<number>>(new Set())
  return {
    items: ref(items),
    total: ref(items.length),
    loading: ref(false),
    query: ref({ pageIndex: 1, pageSize: 20, sortField, sortOrder: sortField ? 'asc' : null }),
    rowKey: (r: Row) => r.id,
    batchActions: {
      selected,
      selectedIds: computed(() => [...selected.value]),
      isSelected: (id: number) => selected.value.has(id),
      toggle: vi.fn(),
    },
  }
}

async function mountDraggable(props: Record<string, unknown> = {}, state = makeState()) {
  const wrapper = mount(TCardRenderer, {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    props: { state: state as any, draggable: true, ...props },
    slots: { card: '<div class="card-item">{{ params.item.name }}</div>' },
  })
  await flushPromises()
  return { wrapper, state }
}

/** Names in the order the grid currently renders them. */
function rendered(wrapper: { findAll: (s: string) => { text: () => string }[] }): string[] {
  return wrapper.findAll('.card-item').map((n) => n.text())
}

describe('TCardRenderer drag-to-reorder', () => {
  beforeEach(() => {
    drag.onStart = undefined
    drag.onEnd = undefined
    drag.setModel = undefined
    drag.attrs = undefined
  })

  it('is off by default - a plain grid, no draggable affordance', async () => {
    const wrapper = mount(TCardRenderer, {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      props: { state: makeState() as any },
      slots: { card: '<div class="card-item" />' },
    })
    await flushPromises()

    expect(wrapper.find('.vd-stub').exists()).toBe(false)
    expect(wrapper.find('.t-card-renderer__grid--draggable').exists()).toBe(false)
    expect(wrapper.find('.t-card-renderer__cell--draggable').exists()).toBe(false)
  })

  it('marks every cell draggable when turned on (the press-state cursor rides on this class)', async () => {
    const { wrapper } = await mountDraggable()

    expect(wrapper.find('.vd-stub').exists()).toBe(true)
    expect(wrapper.find('.t-card-renderer__grid--draggable').exists()).toBe(true)
    expect(wrapper.findAll('.t-card-renderer__cell--draggable')).toHaveLength(3)
  })

  /**
   * A hand-made order means nothing while the server is ordering by a column:
   * the next refresh re-sorts it away. Dragging has to be off, not merely
   * ineffective.
   */
  it('stays off while a column sort is active', async () => {
    const { wrapper } = await mountDraggable({}, makeState([...ROWS], 'creationTime'))

    expect(wrapper.find('.vd-stub').exists()).toBe(false)
    expect(wrapper.find('.t-card-renderer__cell--draggable').exists()).toBe(false)
  })

  it('dragWhileSorted lets a page opt back in', async () => {
    const { wrapper } = await mountDraggable({ dragWhileSorted: true }, makeState([...ROWS], 'creationTime'))

    expect(wrapper.find('.vd-stub').exists()).toBe(true)
  })

  it('excludes interactive elements from starting a gesture', async () => {
    await mountDraggable({ dragFilter: '.my-menu' })

    const filter = String(drag.attrs?.filter ?? '')
    expect(filter).toContain('button')
    expect(filter).toContain('a,')
    expect(filter).toContain('.my-menu')
    // A press landing on a filtered control must still reach that control.
    expect(drag.attrs?.preventOnFilter).toBe(false)
  })

  /**
   * On a phone the card grid is a single column - the same place people swipe
   * to scroll. Without a touch-only hold, that swipe picks a card up instead.
   */
  it('requires a brief hold on touch but stays immediate for the mouse', async () => {
    await mountDraggable()

    expect(drag.attrs?.delay).toBeGreaterThan(0)
    expect(drag.attrs?.delayOnTouchOnly).toBe(true)
  })

  it('marks dragDisabled rows so they are excluded from the gesture', async () => {
    const { wrapper } = await mountDraggable({ dragDisabled: (r: Row) => r.id === 2 })

    expect(wrapper.findAll('.t-card-renderer__cell--draggable')).toHaveLength(2)
    expect(wrapper.findAll('.t-card-renderer__cell--no-drag')).toHaveLength(1)
  })

  it('emits the new order and applies it optimistically', async () => {
    const { wrapper, state } = await mountDraggable()

    drag.onStart?.()
    // What SortableJS does: C dropped at the front.
    drag.setModel?.([ROWS[2], ROWS[0], ROWS[1]])
    await flushPromises()
    drag.onEnd?.({ oldIndex: 2, newIndex: 0 })
    await flushPromises()

    const events = wrapper.emitted('reorder')
    expect(events).toHaveLength(1)
    const payload = events![0][0] as {
      orderedIds: number[]
      items: Row[]
      from: number
      to: number
      moved: Row
      revert: () => void
    }
    expect(payload.orderedIds).toEqual([3, 1, 2])
    expect(payload.from).toBe(2)
    expect(payload.to).toBe(0)
    expect(payload.moved).toEqual(ROWS[2])
    // Optimistic: the list already shows the dropped order.
    expect(state.items.value.map((r) => r.id)).toEqual([3, 1, 2])
    expect(rendered(wrapper)).toEqual(['C', 'A', 'B'])
  })

  /**
   * A rejected save must put the list back. Without this the screen keeps
   * showing an order the server refused, and nothing tells the operator.
   */
  it('revert() restores the order the drag started from', async () => {
    const { wrapper, state } = await mountDraggable()

    drag.onStart?.()
    drag.setModel?.([ROWS[2], ROWS[0], ROWS[1]])
    await flushPromises()
    drag.onEnd?.({ oldIndex: 2, newIndex: 0 })
    await flushPromises()

    const payload = wrapper.emitted('reorder')![0][0] as { revert: () => void }
    payload.revert()
    await flushPromises()

    expect(state.items.value.map((r) => r.id)).toEqual([1, 2, 3])
    expect(rendered(wrapper)).toEqual(['A', 'B', 'C'])
  })

  it('does not emit when the card is dropped where it started', async () => {
    const { wrapper } = await mountDraggable()

    drag.onStart?.()
    drag.onEnd?.({ oldIndex: 1, newIndex: 1 })
    await flushPromises()

    expect(wrapper.emitted('reorder')).toBeUndefined()
  })

  /**
   * An upstream refresh landing mid-gesture would reassign the bound array and
   * sever the pointer tracking, which reads as the card being dropped somewhere
   * the operator never let go of.
   */
  it('ignores upstream item changes while a gesture is in flight', async () => {
    const { wrapper, state } = await mountDraggable()

    drag.onStart?.()
    state.items.value = [{ id: 9, name: 'Z' }]
    await flushPromises()

    expect(rendered(wrapper)).toEqual(['A', 'B', 'C'])

    drag.onEnd?.({ oldIndex: 0, newIndex: 0 })
    state.items.value = [{ id: 9, name: 'Z' }]
    await flushPromises()
    expect(rendered(wrapper)).toEqual(['Z'])
  })
})
