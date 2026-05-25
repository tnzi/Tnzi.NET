import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import AuditOperation from '../../src/pages/audit/AuditOperation.vue'

vi.mock('../../src/plugin/client', () => ({ useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }) }))
vi.mock('../../src/services/bridges/audit-bridge', () => ({
  createAuditBridge: () => ({
    logs: {
      fetch: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20 })),
    },
    operations: {
      fetch: vi.fn(async () => ({
        items: [
          { id: 'op1', functionName: 'GetUser', userId: 'u1', userName: 'admin', elapsed: 42, startTime: '2026-01-01T00:00:00Z', resultType: 1, entityEntries: [], creationTime: '2026-01-01T00:00:00Z' },
          { id: 'op2', functionName: 'CreateRole', userId: 'u2', userName: 'editor', elapsed: 155, startTime: '2026-01-01T00:01:00Z', resultType: 1, entityEntries: [], creationTime: '2026-01-01T00:01:00Z' },
        ],
        totalCount: 2,
        pageIndex: 1,
        pageSize: 20,
      })),
    },
  }),
  // 0.2.72+ (B4): the bridge now re-exports `AuditResultType` so pages
  // can read the enum value without reaching into
  // `@tnzi/core/services/audit`. Mirror that surface in the mock.
  AuditResultType: {
    Success: 1,
    Failed: 2,
    Warning: 3,
  },
}))

describe('AuditOperation page (Tier 2: timeline view)', () => {
  beforeEach(() => { setActivePinia(createPinia()) })

  it('mounts the timeline view and fetches operation entries on load', async () => {
    const wrapper = mount(AuditOperation)
    await nextTick()
    await new Promise(r => setTimeout(r, 50))
    await nextTick()
    expect(wrapper.find('.t-audit-timeline').exists()).toBe(true)
    const items = wrapper.findAll('.n-timeline-item')
    expect(items.length).toBeGreaterThanOrEqual(2)
  })

  it('does not show TCrudPage chrome (timeline view replaces it)', async () => {
    const wrapper = mount(AuditOperation)
    await nextTick()
    await new Promise(r => setTimeout(r, 50))
    expect(wrapper.find('.t-crud-page').exists()).toBe(false)
    expect(wrapper.find('.t-crud-toolbar').exists()).toBe(false)
  })
})
