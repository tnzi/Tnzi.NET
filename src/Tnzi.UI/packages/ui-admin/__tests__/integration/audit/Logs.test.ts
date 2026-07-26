import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import Logs from '../../../src/pages/audit/Logs.vue'

vi.mock('../../../src/plugin/client', () => ({ useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }) }))
// TAuditTimeline builds an identity bridge to power its user-filter selector.
vi.mock('../../../src/services/bridges/identity-bridge', () => ({
  createIdentityBridge: () => ({
    users: { fetch: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20 })) },
  }),
}))
vi.mock('../../../src/services/bridges/audit-bridge', () => ({
  createAuditBridge: () => ({
    logs: {
      fetch: vi.fn(async () => ({
        items: [
          { id: 'op1', functionName: 'GetUser', userId: 'u1', userName: 'admin', elapsed: 42, startTime: '2026-01-01T00:00:00Z', resultType: 'Success', entityEntries: [], creationTime: '2026-01-01T00:00:00Z' },
          { id: 'op2', functionName: 'CreateRole', userId: 'u1', userName: 'admin', elapsed: 88, startTime: '2026-01-01T00:01:00Z', resultType: 'Success', entityEntries: [], creationTime: '2026-01-01T00:01:00Z' },
        ],
        totalCount: 2,
        pageIndex: 1,
        pageSize: 20,
      })),
      detail: vi.fn(async (id: string) => ({ id, functionName: 'GetUser', resultType: 'Success', entityEntries: [], creationTime: '2026-01-01T00:00:00Z', startTime: '2026-01-01T00:00:00Z', elapsed: 42 })),
    },
    operations: {
      fetch: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20 })),
      detail: vi.fn(async (id: string) => ({ id, entityEntries: [] })),
    },
  }),
  // The bridge re-exports `AuditResultType` / `EntityChangeType` so pages read
  // the enum values without reaching into `@tnzi/core/services/audit`. Both are
  // PascalCase string enums (global JsonStringEnumConverter) - mirror that here.
  AuditResultType: {
    Success: 'Success',
    Failed: 'Failed',
    Warning: 'Warning',
  },
  EntityChangeType: {
    Unchanged: 'Unchanged',
    Added: 'Added',
    Modified: 'Modified',
    Deleted: 'Deleted',
    Detached: 'Detached',
  },
}))

describe('Logs page (Tier 2: timeline view)', () => {
  beforeEach(() => { setActivePinia(createPinia()) })

  it('mounts the timeline view and fetches audit entries on load', async () => {
    const wrapper = mount(Logs)
    await nextTick()
    // Bridge.fetch is async - wait a tick beyond the initial mount.
    await new Promise(r => setTimeout(r, 50))
    await nextTick()
    expect(wrapper.find('.t-audit-timeline').exists()).toBe(true)
    // Two seeded operation entries should be rendered as timeline items.
    const items = wrapper.findAll('.n-timeline-item')
    expect(items.length).toBeGreaterThanOrEqual(2)
  })

  it('does not show TCrudPage chrome (timeline view replaces it)', async () => {
    const wrapper = mount(Logs)
    await nextTick()
    await new Promise(r => setTimeout(r, 50))
    // The legacy CRUD container is gone - no toolbar / column-setting popover.
    expect(wrapper.find('.t-crud-page').exists()).toBe(false)
    expect(wrapper.find('.t-crud-toolbar').exists()).toBe(false)
  })
})
