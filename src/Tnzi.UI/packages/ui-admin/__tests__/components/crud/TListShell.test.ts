import { describe, it, expect, vi, afterEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { ref, computed } from 'vue'
import TListShell from '../../../src/components/crud/TListShell.vue'

const downloadBlob = vi.fn()
vi.mock('@tnzi/core/utils', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@tnzi/core/utils')>()),
  downloadBlob: (...args: unknown[]) => downloadBlob(...args),
}))

vi.mock('../../../src/headless/useBreakpoint', () => ({
  useBreakpoint: () => ({
    width: ref(1280),
    isSm: ref((globalThis as any).__bpNarrow ?? false),
    isMd: ref(false),
    isLg: ref(false),
  }),
}))

const stubs = {
  Pagination: { name: 'Pagination', props: ['page', 'itemCount', 'pageSize'], template: '<div class="n-pagination-stub" />' },
  Button: { name: 'Button', template: '<button @click="$emit(\'click\')"><slot /></button>' },
  Modal: { name: 'Modal', props: ['show'], template: '<div v-if="show" class="n-modal-stub"><slot /><slot name="footer" /></div>' },
  Card: { name: 'Card', template: '<div class="n-card-stub"><slot name="header" /><slot name="header-extra" /><slot /></div>' },
  Popconfirm: { name: 'Popconfirm', template: '<div><slot name="trigger" /><slot /></div>' },
  Popover: { name: 'Popover', template: '<div><slot name="trigger" /><slot /></div>' },
  Alert: { name: 'Alert', props: ['title'], template: '<div class="n-alert-stub"><slot /></div>' },
  SvgIcon: true,
}

function makeState(overrides: Record<string, unknown> = {}) {
  const selected = ref<Set<number>>(new Set())
  return {
    query: ref({ pageIndex: 1, pageSize: 20, searchText: '', sortField: undefined, sortOrder: null, filters: {} }),
    items: ref([{ id: 1 }, { id: 2 }]),
    total: ref(2),
    loading: ref(false),
    error: ref<Error | null>(null),
    hasData: computed(() => true),
    columnSettings: { visibleColumns: ref([]), orderedKeys: ref([]), hiddenKeys: ref(new Set()), fixedOverrides: ref(new Map()), hide: vi.fn(), show: vi.fn(), toggle: vi.fn(), reorder: vi.fn(), cycleFixed: vi.fn(), getFixed: vi.fn(), reset: vi.fn() },
    batchActions: { selected, selectedIds: computed(() => [...selected.value]), selectedCount: computed(() => selected.value.size), hasSelection: computed(() => selected.value.size > 0), select: vi.fn(), unselect: vi.fn(), toggle: vi.fn(), selectAll: vi.fn(), clear: vi.fn(), isSelected: vi.fn() },
    formModal: { visible: ref(false), mode: ref<'create' | 'edit' | 'view' | null>(null), formData: ref(null), open: vi.fn(), close: vi.fn(), confirm: vi.fn() },
    rowKey: (r: { id: number }) => r.id,
    canCreate: true, canUpdate: true, canDelete: true,
    refresh: vi.fn(), setPage: vi.fn(), setPageSize: vi.fn(), setSearch: vi.fn(), setSort: vi.fn(), setFilters: vi.fn(), resetQuery: vi.fn(),
    openCreate: vi.fn(), openEdit: vi.fn(), openView: vi.fn(), submit: vi.fn(), handleDelete: vi.fn(), exportAll: vi.fn(), importFile: vi.fn(), dismissError: vi.fn(),
    ...overrides,
  }
}

