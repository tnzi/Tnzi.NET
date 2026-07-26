import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { ref } from 'vue'
import TCrudSearchDrawer from '../../../src/components/crud/TCrudSearchDrawer.vue'
import type { FormSchemaItem } from '../../../src/pages/_shared/form-schema'

const stubs = {
  Drawer: { name: 'Drawer', props: ['show'], template: '<div v-if="show" class="n-drawer-stub"><slot /></div>' },
  DrawerContent: { name: 'DrawerContent', template: '<div class="n-drawer-content-stub"><slot /><slot name="footer" /></div>' },
  Button: { name: 'Button', template: '<button @click="$emit(\'click\')"><slot /></button>' },
  Form: { name: 'Form', template: '<form><slot /></form>' },
  FormItem: { name: 'FormItem', template: '<div><slot /></div>' },
  Grid: { name: 'Grid', template: '<div><slot /></div>' },
  GridItem: { name: 'GridItem', template: '<div><slot /></div>' },
  Space: { name: 'Space', template: '<div><slot /></div>' },
  Input: { name: 'Input', props: ['value'], template: '<input />' },
}
const fields: FormSchemaItem[] = [{ key: 'name', label: 'Name', type: 'text' }]
function makeState() {
  return { query: ref({ searchText: '' }), setSearch: () => {}, setFilters: () => {}, refresh: async () => undefined }
}

describe('TCrudSearchDrawer', () => {
  it('renders the advanced grid inside a drawer when shown', () => {
    const w = mount(TCrudSearchDrawer, { props: { show: true, state: makeState() as any, searchFields: fields }, global: { stubs } })
    expect(w.find('.n-drawer-stub').exists()).toBe(true)
    expect(w.find('.t-crud-search-advanced').exists()).toBe(true)
  })

  it('wraps the drawer in TOverlayTheme so it follows the GLOBAL mode (not a dark card surface)', () => {
    const w = mount(TCrudSearchDrawer, { props: { show: true, state: makeState() as any, searchFields: fields }, global: { stubs } })
    expect(w.findComponent({ name: 'TOverlayTheme' }).exists()).toBe(true)
  })

  it('does not render drawer content when show=false', () => {
    const w = mount(TCrudSearchDrawer, { props: { show: false, state: makeState() as any, searchFields: fields }, global: { stubs } })
    expect(w.find('.n-drawer-stub').exists()).toBe(false)
  })

  it('emits update:show=false when the footer Search is clicked (apply + close)', async () => {
    const w = mount(TCrudSearchDrawer, { props: { show: true, state: makeState() as any, searchFields: fields }, global: { stubs } })
    const buttons = w.findAll('.n-drawer-content-stub button')
    await buttons[buttons.length - 1].trigger('click')
    expect(w.emitted('update:show')?.some((e) => e[0] === false)).toBe(true)
  })
})
