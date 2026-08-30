import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { ref, computed, nextTick } from 'vue'
import TCardRenderer from '../../../src/components/crud/renderers/TCardRenderer.vue'

function makeState(items: { id: number; name: string }[] = [{ id: 1, name: 'A' }, { id: 2, name: 'B' }], loading = false) {
  const selected = ref<Set<number>>(new Set())
  return {
    items: ref(items),
    total: ref(items.length),
    loading: ref(loading),
    rowKey: (r: { id: number }) => r.id,
    batchActions: {
      selected,
      selectedIds: computed(() => [...selected.value]),
      isSelected: (id: number) => selected.value.has(id),
      toggle: vi.fn(),
    },
  }
}

describe('TCardRenderer', () => {
  it('renders one #card slot per item', () => {
    const wrapper = mount(TCardRenderer, {
      props: { state: makeState() as any },
      slots: { card: '<div class="card-item">{{ params.item.name }}</div>' },
    })
    expect(wrapper.findAll('.card-item')).toHaveLength(2)
    // slot scope actually receives the item (not just empty wrappers)
    expect(wrapper.text()).toContain('A')
    expect(wrapper.text()).toContain('B')
  })

  it('shows the empty state when not loading and no items', () => {
    const wrapper = mount(TCardRenderer, {
      props: { state: makeState([], false) as any, translate: (k: string) => k },
      slots: { card: '<div class="card-item" />' },
    })
    expect(wrapper.find('.card-item').exists()).toBe(false)
    expect(wrapper.find('.t-card-renderer__empty').exists()).toBe(true)
  })

  it('sets an explicit grid-template-columns inline style (not a CSS var)', () => {
    const wrapper = mount(TCardRenderer, {
      props: { state: makeState() as any, cols: 3 },
      slots: { card: '<div class="card-item" />' },
    })
    const grid = wrapper.find('.t-card-renderer__grid')
    expect(grid.attributes('style')).toContain('repeat(')
  })

  it('toggles selection when showSelection and a card is selected', async () => {
    const state = makeState()
    const wrapper = mount(TCardRenderer, {
      props: { state: state as any, showSelection: true },
      slots: { card: '<div class="card-item" />' },
    })
    await wrapper.find('.t-card-renderer__select').trigger('click')
    expect(state.batchActions.toggle).toHaveBeenCalledWith(1)
  })

  it('reflects pre-selected state in aria-pressed and the --selected class', () => {
    const state = makeState()
    state.batchActions.selected.value = new Set([1])
    const wrapper = mount(TCardRenderer, {
      props: { state: state as any, showSelection: true },
      slots: { card: '<div class="card-item" />' },
    })
    const cells = wrapper.findAll('.t-card-renderer__cell')
    expect(cells[0].classes()).toContain('t-card-renderer__cell--selected')
    expect(wrapper.findAll('.t-card-renderer__select')[0].attributes('aria-pressed')).toBe('true')
  })

  describe('empty-state create CTA', () => {
    /** First-load empty creatable state: no items, no keyword/filters. */
    function makeCreatableEmptyState(query: Record<string, unknown> = {}) {
      const state = makeState([], false) as any
      state.canCreate = true
      state.openCreate = vi.fn()
      state.query = ref({
        pageIndex: 1, pageSize: 20, searchText: '', sortField: undefined, sortOrder: null, filters: {},
        ...query,
      })
      return state
    }

    it('shows the Create CTA on a first-load empty creatable list and opens create on click', async () => {
      const state = makeCreatableEmptyState()
      const wrapper = mount(TCardRenderer, {
        props: { state, translate: (k: string) => k },
        slots: { card: '<div class="card-item" />' },
      })
      const cta = wrapper.find('.t-crud-empty-cta')
      expect(cta.exists()).toBe(true)
      expect(cta.text()).toBe('admin.crud.create')
      await cta.trigger('click')
      expect(state.openCreate).toHaveBeenCalledTimes(1)
    })

    it('hides the CTA when a search keyword is active (search miss, not first-load empty)', () => {
      const state = makeCreatableEmptyState({ searchText: 'nothing matches' })
      const wrapper = mount(TCardRenderer, {
        props: { state },
        slots: { card: '<div class="card-item" />' },
      })
      expect(wrapper.find('.t-crud-empty-cta').exists()).toBe(false)
      expect(wrapper.find('.t-card-renderer__empty').exists()).toBe(true)
    })

    it('hides the CTA when an advanced filter is active', () => {
      const state = makeCreatableEmptyState({ filters: { status: 'Failed' } })
      const wrapper = mount(TCardRenderer, {
        props: { state },
        slots: { card: '<div class="card-item" />' },
      })
      expect(wrapper.find('.t-crud-empty-cta').exists()).toBe(false)
    })

    it('hides the CTA on read-only lists (canCreate=false)', () => {
      const state = makeCreatableEmptyState()
      state.canCreate = false
      const wrapper = mount(TCardRenderer, {
        props: { state },
        slots: { card: '<div class="card-item" />' },
      })
      expect(wrapper.find('.t-crud-empty-cta').exists()).toBe(false)
    })

    it('a custom #empty slot still overrides the default visual (no CTA injected)', () => {
      const state = makeCreatableEmptyState()
      const wrapper = mount(TCardRenderer, {
        props: { state },
        slots: { card: '<div class="card-item" />', empty: '<div class="my-empty">none</div>' },
      })
      expect(wrapper.find('.my-empty').exists()).toBe(true)
      expect(wrapper.find('.t-crud-empty-cta').exists()).toBe(false)
    })
  })

  /**
   * `cols` stops at `xl`, and `xl` means "1280 and up, forever" - so a 1280
   * laptop and a 4K monitor got the same column count and the cards on the 4K
   * one grew to fill whatever was left. `minColWidth` states the thing the
   * design actually fixes and lets the count follow.
   */
  describe('minColWidth', () => {
    const gridTemplate = (wrapper: ReturnType<typeof mount>): string =>
      wrapper.find('.t-card-renderer__grid').attributes('style') ?? ''

    it('packs as many columns as fit instead of taking the count from cols', () => {
      const wrapper = mount(TCardRenderer, {
        props: { state: makeState() as any, minColWidth: 280 },
        slots: { card: '<div class="card-item" />' },
      })
      expect(gridTemplate(wrapper)).toContain('repeat(auto-fill, minmax(min(280px, 100%), 1fr))')
    })

    it('clamps the minimum to the container so a phone-width grid does not overflow', () => {
      // A bare `minmax(280px, 1fr)` overflows horizontally the moment the
      // container is narrower than 280 - which every phone is.
      const wrapper = mount(TCardRenderer, {
        props: { state: makeState() as any, minColWidth: 280 },
        slots: { card: '<div class="card-item" />' },
      })
      expect(gridTemplate(wrapper)).toContain('min(280px, 100%)')
    })

    it('leaves call sites that did not opt in exactly as they were', () => {
      const wrapper = mount(TCardRenderer, {
        props: { state: makeState() as any, cols: 3 },
        slots: { card: '<div class="card-item" />' },
      })
      expect(gridTemplate(wrapper)).toContain('repeat(3, minmax(0, 1fr))')
      expect(gridTemplate(wrapper)).not.toContain('auto-fill')
    })

    it('does not observe anything when the prop is absent', () => {
      const observe = vi.fn()
      vi.stubGlobal(
        'ResizeObserver',
        class {
          observe = observe
          disconnect = vi.fn()
        },
      )
      try {
        mount(TCardRenderer, {
          props: { state: makeState() as any, cols: 3 },
          slots: { card: '<div class="card-item" />' },
        })
        expect(observe).not.toHaveBeenCalled()
      } finally {
        vi.unstubAllGlobals()
      }
    })

    it('starts measuring when the prop arrives after mount', async () => {
      const observe = vi.fn()
      const disconnect = vi.fn()
      vi.stubGlobal(
        'ResizeObserver',
        class {
          observe = observe
          disconnect = disconnect
        },
      )
      try {
        const wrapper = mount(TCardRenderer, {
          props: { state: makeState() as any, cols: 3 },
          slots: { card: '<div class="card-item" />' },
        })
        expect(observe).not.toHaveBeenCalled()

        await wrapper.setProps({ minColWidth: 280 })
        expect(observe).toHaveBeenCalledTimes(1)

        // ...and stops again when it goes away, rather than leaving a live
        // observer feeding a width nothing reads.
        await wrapper.setProps({ minColWidth: undefined })
        expect(disconnect).toHaveBeenCalled()
      } finally {
        vi.unstubAllGlobals()
      }
    })

    it('counts the loading skeletons from the measured width, not from cols', async () => {
      // Eight placeholders in a seven-column grid read as a broken row rather
      // than as a list loading, so the skeleton count has to know what
      // `auto-fill` will do.
      let notify: (() => void) | undefined
      vi.stubGlobal(
        'ResizeObserver',
        class {
          constructor(fn: () => void) {
            notify = fn
          }
          observe = vi.fn()
          disconnect = vi.fn()
        },
      )
      try {
        const wrapper = mount(TCardRenderer, {
          // `cols: 3` is the fallback, and deliberately NOT the answer below.
          props: { state: makeState([], true) as any, cols: 3, minColWidth: 280 },
          slots: { card: '<div class="card-item" />' },
        })
        // Unmeasured (no layout in the test DOM) → the breakpoint count stands.
        expect(wrapper.findAll('.t-card-renderer__skeleton')).toHaveLength(6)

        // 1642px of grid at a 16px gap fits floor((1642+16)/(280+16)) = 5.
        Object.defineProperty(wrapper.find('.t-card-renderer').element, 'clientWidth', {
          value: 1642,
          configurable: true,
        })
        notify?.()
        await nextTick()
        expect(wrapper.findAll('.t-card-renderer__skeleton')).toHaveLength(10)
      } finally {
        vi.unstubAllGlobals()
      }
    })
  })
})
