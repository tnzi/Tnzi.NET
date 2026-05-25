import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { ref } from 'vue'
import TCrudSearch, {
  type SearchableState,
} from '../../../src/components/crud/TCrudSearch.vue'
import type { FormSchemaItem } from '../../../src/pages/_shared/form-schema'

function makeState(overrides: Partial<SearchableState> = {}): SearchableState & {
  setSearch: ReturnType<typeof vi.fn>
  setFilters: ReturnType<typeof vi.fn>
  refresh: ReturnType<typeof vi.fn>
} {
  return {
    query: ref({ searchText: '' }),
    setSearch: vi.fn(),
    setFilters: vi.fn(),
    refresh: vi.fn(async () => undefined),
    ...overrides,
  } as never
}

describe('TCrudSearch', () => {
  it('renders the simple keyword row by default', () => {
    const state = makeState()
    const wrapper = mount(TCrudSearch, { props: { state } })
    expect(wrapper.find('.t-crud-page__search-simple').exists()).toBe(true)
  })

  it('hides the simple row when hideSimpleMode=true', () => {
    const state = makeState()
    const wrapper = mount(TCrudSearch, {
      props: { state, hideSimpleMode: true },
    })
    expect(wrapper.find('.t-crud-page__search-simple').exists()).toBe(false)
  })

  it('hides the Advanced toggle when no searchFields are supplied', () => {
    const state = makeState()
    const wrapper = mount(TCrudSearch, { props: { state } })
    expect(wrapper.find('.t-crud-page__search-toggle').exists()).toBe(false)
  })

  it('shows the Advanced toggle when searchFields are supplied', () => {
    const state = makeState()
    const fields: FormSchemaItem[] = [
      { key: 'name', label: 'Name', type: 'text' },
    ]
    const wrapper = mount(TCrudSearch, {
      props: { state, searchFields: fields },
    })
    expect(wrapper.find('.t-crud-page__search-toggle').exists()).toBe(true)
  })

  it('commits keyword + calls refresh when the Search button is clicked', async () => {
    const state = makeState()
    const wrapper = mount(TCrudSearch, { props: { state } })
    const input = wrapper.find('.t-crud-page__search-simple-input input')
    await input.setValue('alice')
    // First primary NButton in the simple row.
    await wrapper.find('button').trigger('click')
    expect(state.setSearch).toHaveBeenCalledWith('alice')
    expect(state.refresh).toHaveBeenCalledOnce()
  })

  it('commits an empty keyword + refresh when the input clears', async () => {
    const state = makeState({ query: ref({ searchText: 'previous' }) } as never)
    const wrapper = mount(TCrudSearch, { props: { state } })
    // NInput's clear() invokes the `clear` event handler — emulate by
    // toggling the value to empty + triggering Enter (the test path uses
    // the keydown.enter handler that mirrors the same flow).
    const input = wrapper.find('.t-crud-page__search-simple-input input')
    await input.setValue('')
    await input.trigger('keydown.enter')
    expect(state.setSearch).toHaveBeenLastCalledWith('')
    expect(state.refresh).toHaveBeenCalled()
  })

  it('renders advanced fields when toggle is opened', async () => {
    const state = makeState()
    const fields: FormSchemaItem[] = [
      { key: 'name', label: 'Name', type: 'text' },
      { key: 'active', label: 'Active', type: 'switch' },
    ]
    const wrapper = mount(TCrudSearch, {
      props: { state, searchFields: fields, defaultAdvancedMode: true },
    })
    expect(wrapper.find('.t-crud-page__search-advanced').exists()).toBe(true)
  })

  it('hides simple row when hideSimpleMode=true even with searchFields', () => {
    const state = makeState()
    const fields: FormSchemaItem[] = [
      { key: 'name', label: 'Name', type: 'text' },
    ]
    const wrapper = mount(TCrudSearch, {
      props: { state, searchFields: fields, hideSimpleMode: true },
    })
    expect(wrapper.find('.t-crud-page__search-simple').exists()).toBe(false)
    // Note: in the legacy TCrudPage behaviour preserved by 0.2.72+ (B5),
    // `hasAdvancedSearch` is `!hideSimpleMode && !!searchFields`, so the
    // advanced panel only renders when simple mode is also reachable.
    // hideSimpleMode without an alternative panel is effectively
    // "no search UI at all" — pages wanting advanced-only should pass
    // a `#search` slot to the parent TCrudPage instead.
    expect(wrapper.find('.t-crud-page__search-advanced').exists()).toBe(false)
  })

  it('passes translate fn through to button labels', async () => {
    const state = makeState()
    const wrapper = mount(TCrudSearch, {
      props: {
        state,
        translate: (key) => (key === 'admin.crud.search' ? 'Find' : key),
      },
    })
    expect(wrapper.text()).toContain('Find')
  })
})
