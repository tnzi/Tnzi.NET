/**
 * The presentation primitives introduced for the 2026-07 page-shape overhaul:
 *
 *   TItemCard - one record as a document row (title / tags / meta / figure)
 *   TRecordHeader - the identity band at the top of a detail surface
 *   TItemRenderer - the row-list renderer behind TItemPage
 *
 * These exist so a page stops being "a table of every column" - the tests
 * below pin the behaviour that makes them safe to use in place of a table row:
 * the click target, the selection affordance, and the fact that a missing
 * value renders the shared placeholder rather than a blank.
 */
import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { ref } from 'vue'
import TItemCard from '../../src/components/data/TItemCard.vue'
import TRecordHeader from '../../src/components/detail/TRecordHeader.vue'
import TItemRenderer from '../../src/components/crud/renderers/TItemRenderer.vue'
import TTableRenderer from '../../src/components/crud/renderers/TTableRenderer.vue'

describe('TItemCard', () => {
  it('renders title, tags, meta and figure', () => {
    const wrapper = mount(TItemCard, {
      props: {
        title: 'INV-1042',
        subtitle: 'Quarterly retainer',
        tags: [{ label: 'Posted', type: 'success' as const }],
        meta: [{ icon: 'mdi:calendar-outline', text: '2026-07-01' }],
        amount: '$1,250.00',
        amountLabel: 'balance due',
      },
    })
    expect(wrapper.find('.t-item-card__title').text()).toBe('INV-1042')
    expect(wrapper.text()).toContain('Quarterly retainer')
    expect(wrapper.text()).toContain('Posted')
    expect(wrapper.text()).toContain('2026-07-01')
    expect(wrapper.find('.t-item-card__amount-value').text()).toBe('$1,250.00')
    expect(wrapper.text()).toContain('balance due')
  })

  it('is inert unless `clickable` - no button role, no click event', async () => {
    const wrapper = mount(TItemCard, { props: { title: 'Read only' } })
    expect(wrapper.attributes('role')).toBeUndefined()
    expect(wrapper.attributes('tabindex')).toBeUndefined()
    await wrapper.trigger('click')
    expect(wrapper.emitted('click')).toBeUndefined()
  })

  it('clickable cards activate by mouse AND keyboard', async () => {
    const wrapper = mount(TItemCard, { props: { title: 'Open me', clickable: true } })
    expect(wrapper.attributes('role')).toBe('button')
    expect(wrapper.attributes('tabindex')).toBe('0')
    await wrapper.trigger('click')
    await wrapper.trigger('keydown', { key: 'Enter' })
    await wrapper.trigger('keydown', { key: ' ' })
    expect(wrapper.emitted('click')).toHaveLength(3)
    // An unrelated key must not activate the card.
    await wrapper.trigger('keydown', { key: 'a' })
    expect(wrapper.emitted('click')).toHaveLength(3)
  })

  it('marks retired records without hiding them', () => {
    const wrapper = mount(TItemCard, { props: { title: 'Voided', muted: true, amount: '$0.00' } })
    expect(wrapper.classes()).toContain('t-item-card--muted')
    expect(wrapper.text()).toContain('Voided')
  })
})

describe('TRecordHeader', () => {
  it('renders name, badges and identifying facts', () => {
    const wrapper = mount(TRecordHeader, {
      props: {
        name: 'Acme Industries',
        subtitle: 'Customer',
        badges: [{ label: 'Active', type: 'success' as const }],
        facts: [
          { label: 'Code', value: 'CUST-001' },
          { label: 'Balance', value: '$4,200.00' },
        ],
      },
    })
    expect(wrapper.find('.t-record-header__title').text()).toBe('Acme Industries')
    expect(wrapper.text()).toContain('Customer')
    expect(wrapper.text()).toContain('Active')
    expect(wrapper.text()).toContain('CUST-001')
  })

  it('renders the shared placeholder for a fact with no value', () => {
    const wrapper = mount(TRecordHeader, {
      props: { name: 'Unnamed', facts: [{ label: 'Owner' }] },
    })
    // A blank cell is indistinguishable from a rendering bug; the dash says
    // "we looked and there is nothing".
    expect(wrapper.find('.t-record-header__fact-value').text()).toBe('-')
  })
})

