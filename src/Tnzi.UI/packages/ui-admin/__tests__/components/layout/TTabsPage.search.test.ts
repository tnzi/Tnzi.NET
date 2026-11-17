import { describe, it, expect, vi, afterEach } from 'vitest'
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { computed, defineComponent, h, ref } from 'vue'
import TTabsPage from '../../../src/components/layout/TTabsPage.vue'
import TListShell from '../../../src/components/crud/TListShell.vue'
import TOverlayTheme from '../../../src/components/overlay/TOverlayTheme.vue'

// The rule under test: on a tabs page the active tab's list search sits in the
// PAGE HEADER bar (like a top-level list page), and the list toolbar keeps only
// filters and list actions.

const narrow = ref(false)
vi.mock('../../../src/headless/useBreakpoint', () => ({
  useBreakpoint: () => ({ width: ref(1280), isSm: narrow, isMd: ref(false), isLg: ref(false) }),
}))

const routeQuery: Record<string, string> = {}
vi.mock('vue-router', () => ({
  useRoute: () => ({ query: routeQuery, params: {}, path: '/x', fullPath: '/x', hash: '', name: 'x', meta: {} }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), back: vi.fn() }),
}))

function makeState() {
  const selected = ref<Set<number>>(new Set())
  return {
    query: ref({ pageIndex: 1, pageSize: 20, searchText: '', sortField: undefined, sortOrder: null, filters: {} }),
    items: ref([]),
    total: ref(0),
    loading: ref(false),
    error: ref<Error | null>(null),
    hasData: computed(() => false),
    batchActions: { selected, selectedIds: computed(() => [...selected.value]), selectedCount: computed(() => selected.value.size), clear: vi.fn() },
    formModal: { visible: ref(false), mode: ref(null), formData: ref(null), open: vi.fn(), close: vi.fn() },
    canCreate: true, canUpdate: true, canDelete: true,
    refresh: vi.fn(), setPage: vi.fn(), setPageSize: vi.fn(), setSearch: vi.fn(), setFilters: vi.fn(),
    openCreate: vi.fn(), submit: vi.fn(), handleDelete: vi.fn(), exportAll: vi.fn(), importFile: vi.fn(), dismissError: vi.fn(),
  }
}

type ListSpec = { placeholder: string; state?: ReturnType<typeof makeState>; showSearch?: boolean; inOverlay?: boolean }

function list(spec: ListSpec) {
  const shell = h(TListShell as never, {
    state: spec.state ?? makeState(),
    showHeader: false,
    showSearch: spec.showSearch ?? true,
    searchPlaceholder: spec.placeholder,
  }, { toolbarLeft: () => h('span', { class: 'filter-marker' }, 'filter'), renderer: () => h('div') })
  return spec.inOverlay ? h(TOverlayTheme, null, { default: () => shell }) : shell
}

let wrapper: VueWrapper | null = null
afterEach(() => {
  wrapper?.unmount()
  wrapper = null
  narrow.value = false
  for (const k of Object.keys(routeQuery)) delete routeQuery[k]
})

async function mountPage(panes: Record<string, ListSpec[]>, section?: string) {
  setActivePinia(createPinia())
  if (section) routeQuery.section = section
  const Host = defineComponent({
    setup: () => () =>
      h(TTabsPage, {
        title: 'Hosting',
        sections: Object.keys(panes).map((name) => ({ name, label: name })),
      }, Object.fromEntries(Object.entries(panes).map(([name, specs]) => [name, () => specs.map(list)]))),
  })
  wrapper = mount(Host, { attachTo: document.body })
  await flushPromises()
  return wrapper
}

const headerInputs = () => [...document.querySelectorAll('.t-page-header .t-list-shell__search-input input')]
const toolbarInputs = () => [...document.querySelectorAll('.t-list-shell__toolbar .t-list-shell__search-input input')]
const placeholderOf = (el: Element) => (el as HTMLInputElement).placeholder

describe('TTabsPage header search', () => {
  it("puts the active tab's list search in the page header, not in the list toolbar", async () => {
    await mountPage({ apps: [{ placeholder: 'Search apps' }] })

    expect(headerInputs().map(placeholderOf)).toEqual(['Search apps'])
    expect(toolbarInputs()).toHaveLength(0)
    // The toolbar keeps the filters (left) and the list actions (right).
    expect(document.querySelector('.t-list-shell__toolbar-left .filter-marker')).not.toBeNull()
    expect(document.querySelector('.t-list-shell__toolbar .t-list-shell__refresh')).not.toBeNull()
  })

  it('drives the list from the header input', async () => {
    const state = makeState()
    await mountPage({ apps: [{ placeholder: 'Search apps', state }] })

    const input = headerInputs()[0] as HTMLInputElement
    input.value = 'tnzi'
    input.dispatchEvent(new Event('input', { bubbles: true }))
    await flushPromises()
    document.querySelector<HTMLButtonElement>('.t-page-header .t-list-shell__search-submit')!.click()
    await flushPromises()

    expect(state.setSearch).toHaveBeenCalledWith('tnzi')
    expect(state.refresh).toHaveBeenCalled()
  })

  it("shows the other tab's search after a tab switch, and none on a tab without one", async () => {
    await mountPage({
      apps: [{ placeholder: 'Search apps' }],
      domains: [{ placeholder: 'Search domains' }],
      logs: [{ placeholder: 'unused', showSearch: false }],
    }, 'domains')
    expect(headerInputs().map(placeholderOf)).toEqual(['Search domains'])

    await wrapper!.findAll('.n-tabs-tab').find((t) => t.text() === 'logs')!.trigger('click')
    await flushPromises()
    expect(headerInputs()).toHaveLength(0)
    expect(document.querySelector('.t-tabs-page__search')).toBeNull()
  })

  it('keeps each search in its own toolbar when a tab holds two searchable lists', async () => {
    await mountPage({ both: [{ placeholder: 'Search A' }, { placeholder: 'Search B' }] })

    expect(headerInputs()).toHaveLength(0)
    expect(toolbarInputs().map(placeholderOf)).toEqual(['Search A', 'Search B'])
  })

  it('never moves the search of a list inside an overlay into the page header', async () => {
    await mountPage({ apps: [{ placeholder: 'Search picker', inOverlay: true }] })

    expect(headerInputs()).toHaveLength(0)
    expect(toolbarInputs().map(placeholderOf)).toEqual(['Search picker'])
  })

  it('on phones puts the search toggles in the header and expands the form inside the header card', async () => {
    narrow.value = true
    await mountPage({ apps: [{ placeholder: 'Search apps' }] })

    const header = document.querySelector('.t-page-header')!
    expect(header.classList.contains('t-page-header--inline-actions')).toBe(true)
    const toggle = header.querySelector<HTMLButtonElement>('.t-list-shell__search--mobile .t-list-shell__search-icon')
    expect(toggle).not.toBeNull()
    expect(document.querySelector('.t-list-shell__toolbar .t-list-shell__search--mobile')).toBeNull()

    toggle!.click()
    await flushPromises()
    const panel = header.querySelector('.t-list-shell__mobile-search--tabs-header')
    expect(panel).not.toBeNull()
    expect(panel!.querySelector<HTMLInputElement>('input')!.placeholder).toBe('Search apps')
  })
})
