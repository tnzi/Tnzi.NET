import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { ref, computed } from 'vue'
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
})
