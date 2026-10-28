import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, enableAutoUnmount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import Operations from '../../../src/pages/audit/Operations.vue'

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
      fetch: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20 })),
      detail: vi.fn(async (id: string) => ({ id, entityEntries: [] })),
    },
    operations: {
      fetch: vi.fn(async () => ({
        items: [
          { id: 'op1', functionName: 'GetUser', userId: 'u1', userName: 'admin', elapsed: 42, startTime: '2026-01-01T00:00:00Z', resultType: 'Success', entityEntries: [], creationTime: '2026-01-01T00:00:00Z' },
          { id: 'op2', functionName: 'CreateRole', userId: 'u2', userName: 'editor', elapsed: 155, startTime: '2026-01-01T00:01:00Z', resultType: 'Success', entityEntries: [], creationTime: '2026-01-01T00:01:00Z' },
        ],
        totalCount: 2,
        pageIndex: 1,
        pageSize: 20,
      })),
      detail: vi.fn(async (id: string) => ({
        id, functionName: 'CreateRole', resultType: 'Success', entityEntries: [], creationTime: '2026-01-01T00:00:00Z', startTime: '2026-01-01T00:00:00Z', elapsed: 42,
        httpStatusCode: 201, permissionName: 'identity.role.create',
        requestBody: '{"name":"Auditors"}', responseResult: 'sandbox stdout: ok',
      })),
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

describe('Operations page (Tier 2: timeline view)', () => {
  beforeEach(() => { setActivePinia(createPinia()) })
  // The drawer teleports to document.body; unmount so one test's drawer never leaks into the next.
  enableAutoUnmount(afterEach)

  it('mounts the timeline view and fetches operation entries on load', async () => {
    const wrapper = mount(Operations)
    await nextTick()
    await new Promise(r => setTimeout(r, 50))
    await nextTick()
    expect(wrapper.find('.t-audit-timeline').exists()).toBe(true)
    const items = wrapper.findAll('.n-timeline-item')
    expect(items.length).toBeGreaterThanOrEqual(2)
  })

  it('does not show TCrudPage chrome (timeline view replaces it)', async () => {
    const wrapper = mount(Operations)
    await nextTick()
    await new Promise(r => setTimeout(r, 50))
    expect(wrapper.find('.t-crud-page').exists()).toBe(false)
    expect(wrapper.find('.t-crud-toolbar').exists()).toBe(false)
  })

  // The API returns the captured request body, the recorded response, the status code and the
  // permission; the drawer used to show none of them, so turning on request-body capture had no
  // visible effect and sandbox command output never reached the screen.
  it('shows the captured request body, response, status code and permission in the detail drawer', async () => {
    const wrapper = mount(Operations, { attachTo: document.body })
    await nextTick()
    await new Promise(r => setTimeout(r, 50))
    await nextTick()

    await wrapper.find('.t-audit-timeline__item').trigger('click')
    await new Promise(r => setTimeout(r, 50))
    await nextTick()

    const drawer = document.body.querySelector('.n-drawer')
    expect(drawer).not.toBeNull()
    const text = drawer!.textContent ?? ''
    expect(text).toContain('{"name":"Auditors"}')
    expect(text).toContain('sandbox stdout: ok')
    expect(text).toContain('201')
    expect(text).toContain('identity.role.create')
  })
})