describe('TItemRenderer', () => {
  /** Minimal `useCrudPage`-shaped stub - the renderer only reads these. */
  function makeState(items: Array<{ id: string; name: string }>, loading = false) {
    const selected = ref<string[]>([])
    return {
      items: ref(items),
      loading: ref(loading),
      rowKey: (row: { id: string }) => row.id,
      canCreate: false,
      searchState: { keyword: ref(''), filters: ref({}) },
      batchActions: {
        isSelected: (id: string) => selected.value.includes(id),
        toggle: (id: string) => {
          selected.value = selected.value.includes(id)
            ? selected.value.filter((x) => x !== id)
            : [...selected.value, id]
        },
      },
    } as never
  }

  it('renders one slot instance per record', () => {
    const wrapper = mount(TItemRenderer, {
      props: { state: makeState([{ id: '1', name: 'A' }, { id: '2', name: 'B' }]) },
      slots: { item: '<div class="row">{{ params.item.name }}</div>' },
    })
    expect(wrapper.findAll('.row')).toHaveLength(2)
  })

  it('shows skeletons on first load and an empty state when there is nothing', () => {
    const loading = mount(TItemRenderer, { props: { state: makeState([], true) } })
    expect(loading.findAll('.t-item-renderer__skeleton').length).toBeGreaterThan(0)

    const empty = mount(TItemRenderer, { props: { state: makeState([]) } })
    expect(empty.find('.t-item-renderer__empty').exists()).toBe(true)
    expect(empty.findAll('.t-item-renderer__skeleton')).toHaveLength(0)
  })

  it('exposes selection state to the row slot', async () => {
    const state = makeState([{ id: '1', name: 'A' }])
    const wrapper = mount(TItemRenderer, {
      props: { state, showSelection: true },
      slots: {
        item: `<button class="row" :data-selected="params.selected" @click="params.toggleSelect()">x</button>`,
      },
    })
    expect(wrapper.find('.row').attributes('data-selected')).toBe('false')
    await wrapper.find('.row').trigger('click')
    expect(wrapper.find('.row').attributes('data-selected')).toBe('true')
  })
})

/**
 * A table row that is itself a drill-in target has the operation buttons and the
 * selection checkbox INSIDE its click area. Both regressions below shipped once
 * and were only caught in a browser, so they are pinned here:
 *
 *  - `rowProps` must actually reach the table (the wrapper had the prop declared
 *    but never bound it, so whole-row drill-in silently did nothing).
 *  - the operation + selection cells must swallow their own clicks, or pressing
 *    an action ALSO navigates and the button reads as broken.
 */
describe('TTableRenderer row activation guard', () => {
  /** Captures the column definitions handed to NDataTable. */
  let captured: Record<string, unknown>[] = []
  const stubs = {
    DataTable: {
      props: ['columns', 'rowProps'],
      template: '<div class="dt" />',
      created(this: { columns: Record<string, unknown>[]; rowProps?: unknown }) {
        captured = this.columns
        capturedRowProps = this.rowProps
      },
    },
  }
  let capturedRowProps: unknown

  function makeState(items: Array<{ id: string; name: string }>) {
    return {
      items: ref(items),
      loading: ref(false),
      rowKey: (row: { id: string }) => row.id,
      canCreate: false,
      query: ref({ pageIndex: 1, pageSize: 20 }),
      searchState: { keyword: ref(''), filters: ref({}) },
      columnSettings: { visibleColumns: ref([{ key: 'name', title: 'Name' }]) },
      batchActions: {
        selectedIds: ref([]),
        isSelected: () => false,
        toggle: () => {},
        selectAll: () => {},
      },
    } as never
  }

  const rows = [{ id: '1', name: 'A' }]

  it('forwards rowProps to the table so a whole-row drill-in works', async () => {
    const onClick = vi.fn()
    mount(TTableRenderer, {
      props: {
        state: makeState(rows),
        rowProps: () => ({ style: 'cursor: pointer;', onClick }),
      },
      global: { stubs },
    })
    expect(typeof capturedRowProps).toBe('function')
    const attrs = (capturedRowProps as (row: unknown) => Record<string, unknown>)(rows[0])
    expect(attrs.style).toBe('cursor: pointer;')
    expect(attrs.onClick).toBe(onClick)
  })

  it('makes the operation and selection cells swallow clicks when rows are clickable', () => {
    mount(TTableRenderer, {
      props: {
        state: makeState(rows),
        showSelection: true,
        rowActions: [{ key: 'post', label: 'Post', onClick: () => {} }],
        rowProps: () => ({ onClick: () => {} }),
      },
      global: { stubs },
    })
    const selection = captured.find((c) => c.type === 'selection')
    const actions = captured.find((c) => c.key === '__row_actions__')
    for (const col of [selection, actions]) {
      const cellProps = col?.cellProps as ((row: unknown, i: number) => Record<string, unknown>) | undefined
      expect(cellProps).toBeTypeOf('function')
      const attrs = cellProps!(rows[0], 0)
      expect(attrs.onClick).toBeTypeOf('function')
      // The guard stops propagation rather than swallowing the event outright,
      // so the button underneath still fires.
      const stopPropagation = vi.fn()
      ;(attrs.onClick as (e: unknown) => void)({ stopPropagation })
      expect(stopPropagation).toHaveBeenCalled()
    }
  })

  it('leaves cells untouched on a table with no row-level handler', () => {
    mount(TTableRenderer, {
      props: {
        state: makeState(rows),
        showSelection: true,
        rowActions: [{ key: 'post', label: 'Post', onClick: () => {} }],
      },
      global: { stubs },
    })
    expect(capturedRowProps).toBeUndefined()
    const actions = captured.find((c) => c.key === '__row_actions__')
    // No drill-in → nothing to guard against, so naive keeps its own behaviour.
    expect(actions?.cellProps).toBeUndefined()
  })
})
