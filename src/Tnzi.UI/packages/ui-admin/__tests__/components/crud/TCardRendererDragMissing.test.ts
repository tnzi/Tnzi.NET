import { describe, it, expect, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { ref, computed } from 'vue'

/**
 * The drag library is not installed at all.
 *
 * `vue-draggable-plus` is declared `optional: true` in this package's
 * `peerDependenciesMeta`, so a consumer who turns `draggable` on without it is
 * doing nothing wrong. The sibling file `TCardRendererDragLoading.test.ts`
 * covers the module being slow; this one covers it never arriving.
 *
 * ★ The failure this guards against is not "dragging stops working" - it is the
 * grid going blank. Because the draggable IS the container of every card, a
 * container that renders nothing takes the whole list with it. That is what
 * `defineAsyncComponent` does on a failed load unless an `errorComponent` is
 * given, and it is ALSO what it does WITH one: Vue mounts an error component as
 * `createVNode(errorComponent, { error })`, with no children. Only
 * `loadingComponent` inherits them (`createInnerComp`) - which is exactly why
 * the loading test above could pass while this case was broken.
 */
vi.mock('vue-draggable-plus', () => {
  throw new Error("Cannot find module 'vue-draggable-plus'")
})

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

function mountGrid() {
  return mount(TCardRenderer, {
    props: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      state: makeState([{ id: 1, name: 'A' }, { id: 2, name: 'B' }]) as any,
      draggable: true,
    },
    slots: { card: '<div class="card-item">{{ params.item.name }}</div>' },
  })
}

describe('TCardRenderer when the drag library is not installed', () => {
  it('★ still renders every card', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    vi.spyOn(console, 'error').mockImplementation(() => {})

    const wrapper = mountGrid()
    await flushPromises()

    expect(wrapper.findAll('.card-item')).toHaveLength(2)
    expect(wrapper.text()).toContain('A')
    expect(wrapper.text()).toContain('B')
  })

  it('keeps the grid layout, so the cards do not collapse into a column', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    vi.spyOn(console, 'error').mockImplementation(() => {})

    const wrapper = mountGrid()
    await flushPromises()

    // The stand-in passes `class` and `style` through by hand precisely for
    // this - the grid template lives on them.
    const grid = wrapper.find('.t-card-renderer__grid')
    expect(grid.exists()).toBe(true)
    expect(grid.attributes('style') ?? '').toContain('grid-template-columns')
  })
})
