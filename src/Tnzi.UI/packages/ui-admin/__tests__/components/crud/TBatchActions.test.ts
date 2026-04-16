import { describe, it, expect, vi } from 'vitest'
import { ref, computed } from 'vue'
import { mount } from '@vue/test-utils'
import TBatchActions from '../../../src/components/crud/TBatchActions.vue'

function makeState(count: number) {
  const selected = ref(new Set(Array.from({ length: count }, (_, i) => String(i))))
  return {
    selected,
    selectedCount: computed(() => selected.value.size),
    hasSelection: computed(() => selected.value.size > 0),
    selectedIds: computed(() => [...selected.value]),
    select: vi.fn(),
    unselect: vi.fn(),
    toggle: vi.fn(),
    selectAll: vi.fn(),
    clear: vi.fn(),
    isSelected: vi.fn(),
  } as any
}

describe('TBatchActions', () => {
  it('is hidden when no selection', () => {
    const state = makeState(0)
    const wrapper = mount(TBatchActions, { props: { state } })
    expect(wrapper.find('.t-batch-actions').exists()).toBe(false)
  })

  it('is visible when selection exists and shows count', () => {
    const state = makeState(3)
    const wrapper = mount(TBatchActions, { props: { state } })
    expect(wrapper.find('.t-batch-actions').exists()).toBe(true)
    expect(wrapper.find('.t-batch-actions__count').text()).toContain('3')
  })

  it('Unselect all calls state.clear()', async () => {
    const state = makeState(2)
    const wrapper = mount(TBatchActions, { props: { state } })
    await wrapper.find('.t-batch-actions__clear').trigger('click')
    expect(state.clear).toHaveBeenCalled()
  })

  it('renders default slot content', () => {
    const state = makeState(2)
    const wrapper = mount(TBatchActions, {
      props: { state },
      slots: { default: '<button class="del">Delete</button>' },
    })
    expect(wrapper.find('.del').exists()).toBe(true)
  })
})
