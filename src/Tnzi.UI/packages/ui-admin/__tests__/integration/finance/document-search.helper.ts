import { expect, vi } from 'vitest'
import { mount, type VueWrapper } from '@vue/test-utils'
import TCrudPage from '../../../src/components/crud/TCrudPage.vue'
import TItemPage from '../../../src/components/crud/TItemPage.vue'
import TCrudSearchAdvanced, { type SearchFieldItem } from '../../../src/components/crud/TCrudSearchAdvanced.vue'

const searchStubs = {
  Form: { name: 'Form', template: '<form><slot /></form>' },
  FormItem: { name: 'FormItem', template: '<div><slot /></div>' },
  Grid: { name: 'Grid', template: '<div><slot /></div>' },
  GridItem: { name: 'GridItem', template: '<div><slot /></div>' },
  Select: { name: 'Select', template: '<i />' },
  DatePicker: { name: 'DatePicker', props: ['value'], template: '<i />' },
  TPartySelect: { name: 'TPartySelect', props: ['modelValue'], template: '<i />' },
}

/**
 * Drives a document list's advanced search the way an operator does (pick a
 * party, pick a "from" date, apply) and returns the filters the list is asked
 * for. The list endpoint binds those keys by name: a party key it does not
 * declare is dropped without an error, and a date that is not a date fails the
 * whole request.
 */
export async function applyDocumentSearch(page: VueWrapper): Promise<Record<string, unknown>> {
  const shell = page.findComponent(TCrudPage).exists() ? page.findComponent(TCrudPage) : page.findComponent(TItemPage)
  const fields = shell.props('searchFields') as SearchFieldItem[]
  const state = { query: { value: { searchText: '' } }, setSearch: vi.fn(), setFilters: vi.fn(), refresh: vi.fn(async () => undefined) }
  const search = mount(TCrudSearchAdvanced, {
    props: { state: state as never, searchFields: fields, hideSubmit: true },
    global: { stubs: searchStubs },
  })

  // The first date field is the "from" end of the range.
  search.findAllComponents({ name: 'DatePicker' })[0]!.vm.$emit('update:value', new Date(2026, 0, 15).getTime())
  search.findComponent({ name: 'TPartySelect' }).vm.$emit('update:modelValue', 'party-1')
  await search.vm.$nextTick()
  ;(search.vm as unknown as { apply: () => void }).apply()

  expect(state.setFilters).toHaveBeenCalledTimes(1)
  return state.setFilters.mock.calls[0]![0] as Record<string, unknown>
}
