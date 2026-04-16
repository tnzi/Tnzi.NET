import { describe, it, expect, vi } from 'vitest'
import { ref } from 'vue'
import { mount } from '@vue/test-utils'
import TCrudColumnSetting from '../../../src/components/crud/TCrudColumnSetting.vue'

const popoverStub = {
  name: 'Popover',
  props: ['show'],
  template:
    '<div v-if="show" class="n-popover-stub"><slot name="trigger" /><slot /></div>',
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
    hide: vi.fn(),
    show: vi.fn(),
    toggle: vi.fn(),
    reorder: vi.fn(),
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

  it('clicking a checkbox toggles column visibility', async () => {
    const settings = makeSettings()
    const wrapper = mount(TCrudColumnSetting, {
      props: { settings, allColumns, show: true },
      global: { stubs },
    })
    const checkboxes = wrapper.findAll('input[type="checkbox"]')
    expect(checkboxes.length).toBeGreaterThan(0)
    await checkboxes[0].trigger('change')
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
