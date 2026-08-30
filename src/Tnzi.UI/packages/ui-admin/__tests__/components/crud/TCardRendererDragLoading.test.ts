import { describe, it, expect, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { ref, computed } from 'vue'

/**
 * The drag library never finishes loading here. That is the point: it stands in
 * for a slow connection (and for any test that mounts a draggable page without
 * awaiting the dynamic import). The cards must be on screen the whole time -
 * a list that blinks out while a chunk downloads is worse than one that cannot
 * be dragged for another 200ms.
 */
vi.mock('vue-draggable-plus', () => new Promise(() => {}))

import TCardRenderer from '../../../src/components/crud/renderers/TCardRenderer.vue'

type Row = { id: number; name: string }

function makeState(items: Row[]) {
  const selected = ref<Set<number>>(new Set())
  return {
    items: ref(items),
    total: ref(items.length),
    loading: ref(false),
    query: ref({ pageIndex: 1, pageSize: 20 }),
    rowKey: (r: Row) => r.id,
    batchActions: {
      selected,
      selectedIds: computed(() => [...selected.value]),
      isSelected: (id: number) => selected.value.has(id),
      toggle: vi.fn(),
    },
  }
}

describe('TCardRenderer while the drag library is still loading', () => {
  it('keeps rendering the cards', async () => {
    const wrapper = mount(TCardRenderer, {
      props: {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        state: makeState([{ id: 1, name: 'A' }, { id: 2, name: 'B' }]) as any,
        draggable: true,
      },
      slots: { card: '<div class="card-item">{{ params.item.name }}</div>' },
    })
    await flushPromises()

    expect(wrapper.findAll('.card-item')).toHaveLength(2)
    expect(wrapper.text()).toContain('A')
    expect(wrapper.text()).toContain('B')
    // Still the grid, still marked draggable - only the gesture is not wired yet.
    expect(wrapper.find('.t-card-renderer__grid--draggable').exists()).toBe(true)
  })

  it('does not leak Sortable options onto the DOM as attributes', async () => {
    const wrapper = mount(TCardRenderer, {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      props: { state: makeState([{ id: 1, name: 'A' }]) as any, draggable: true },
      slots: { card: '<div class="card-item" />' },
    })
    await flushPromises()

    const html = wrapper.html()
    expect(html).not.toContain('delayontouchonly')
    expect(html).not.toContain('preventonfilter')
  })
})
