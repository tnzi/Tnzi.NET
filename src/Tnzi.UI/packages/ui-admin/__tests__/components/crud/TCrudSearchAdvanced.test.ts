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

// NDatePicker answers with the local midnight of the picked day as epoch
// milliseconds. Every list endpoint binds its date filters to `DateTime?`, so
// a number on the query string fails model binding (400) and the list never
// loads. The filter carries the picked calendar date instead.
describe('TCrudSearchAdvanced date fields', () => {
  const dateStubs = { ...stubs, DatePicker: { name: 'DatePicker', props: ['value'], template: '<i />' } }

  it('sends the picked calendar date, not the picker timestamp', async () => {
    const state = makeState()
    const dateFields: FormSchemaItem[] = [
      { key: 'status', label: 'Status', type: 'select', options: [] },
      { key: 'dateFrom', label: 'From', type: 'date' },
    ]
    const w = mount(TCrudSearchAdvanced, { props: { state: state as any, searchFields: dateFields, hideSubmit: true }, global: { stubs: dateStubs } })

    w.findComponent({ name: 'DatePicker' }).vm.$emit('update:value', new Date(2026, 0, 15).getTime())
    await w.vm.$nextTick()
    ;(w.vm as unknown as { apply: () => void }).apply()

    expect(state.setFilters).toHaveBeenCalledWith({ dateFrom: '2026-01-15' })
  })

  it('follows a field whose type is decided by the model', async () => {
    const state = makeState()
    const dateFields: FormSchemaItem[] = [{ key: 'on', label: 'On', type: 'text', typeFn: () => 'date' }]
    const w = mount(TCrudSearchAdvanced, { props: { state: state as any, searchFields: dateFields, hideSubmit: true }, global: { stubs: dateStubs } })

    w.findComponent({ name: 'DatePicker' }).vm.$emit('update:value', new Date(2026, 11, 31).getTime())
    await w.vm.$nextTick()
    ;(w.vm as unknown as { apply: () => void }).apply()

    expect(state.setFilters).toHaveBeenCalledWith({ on: '2026-12-31' })
  })
})

// The drawer and the phone panel unmount whenever they close, and a page can
// set a filter itself (a deep-linked id). A form that started empty showed
// nothing for a filter still narrowing the list, and its next Search replaced
// the filters with only what was on screen, dropping the rest.
describe('TCrudSearchAdvanced opens on the filters in effect', () => {
  const seedStubs = {
    ...stubs,
    Select: { name: 'Select', props: ['value'], template: '<i />' },
    DatePicker: { name: 'DatePicker', props: ['value'], template: '<i />' },
  }
  const docFields: FormSchemaItem[] = [
    { key: 'status', label: 'Status', type: 'select', options: [] },
    { key: 'dateFrom', label: 'From', type: 'date' },
  ]
  function stateWith(filters: Record<string, unknown>) {
    return { ...makeState(), query: ref({ searchText: '', filters }) }
  }

  it('shows an applied filter after reopening, and the next Search keeps it', async () => {
    const state = stateWith({ status: 'Posted' })
    const w = mount(TCrudSearchAdvanced, { props: { state: state as any, searchFields: docFields, hideSubmit: true }, global: { stubs: seedStubs } })

    expect(w.findComponent({ name: 'Select' }).props('value')).toBe('Posted')
    w.findComponent({ name: 'DatePicker' }).vm.$emit('update:value', new Date(2026, 9, 1).getTime())
    await w.vm.$nextTick()
    ;(w.vm as unknown as { apply: () => void }).apply()

    expect(state.setFilters).toHaveBeenCalledWith({ status: 'Posted', dateFrom: '2026-10-01' })
  })

  it('shows an applied date as the picked day', () => {
    const state = stateWith({ dateFrom: '2026-10-01' })
    const w = mount(TCrudSearchAdvanced, { props: { state: state as any, searchFields: docFields, hideSubmit: true }, global: { stubs: seedStubs } })
    expect(w.findComponent({ name: 'DatePicker' }).props('value')).toBe(new Date(2026, 9, 1).getTime())
  })

  it('follows a filter set from outside while it is open', async () => {
    const state = makeState() as ReturnType<typeof makeState> & { query: { value: { searchText: string; filters?: Record<string, unknown> } } }
    const w = mount(TCrudSearchAdvanced, { props: { state: state as any, searchFields: fields, hideSubmit: true }, global: { stubs } })
    expect(w.findComponent({ name: 'Input' }).props('value')).toBeNull()

    state.query.value = { searchText: '', filters: { name: 'alice' } }
    await w.vm.$nextTick()

    expect(w.findComponent({ name: 'Input' }).props('value')).toBe('alice')
  })

  it('leaves a filter no field shows in place on Search and on Reset', async () => {
    const state = stateWith({ categoryId: 'c1' })
    const w = mount(TCrudSearchAdvanced, { props: { state: state as any, searchFields: fields, hideSubmit: true }, global: { stubs } })
    const vm = w.vm as unknown as { apply: () => void; reset: () => void }

    w.findComponent({ name: 'Input' }).vm.$emit('update:value', 'alice')
    await w.vm.$nextTick()
    vm.apply()
    expect(state.setFilters).toHaveBeenLastCalledWith({ categoryId: 'c1', name: 'alice' })

    vm.reset()
    expect(state.setFilters).toHaveBeenLastCalledWith({ categoryId: 'c1' })
  })
})

