import { describe, it, expect, vi } from 'vitest'
import { ref } from 'vue'
import { mount } from '@vue/test-utils'
import TCrudColumnSetting from '../../../src/components/crud/TCrudColumnSetting.vue'

const popoverStub = {
  name: 'Popover',
  // After moving NPopover from `:show` (controlled) to `:default-show`
  // (uncontrolled with outside-click auto-close), the test stub also
  // needs to accept either prop. Mirrors Naive UI's actual API surface.
  props: ['show', 'defaultShow'],
  template:
    '<div v-if="show || defaultShow" class="n-popover-stub"><slot name="trigger" /><slot /></div>',
}

const checkboxStub = {
  name: 'Checkbox',
  props: ['checked'],
  emits: ['update:checked'],
  template:
    '<input type="checkbox" :checked="checked" @change="$emit(\'update:checked\', $event.target.checked)" />',
}

const draggableStub = {
  name: 'VueDraggable',
  props: ['modelValue'],
  emits: ['update:modelValue'],
  template: '<div class="draggable-stub"><slot /></div>',
}

const stubs = {
  Popover: popoverStub,
  Checkbox: checkboxStub,
  VueDraggable: draggableStub,
}

function makeSettings() {
  return {
    visibleColumns: ref([{ key: 'name', title: 'Name' }]),
    orderedKeys: ref(['name', 'note']),
    hiddenKeys: ref(new Set(['note'])),
    fixedOverrides: ref(new Map()),
    hide: vi.fn(),
    show: vi.fn(),
    toggle: vi.fn(),
    reorder: vi.fn(),
    cycleFixed: vi.fn(),
    getFixed: vi.fn(() => undefined),
    reset: vi.fn(),
  } as any
}

const allColumns = [
  { key: 'name', title: 'Name' },
  { key: 'note', title: 'Note' },
]

describe('TCrudColumnSetting', () => {
  it('renders all columns (visible + hidden) as rows', () => {
    const settings = makeSettings()
    const wrapper = mount(TCrudColumnSetting, {
      props: { settings, allColumns, show: true },
      global: { stubs },
    })
    const rows = wrapper.findAll('.t-crud-column-setting__row')
    expect(rows).toHaveLength(2)
  })

  it('clicking a column row checkbox toggles column visibility', async () => {
    // New header layout adds a tri-state "Select all" checkbox in front of
    // the per-column rows, so the first <input type="checkbox"> in the DOM
    // is the select-all. Index [1] is the first column row ('name').
    const settings = makeSettings()
    const wrapper = mount(TCrudColumnSetting, {
      props: { settings, allColumns, show: true },
      global: { stubs },
    })
    const checkboxes = wrapper.findAll('input[type="checkbox"]')
    expect(checkboxes.length).toBeGreaterThanOrEqual(2)
    await checkboxes[1].trigger('change')
    expect(settings.toggle).toHaveBeenCalledWith('name')
  })

  it('reset button calls settings.reset()', async () => {
    const settings = makeSettings()
    const wrapper = mount(TCrudColumnSetting, {
      props: { settings, allColumns, show: true },
      global: { stubs },
    })
    await wrapper.find('.t-crud-column-setting__reset').trigger('click')
    expect(settings.reset).toHaveBeenCalled()
  })
})
