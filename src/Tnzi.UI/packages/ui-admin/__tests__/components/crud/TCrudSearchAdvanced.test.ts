import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { ref } from 'vue'
import TCrudSearchAdvanced from '../../../src/components/crud/TCrudSearchAdvanced.vue'
import type { FormSchemaItem } from '../../../src/pages/_shared/form-schema'

// naive-ui's internal component name for NGi is 'GridItem', not 'Gi'.
// vue-test-utils matches stubs against the component's `name` field.
const stubs = {
  Form: { name: 'Form', template: '<form><slot /></form>' },
  FormItem: { name: 'FormItem', template: '<div><slot /></div>' },
  Grid: { name: 'Grid', template: '<div><slot /></div>' },
  GridItem: { name: 'GridItem', template: '<div><slot /></div>' },
  Space: { name: 'Space', template: '<div><slot /></div>' },
  Button: { name: 'Button', template: '<button @click="$emit(\'click\')"><slot /></button>' },
  Input: { name: 'Input', props: ['value'], template: '<input />' },
}
const fields: FormSchemaItem[] = [{ key: 'name', label: 'Name', type: 'text' }]
function makeState() {
  return { query: ref({ searchText: '' }), setSearch: vi.fn(), setFilters: vi.fn(), refresh: vi.fn(async () => undefined) }
}

describe('TCrudSearchAdvanced', () => {
  it('hides the inline submit button when hideSubmit=true', () => {
    const w = mount(TCrudSearchAdvanced, { props: { state: makeState() as any, searchFields: fields, hideSubmit: true }, global: { stubs } })
    expect(w.find('.t-crud-page__search-actions').exists()).toBe(false)
  })

  it('exposes apply() which commits filters + refresh, and reset() which clears', async () => {
    const state = makeState()
    const w = mount(TCrudSearchAdvanced, { props: { state: state as any, searchFields: fields, hideSubmit: true }, global: { stubs } })
    const vm = w.vm as unknown as { apply: () => void; reset: () => void }
    vm.apply()
    expect(state.setFilters).toHaveBeenCalled()
    expect(state.refresh).toHaveBeenCalled()
    vm.reset()
    expect(state.setFilters).toHaveBeenLastCalledWith({})
  })
})