describe('TListShell', () => {
  afterEach(() => { delete (globalThis as Record<string, unknown>).__bpNarrow })

  it('renders the #renderer slot body', () => {
    const wrapper = mount(TListShell, {
      props: { state: makeState() as any },
      slots: { renderer: '<div class="body-marker">B</div>' },
      global: { stubs },
    })
    expect(wrapper.find('.body-marker').exists()).toBe(true)
  })

  it('shows the create button when canCreate and showCreate, hides when canCreate=false', () => {
    const shown = mount(TListShell, { props: { state: makeState() as any }, slots: { renderer: '<div/>' }, global: { stubs } })
    expect(shown.find('.t-list-shell__create').exists()).toBe(true)
    const hidden = mount(TListShell, { props: { state: makeState({ canCreate: false }) as any }, slots: { renderer: '<div/>' }, global: { stubs } })
    expect(hidden.find('.t-list-shell__create').exists()).toBe(false)
  })

  it('does not mount the form modal when neither canCreate nor canUpdate', () => {
    const state = makeState({ canCreate: false, canUpdate: false })
    state.formModal.visible.value = true
    state.formModal.mode.value = 'view'
    const wrapper = mount(TListShell, { props: { state: state as any }, slots: { renderer: '<div/>', form: '<div class="form-body" />' }, global: { stubs } })
    expect(wrapper.find('.form-body').exists()).toBe(false)
  })

  it('hides pagination when showPagination=false', () => {
    const wrapper = mount(TListShell, { props: { state: makeState() as any, showPagination: false }, slots: { renderer: '<div/>' }, global: { stubs } })
    expect(wrapper.find('.n-pagination-stub').exists()).toBe(false)
  })

  it('applies container modifier class in container mode', () => {
    const wrapper = mount(TListShell, { props: { state: makeState() as any, mode: 'container' }, slots: { renderer: '<div/>' }, global: { stubs } })
    expect(wrapper.find('.t-list-shell--container').exists()).toBe(true)
  })

  it('refresh button calls state.refresh()', async () => {
    const state = makeState()
    const wrapper = mount(TListShell, { props: { state: state as any }, slots: { renderer: '<div/>' }, global: { stubs } })
    await wrapper.find('.t-list-shell__refresh').trigger('click')
    expect(state.refresh).toHaveBeenCalled()
  })

  it('renders the error banner with the error message when state.error is set', () => {
    const state = makeState({ error: ref(new Error('boom')) })
    const wrapper = mount(TListShell, { props: { state: state as any }, slots: { renderer: '<div/>' }, global: { stubs } })
    expect(wrapper.text()).toContain('boom')
  })

  it('renders the #kpis slot between the header card and the list card', () => {
    const wrapper = mount(TListShell, {
      props: { state: makeState() as any, title: 'Roles' },
      slots: { renderer: '<div/>', kpis: '<div class="kpi-marker">K</div>' },
      global: { stubs },
    })
    expect(wrapper.find('.t-list-shell__kpis .kpi-marker').exists()).toBe(true)
    const html = wrapper.html()
    expect(html.indexOf('t-list-shell__header-card')).toBeLessThan(html.indexOf('t-list-shell__kpis'))
    expect(html.indexOf('t-list-shell__kpis')).toBeLessThan(html.indexOf('t-list-shell__list-card'))
  })

  it('omits the kpis wrapper when no #kpis slot is provided', () => {
    const wrapper = mount(TListShell, { props: { state: makeState() as any }, slots: { renderer: '<div/>' }, global: { stubs } })
    expect(wrapper.find('.t-list-shell__kpis').exists()).toBe(false)
  })

  it('renders the page title in a TPageHeader above the list', () => {
    const wrapper = mount(TListShell, { props: { state: makeState() as any, title: 'Roles' }, slots: { renderer: '<div/>' }, global: { stubs } })
    expect(wrapper.find('.t-page-header__title').text()).toBe('Roles')
  })

  it('show-header=false drops the whole white header card (no TPageHeader, no search)', () => {
    const wrapper = mount(TListShell, {
      props: { state: makeState() as any, title: 'Roles', showHeader: false },
      slots: { renderer: '<div/>' },
      global: { stubs },
    })
    expect(wrapper.find('.t-list-shell__header-card').exists()).toBe(false)
    expect(wrapper.find('.t-page-header__title').exists()).toBe(false)
    expect(wrapper.find('.t-list-shell__search').exists()).toBe(false)
    // The list card (toolbar/body/footer) keeps rendering.
    expect(wrapper.find('.t-list-shell__list-card').exists()).toBe(true)
  })

  it('show-header=false suppresses a consumer #header slot too', () => {
    const wrapper = mount(TListShell, {
      props: { state: makeState() as any, showHeader: false },
      slots: { renderer: '<div/>', header: '<div class="custom-header">H</div>' },
      global: { stubs },
    })
    expect(wrapper.find('.custom-header').exists()).toBe(false)
  })

  it('renders the header card by default (showHeader defaults to true)', () => {
    const wrapper = mount(TListShell, { props: { state: makeState() as any, title: 'Roles' }, slots: { renderer: '<div/>' }, global: { stubs } })
    expect(wrapper.find('.t-list-shell__header-card').exists()).toBe(true)
  })

  it('still renders the create button (now inside the header actions)', () => {
    const wrapper = mount(TListShell, { props: { state: makeState() as any }, slots: { renderer: '<div/>' }, global: { stubs } })
    expect(wrapper.find('.t-list-shell__create').exists()).toBe(true)
  })

  it('shows the keyword search input at desktop width', () => {
    ;(globalThis as any).__bpNarrow = false
    const wrapper = mount(TListShell, { props: { state: makeState() as any }, slots: { renderer: '<div/>' }, global: { stubs } })
    expect(wrapper.find('.t-list-shell__search-input').exists()).toBe(true)
    expect(wrapper.find('.t-list-shell__search-icon').exists()).toBe(false)
  })

  it('collapses the keyword search to an icon below md', () => {
    ;(globalThis as any).__bpNarrow = true
    const wrapper = mount(TListShell, { props: { state: makeState() as any }, slots: { renderer: '<div/>' }, global: { stubs } })
    expect(wrapper.find('.t-list-shell__search-icon').exists()).toBe(true)
    expect(wrapper.find('.t-list-shell__search-input').exists()).toBe(false)
    ;(globalThis as any).__bpNarrow = false
  })

  it('opens the advanced drawer (no inline advanced panel) when searchFields are given', async () => {
    const drawerStubs = { ...stubs, Drawer: { name: 'Drawer', props: ['show'], template: '<div v-if="show" class="n-drawer-stub"><slot/></div>' }, DrawerContent: { name: 'DrawerContent', template: '<div><slot/><slot name="footer"/></div>' } }
    const wrapper = mount(TListShell, {
      props: { state: makeState() as any, searchFields: [{ key: 'name', label: 'Name', type: 'text' }] as any },
      slots: { renderer: '<div/>' },
      global: { stubs: drawerStubs },
    })
    expect(wrapper.find('.t-list-shell__advanced').exists()).toBe(false)
    await wrapper.find('.t-list-shell__adv-toggle').trigger('click')
    expect(wrapper.find('.n-drawer-stub').exists()).toBe(true)
  })

  // NOTE: the search toggles carry dynamic `:type`/`:tertiary` bindings (the
  // active-state highlight). @vue/test-utils' native `trigger('click')` does
  // NOT propagate a custom-stub's `$emit('click')` to the parent listener when
  // the stubbed component has dynamic props AND lives in a slot - so we drive
  // the wiring via the component's own `click` emit (what NButton fires on a
  // real tap). Browser-verified separately.
  it('phone: tapping the search toggle expands the keyword panel downward, tapping again collapses it', async () => {
    ;(globalThis as any).__bpNarrow = true
    const wrapper = mount(TListShell, { props: { state: makeState() as any }, slots: { renderer: '<div/>' }, global: { stubs } })
    // Collapsed by default - only the toggle icon, no panel.
    expect(wrapper.find('.t-list-shell__mobile-search').exists()).toBe(false)
    expect(wrapper.find('.t-list-shell__search-input').exists()).toBe(false)
    wrapper.findComponent('.t-list-shell__search-icon').vm.$emit('click')
    await wrapper.vm.$nextTick()
    expect(wrapper.find('.t-list-shell__mobile-search').exists()).toBe(true)
    expect(wrapper.find('.t-list-shell__mobile-field').exists()).toBe(true)
    wrapper.findComponent('.t-list-shell__search-icon').vm.$emit('click')
    await wrapper.vm.$nextTick()
    expect(wrapper.find('.t-list-shell__mobile-search').exists()).toBe(false)
    ;(globalThis as any).__bpNarrow = false
  })

  it('phone: Advanced expands the inline one-column form, not the right drawer', async () => {
    ;(globalThis as any).__bpNarrow = true
    const drawerStubs = { ...stubs, Drawer: { name: 'Drawer', props: ['show'], template: '<div v-if="show" class="n-drawer-stub"><slot/></div>' }, DrawerContent: { name: 'DrawerContent', template: '<div><slot/><slot name="footer"/></div>' } }
    const wrapper = mount(TListShell, {
      props: { state: makeState() as any, searchFields: [{ key: 'name', label: 'Name', type: 'text' }] as any },
      slots: { renderer: '<div/>' },
      global: { stubs: drawerStubs },
    })
    wrapper.findComponent('.t-list-shell__adv-toggle').vm.$emit('click')
    await wrapper.vm.$nextTick()
    expect(wrapper.find('.t-list-shell__mobile-search').exists()).toBe(true)
    expect(wrapper.find('.t-crud-search-advanced').exists()).toBe(true)
    // The right drawer is never mounted on phones.
    expect(wrapper.find('.n-drawer-stub').exists()).toBe(false)
    ;(globalThis as any).__bpNarrow = false
  })
})

