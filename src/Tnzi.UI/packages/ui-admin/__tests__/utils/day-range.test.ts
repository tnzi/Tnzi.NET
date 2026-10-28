import { describe, it, expect, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { NDatePicker } from 'naive-ui'
import { dayRangeBounds } from '../../src/utils/day-range'
import { defaultUsageRange, toRangeQuery } from '../../src/pages/system/features/feature-usage-config'

vi.mock('../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))
vi.mock('../../src/services/bridges/identity-bridge', () => ({
  createIdentityBridge: () => ({
    users: { fetch: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20 })) },
  }),
}))

import TAuditTimeline from '../../src/pages/audit/components/TAuditTimeline.vue'

/** What NDatePicker `type="daterange"` emits for a pick: local midnight of each day. */
function pickedDays(from: [number, number, number], to: [number, number, number]): [number, number] {
  return [new Date(from[0], from[1] - 1, from[2]).getTime(), new Date(to[0], to[1] - 1, to[2]).getTime()]
}

const endOfDay = (y: number, m: number, d: number) => new Date(y, m - 1, d, 23, 59, 59, 999).toISOString()
const startOfDay = (y: number, m: number, d: number) => new Date(y, m - 1, d).toISOString()

describe('dayRangeBounds', () => {
  it('covers the whole of a single picked day', () => {
    expect(dayRangeBounds(pickedDays([2026, 10, 10], [2026, 10, 10]))).toEqual({
      start: startOfDay(2026, 10, 10),
      end: endOfDay(2026, 10, 10),
    })
  })

  it('keeps the last day of a longer range', () => {
    expect(dayRangeBounds(pickedDays([2026, 10, 1], [2026, 10, 10])).end).toBe(endOfDay(2026, 10, 10))
  })

  it('leaves a range that already ends at the end of its day unchanged', () => {
    const [start, end] = defaultUsageRange(new Date(2026, 9, 10, 15, 30))
    expect(dayRangeBounds([start, end])).toEqual({
      start: new Date(start).toISOString(),
      end: new Date(end).toISOString(),
    })
  })
})

describe('feature usage range query', () => {
  it('a range the operator picks keeps its last day', () => {
    expect(toRangeQuery(pickedDays([2026, 10, 10], [2026, 10, 10]))).toEqual({
      from: startOfDay(2026, 10, 10),
      to: endOfDay(2026, 10, 10),
    })
  })
})

describe('audit timeline date filter', () => {
  it('sends the end of the last picked day, to the list and to the export alike', async () => {
    setActivePinia(createPinia())
    const fetch = vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 30, totalPages: 0, hasPreviousPage: false, hasNextPage: false }))
    const exportCsv = vi.fn(async () => new Blob(['x']))
    const wrapper = mount(TAuditTimeline, {
      props: { pageId: 'audit.operations', fetch, exportCsv, translate: (k: string) => k },
    })
    await flushPromises()

    wrapper.findComponent(NDatePicker).vm.$emit('update:value', pickedDays([2026, 10, 10], [2026, 10, 10]))
    await flushPromises()
    fetch.mockClear()
    const vm = wrapper.vm as unknown as { loadFirst: () => Promise<void>; onExportSelect: (k: string) => Promise<void> }
    await vm.loadFirst()

    const filters = (fetch.mock.calls[0] as unknown as [{ filters: Record<string, unknown> }])[0].filters
    expect(filters.startDate).toBe(startOfDay(2026, 10, 10))
    expect(filters.endDate).toBe(endOfDay(2026, 10, 10))

    // jsdom has no object URLs; the download itself is not under test.
    const createObjectURL = vi.fn(() => 'blob:x')
    Object.assign(URL, { createObjectURL, revokeObjectURL: vi.fn() })
    await vm.onExportSelect('csv')
    expect(exportCsv).toHaveBeenCalledWith(expect.objectContaining({ endDate: endOfDay(2026, 10, 10) }))
  })
})
