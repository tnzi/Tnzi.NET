import { describe, it, expect, vi, beforeEach } from 'vitest'
import { ref } from 'vue'
import { mount, flushPromises } from '@vue/test-utils'

/**
 * The drag library is not installed at all.
 *
 * `vue-draggable-plus` is an optional peer. The column settings popover is part
 * of every CRUD table, so a static import of it made the package required for
 * any app that renders one. Without it the rows still list every column and the
 * visibility toggles still work; only reordering is unavailable, and the drag
 * handle is not shown, so nothing offers a gesture that cannot happen.
 */
vi.mock('vue-draggable-plus', () => {
  throw new Error("Cannot find module 'vue-draggable-plus'")
})

import TCrudColumnSetting from '../../../src/components/crud/TCrudColumnSetting.vue'

const popoverStub = {
  name: 'Popover',
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
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
  } as any
}

const allColumns = [
  { key: 'name', title: 'Name' },
  { key: 'note', title: 'Note' },
]

function mountPopover(settings = makeSettings()) {
  return mount(TCrudColumnSetting, {
    props: { settings, allColumns, show: true },
    global: { stubs: { Popover: popoverStub, Checkbox: checkboxStub } },
  })
}

describe('TCrudColumnSetting when the drag library is not installed', () => {
  beforeEach(() => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    vi.spyOn(console, 'error').mockImplementation(() => {})
  })

  it('★ still lists every column', async () => {
    const wrapper = mountPopover()
    await flushPromises()

    expect(wrapper.findAll('.t-crud-column-setting__row')).toHaveLength(2)
    expect(wrapper.find('.t-crud-column-setting__list').exists()).toBe(true)
  })

  it('does not show a drag handle that cannot drag', async () => {
    const wrapper = mountPopover()
    await flushPromises()

    expect(wrapper.find('.t-crud-column-setting__drag').exists()).toBe(false)
  })

  it('still toggles a column', async () => {
    const settings = makeSettings()
    const wrapper = mountPopover(settings)
    await flushPromises()

    const rowCheckboxes = wrapper.findAll('.t-crud-column-setting__row input[type="checkbox"]')
    await rowCheckboxes[1].trigger('change')
    expect(settings.toggle).toHaveBeenCalledWith('note')
  })
})