// ── Toolbar: icon-only buttons + fixed-trailing order ─────────────────────
// Enhanced stubs that expose aria-label and both default + icon named slots,
// so we can assert "no text content" and "has aria-label".
const trailingStubs = {
  ...stubs,
  // Override Button stub to:
  //   • carry aria-label on the root <button>
  //   • render both #icon slot AND #default slot
  //   • suppress NTooltip wrapper (we test for it separately)
  Button: {
    name: 'Button',
    props: ['ariaLabel'],
    inheritAttrs: false,
    template: `<button
      class="n-button"
      v-bind="$attrs"
      :aria-label="ariaLabel"
      @click="$emit('click')"
    ><slot name="icon" /><slot /></button>`,
  },
  // NTooltip: render its #trigger slot so descendants are visible;
  // wrap in a sentinel class so we can assert it's present.
  Tooltip: {
    name: 'Tooltip',
    template: '<span class="n-tooltip-stub"><slot name="trigger" /></span>',
  },
}

describe('TListShell toolbar - icon-only buttons + trailing order', () => {
  afterEach(() => { delete (globalThis as Record<string, unknown>).__bpNarrow })

  // (a) Refresh renders as icon-only: no text node in its default slot,
  //     aria-label is set, button is wrapped inside NTooltip.
  it('renders Refresh as icon-only button with aria-label and no text', () => {
    const wrapper = mount(TListShell, {
      props: { state: makeState() as any, showRefresh: true },
      slots: { renderer: '<div/>' },
      global: { stubs: trailingStubs },
    })
    const refreshBtn = wrapper.find('.t-list-shell__refresh')
    expect(refreshBtn.exists()).toBe(true)
    // aria-label must be set (non-empty)
    expect(refreshBtn.attributes('aria-label')).toBeTruthy()
    // The default slot of the Refresh button must be empty (icon-only)
    // We detect text by checking that there are no text nodes directly in the
    // button's rendered output - only the icon slot element.
    const directText = refreshBtn.text().trim()
    expect(directText).toBe('')
    // The Refresh button must be inside an NTooltip wrapper
    expect(wrapper.find('.n-tooltip-stub').exists()).toBe(true)
  })

  // (b) Columns (injected via #toolbar slot from TCrudPage) must render
  //     as icon-only: no text, aria-label set. We simulate what TCrudPage injects.
  it('renders Columns (#toolbar slot) as icon-only button with aria-label', () => {
    const wrapper = mount(TListShell, {
      props: { state: makeState() as any },
      slots: {
        renderer: '<div/>',
        // Simulate what TCrudPage injects: icon-only button with aria-label
        toolbar: '<button class="t-crud-toolbar__columns columns-btn" aria-label="Columns"><span class="icon-stub" /></button>',
      },
      global: { stubs: trailingStubs },
    })
    const colBtn = wrapper.find('.columns-btn')
    expect(colBtn.exists()).toBe(true)
    expect(colBtn.attributes('aria-label')).toBe('Columns')
    // Text content should be empty (no label text)
    expect(colBtn.text().trim()).toBe('')
  })

  // (c) DOM order: #toolbarRight content must appear BEFORE Refresh and Columns
  it('places Refresh and Columns (#toolbar) after #toolbarRight in the DOM', () => {
    const wrapper = mount(TListShell, {
      props: { state: makeState() as any, showRefresh: true },
      slots: {
        renderer: '<div/>',
        toolbarRight: '<button class="custom-right-btn">Custom</button>',
        toolbar: '<button class="columns-btn" aria-label="Columns"><span /></button>',
      },
      global: { stubs: trailingStubs },
    })
    const actionsEl = wrapper.find('.t-list-shell__actions')
    expect(actionsEl.exists()).toBe(true)
    const html = actionsEl.html()

    const customIdx = html.indexOf('custom-right-btn')
    const refreshIdx = html.indexOf('t-list-shell__refresh')
    const columnsIdx = html.indexOf('columns-btn')

    expect(customIdx).toBeGreaterThanOrEqual(0)
    expect(refreshIdx).toBeGreaterThanOrEqual(0)
    expect(columnsIdx).toBeGreaterThanOrEqual(0)
    // Custom (#toolbarRight) before Refresh
    expect(customIdx).toBeLessThan(refreshIdx)
    // Custom (#toolbarRight) before Columns
    expect(customIdx).toBeLessThan(columnsIdx)
    // Refresh before Columns (Columns is rightmost)
    expect(refreshIdx).toBeLessThan(columnsIdx)
  })

  // (d) showRefresh=false → Refresh button absent
  it('omits the Refresh button when showRefresh=false', () => {
    const wrapper = mount(TListShell, {
      props: { state: makeState() as any, showRefresh: false },
      slots: { renderer: '<div/>' },
      global: { stubs: trailingStubs },
    })
    expect(wrapper.find('.t-list-shell__refresh').exists()).toBe(false)
  })

  // (e) No #toolbar slot → no Columns button rendered (the shell itself never
  //     renders a Columns button - that is TCrudPage's responsibility via the slot)
  it('does not render a Columns button when no #toolbar slot is provided', () => {
    const wrapper = mount(TListShell, {
      props: { state: makeState() as any, showRefresh: true },
      slots: { renderer: '<div/>' },
      global: { stubs: trailingStubs },
    })
    expect(wrapper.find('.t-crud-toolbar__columns').exists()).toBe(false)
  })

  // Export: `useCrudPage.exportAll` runs the bridge through runWithErrorHandling,
  // which toasts and RE-THROWS. The shell must neither download around a
  // rejected export nor let the rejection escape the click handler (nothing else
  // is listening for it), and must download when a Blob comes back.
  describe('export', () => {
    function exportButton(wrapper: ReturnType<typeof mount>) {
      const button = wrapper.findAll('button').find((b) => b.text().toLowerCase().includes('export'))
      expect(button, 'export button').toBeDefined()
      return button!
    }

    it('downloads the Blob exportAll resolves', async () => {
      downloadBlob.mockClear()
      const blob = new Blob(['id\n1'], { type: 'text/csv' })
      const state = makeState({ exportAll: vi.fn(async () => blob) })
      const wrapper = mount(TListShell, { props: { state: state as any, showExport: true, title: 'Users' }, slots: { renderer: '<div/>' }, global: { stubs } })
      await exportButton(wrapper).trigger('click')
      await new Promise((r) => setTimeout(r, 0))
      expect(downloadBlob).toHaveBeenCalledWith(blob, 'Users.csv')
    })

    // Import goes through the same runWithErrorHandling (toast + re-throw);
    // the picker's change handler fired it with `void`, so a rejected CSV
    // import surfaced a second time as an unhandled rejection.
    it('a rejected import settles the picker handler instead of escaping it', async () => {
      const state = makeState({ importFile: vi.fn(async () => { throw new Error('Column "email" is required') }) })
      const wrapper = mount(TListShell, {
        props: { state: state as any, showImport: true },
        slots: { renderer: '<div/>' },
        global: { stubs },
      })
      const file = new File(['a,b'], 'rows.csv', { type: 'text/csv' })
      const vm = wrapper.vm as unknown as { importPicked: (f: File) => Promise<void> }
      await expect(vm.importPicked(file)).resolves.toBeUndefined()
      expect(state.importFile).toHaveBeenCalledWith(file)
    })

    it('a rejected export downloads nothing and does not escape the click handler', async () => {
      downloadBlob.mockClear()
      const state = makeState({ exportAll: vi.fn(async () => { throw new Error('12000 rows matched; narrow the filter') }) })
      const errorHandler = vi.fn()
      const wrapper = mount(TListShell, {
        props: { state: state as any, showExport: true },
        slots: { renderer: '<div/>' },
        global: { stubs, config: { errorHandler } },
      })
      await exportButton(wrapper).trigger('click')
      await new Promise((r) => setTimeout(r, 0))
      expect(downloadBlob).not.toHaveBeenCalled()
      expect(errorHandler).not.toHaveBeenCalled()
    })
  })
})