// Search configs follow the shared schema shape: `labelKey` / `placeholderKey`
// and option `labelKey` render through the page translator (the English label
// is the fallback), and `visible` hides a field. The advanced search read only
// the English literals, so a translated admin saw its create / edit forms in
// its language and the same fields in English one panel over.
describe('TCrudSearchAdvanced resolves the schema keys the forms resolve', () => {
  const keyStubs = {
    ...stubs,
    FormItem: { name: 'FormItem', props: ['label'], template: '<div class="item" :data-label="label"><slot /></div>' },
    Input: { name: 'Input', props: ['value', 'placeholder'], template: '<input :placeholder="placeholder" />' },
    Select: { name: 'Select', props: ['value', 'options', 'placeholder'], template: '<i class="select" />' },
  }
  const dictionary: Record<string, string> = {
    'form.isLockedOut': '锁定状态',
    'form.keyword': '关键字',
    'search.keywordHint': '用户名或邮箱',
    'status.locked': '已锁定',
  }
  const translate = (key: string) => dictionary[key] ?? `?${key}`
  const schema: FormSchemaItem[] = [
    { key: 'keyword', labelKey: 'form.keyword', label: 'Keyword', type: 'text', placeholderKey: 'search.keywordHint' },
    {
      key: 'isLockedOut',
      labelKey: 'form.isLockedOut',
      label: 'Lock Status',
      type: 'select',
      options: [
        { labelKey: 'status.locked', label: 'Locked', value: 'true' },
        { label: 'Unlocked', value: 'false' },
      ],
    },
    { key: 'hidden', label: 'Hidden', type: 'text', visible: () => false },
  ]

  function mountLabeled(state = makeState()) {
    return mount(TCrudSearchAdvanced, {
      props: { state: state as any, searchFields: schema, translate, labeled: true, hideSubmit: true },
      global: { stubs: keyStubs },
    })
  }

  it('labels a field by its labelKey and its placeholder by placeholderKey', () => {
    const w = mountLabeled()
    expect(w.findAll('.item').map((n) => n.attributes('data-label'))).toEqual(['关键字', '锁定状态'])
    expect(w.find('input').attributes('placeholder')).toBe('用户名或邮箱')
  })

  it('labels a select option by its labelKey, keeping the literal where there is none', () => {
    const w = mountLabeled()
    const options = w.findComponent({ name: 'Select' }).props('options') as Array<{ label: string; value: string }>
    expect(options).toEqual([{ label: '已锁定', value: 'true' }, { label: 'Unlocked', value: 'false' }])
  })

  it('hides a field whose visible() is false and leaves it out of the filters', async () => {
    // A value it held (here from the filters in effect) would otherwise keep
    // narrowing the list with nothing on screen to show or clear it.
    const state = { ...makeState(), query: ref({ searchText: '', filters: { hidden: 'x' } }) }
    const w = mountLabeled(state)
    expect(w.findAll('.item')).toHaveLength(2)
    ;(w.vm as unknown as { apply: () => void }).apply()
    expect(state.setFilters).toHaveBeenCalledWith({})
  })
})