// ── Header off (list embedded in a tab): search lives in the toolbar ───────
// The standard for multi-tab pages turns the list's own header off, so the
// search entry points move into the list card. On phones they must be the same
// icon toggles + downward panel the header path gives, because the side drawer
// is desktop-only: without them a phone has no way to open the filters.
describe('TListShell - header off (embedded in a tab)', () => {
  afterEach(() => { delete (globalThis as Record<string, unknown>).__bpNarrow })

  const drawerStubs = {
    ...stubs,
    Drawer: { name: 'Drawer', props: ['show'], template: '<div v-if="show" class="n-drawer-stub"><slot/></div>' },
    DrawerContent: { name: 'DrawerContent', template: '<div><slot/><slot name="footer"/></div>' },
  }
  const fields = [{ key: 'name', label: 'Name', type: 'text' }] as any

  function mountEmbedded() {
    return mount(TListShell, {
      props: { state: makeState() as any, showHeader: false, searchFields: fields },
      slots: { renderer: '<div class="body-marker" />' },
      global: { stubs: drawerStubs },
    })
  }

  it('phone: shows the icon toggles in the toolbar instead of the inline keyword row', () => {
    ;(globalThis as any).__bpNarrow = true
    const wrapper = mountEmbedded()
    const toolbar = wrapper.find('.t-list-shell__toolbar')
    expect(toolbar.find('.t-list-shell__search-icon').exists()).toBe(true)
    expect(toolbar.find('.t-list-shell__adv-toggle').exists()).toBe(true)
    // The desktop row (keyword input + Search + Advanced) would overflow 375px.
    expect(wrapper.find('.t-list-shell__inline-keyword').exists()).toBe(false)
  })

  it('phone: Advanced expands the one-column form inside the list card, never the drawer', async () => {
    ;(globalThis as any).__bpNarrow = true
    const wrapper = mountEmbedded()
    wrapper.findComponent('.t-list-shell__adv-toggle').vm.$emit('click')
    await wrapper.vm.$nextTick()
    const panel = wrapper.find('.t-list-shell__list-card .t-list-shell__mobile-search')
    expect(panel.exists()).toBe(true)
    expect(panel.classes()).toContain('t-list-shell__mobile-search--toolbar')
    expect(panel.find('.t-crud-search-advanced').exists()).toBe(true)
    expect(wrapper.find('.n-drawer-stub').exists()).toBe(false)
    // Panel sits between the toolbar and the list body.
    const html = wrapper.html()
    expect(html.indexOf('t-list-shell__toolbar')).toBeLessThan(html.indexOf('t-list-shell__mobile-search'))
    expect(html.indexOf('t-list-shell__mobile-search')).toBeLessThan(html.indexOf('body-marker'))
  })

  it('phone: the search toggle expands the keyword panel and collapses it again', async () => {
    ;(globalThis as any).__bpNarrow = true
    const wrapper = mountEmbedded()
    wrapper.findComponent('.t-list-shell__search-icon').vm.$emit('click')
    await wrapper.vm.$nextTick()
    expect(wrapper.find('.t-list-shell__mobile-field').exists()).toBe(true)
    wrapper.findComponent('.t-list-shell__search-icon').vm.$emit('click')
    await wrapper.vm.$nextTick()
    expect(wrapper.find('.t-list-shell__mobile-search').exists()).toBe(false)
  })

  it('desktop: keeps the inline keyword row and the right drawer', async () => {
    ;(globalThis as any).__bpNarrow = false
    const wrapper = mountEmbedded()
    expect(wrapper.find('.t-list-shell__inline-keyword').exists()).toBe(true)
    expect(wrapper.find('.t-list-shell__search-icon').exists()).toBe(false)
    const adv = wrapper.findAll('button').find((b) => b.text().includes('admin.crud.advancedSearch'))
    expect(adv, 'toolbar Advanced button').toBeDefined()
    await adv!.trigger('click')
    expect(wrapper.find('.n-drawer-stub').exists()).toBe(true)
    expect(wrapper.find('.t-list-shell__mobile-search').exists()).toBe(false)
  })

  it('header on: the phone panel stays in the header card, not the list card', async () => {
    ;(globalThis as any).__bpNarrow = true
    const wrapper = mount(TListShell, {
      props: { state: makeState() as any, searchFields: fields },
      slots: { renderer: '<div/>' },
      global: { stubs: drawerStubs },
    })
    wrapper.findComponent('.t-list-shell__adv-toggle').vm.$emit('click')
    await wrapper.vm.$nextTick()
    expect(wrapper.find('.t-list-shell__header-card .t-list-shell__mobile-search').exists()).toBe(true)
    expect(wrapper.find('.t-list-shell__list-card .t-list-shell__mobile-search').exists()).toBe(false)
    expect(wrapper.find('.t-list-shell__toolbar .t-list-shell__search-icon').exists()).toBe(false)
  })
})

// Every `admin.crud.*` key the shell renders must exist in both bundled
// dictionaries: a miss renders the humanised key, which in zh-cn reads as an
// untranslated English word (the toolbar once rendered "Advanced" this way).
describe('TListShell - translation keys', () => {
  it('uses only admin.crud keys present in en and zh-cn', async () => {
    const fs = await import('node:fs')
    const path = await import('node:path')
    const { en } = await import('../../../src/locales/en')
    const { zhCn } = await import('../../../src/locales/zh-cn')
    const src = fs.readFileSync(path.resolve(__dirname, '../../../src/components/crud/TListShell.vue'), 'utf8')
    const keys = [...new Set([...src.matchAll(/t\('admin\.crud\.([\w.]+)'\)/g)].map((m) => m[1]))]
    expect(keys.length).toBeGreaterThan(5)
    const lookup = (dict: Record<string, unknown>, key: string) =>
      key.split('.').reduce<unknown>((node, part) => (node as Record<string, unknown> | undefined)?.[part], dict)
    for (const key of keys) {
      expect(typeof lookup(en.admin.crud as Record<string, unknown>, key), `en admin.crud.${key}`).toBe('string')
      expect(typeof lookup(zhCn.admin.crud as Record<string, unknown>, key), `zh-cn admin.crud.${key}`).toBe('string')
    }
  })
})
