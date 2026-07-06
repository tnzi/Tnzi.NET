import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import ScheduledJobs from '../../../src/pages/system/ScheduledJobs.vue'

const mockTrigger = vi.fn(async () => undefined)
const mockDelete = vi.fn(async () => undefined)
const mockFetch = vi.fn(async () => ({
  items: [] as never[],
  totalCount: 0,
  pageIndex: 1,
  pageSize: 20,
}))

// Mock the client composable — the page now calls useAdminClient() to get
// an HttpClient for the bridge. Tests don't need a real client because
// createSystemBridge is fully mocked below, but the composable must return
// something truthy so the require-client check at useAdminClient() passes.
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), delete: vi.fn() }),
}))

vi.mock('../../../src/services/bridges/system-bridge', () => ({
  createSystemBridge: () => ({
    menus: { fetch: vi.fn(), create: vi.fn(), update: vi.fn(), delete: vi.fn(), reorder: vi.fn() },
    settings: { fetch: vi.fn(), create: vi.fn(), update: vi.fn(), delete: vi.fn() },
    accessLogs: { fetch: vi.fn() },
    scheduledJobs: {
      fetch: mockFetch,
      trigger: mockTrigger,
      delete: mockDelete,
    },
  }),
}))

const stubs = {
  DataTable: { props: ['data'], template: '<div class="dt" :data-rows="data.length" />' },
  Pagination: { template: '<div />' },
  Input: { props: ['value'], template: '<input />' },
  Button: { template: '<button @click="$emit(\'click\')"><slot /></button>' },
  Modal: { props: ['show'], template: '<div v-if="show"><slot /></div>' },
  Popover: { template: '<div><slot name="trigger" /></div>' },
  Checkbox: { template: '<input type="checkbox" />' },
  Form: { template: '<form><slot /></form>' },
  FormItem: { template: '<div><slot /></div>' },
  InputNumber: { template: '<input type="number" />' },
  Switch: { template: '<button />' },
  Select: { template: '<select />' },
  DatePicker: { template: '<input type="date" />' },
  // The View action renders its detail in a drawer (closed on mount).
  Drawer: { props: ['show'], template: '<div v-if="show"><slot /></div>' },
  DrawerContent: { template: '<div><slot /></div>' },
  VueDraggable: { template: '<div><slot /></div>' },
}

describe('ScheduledJobs page (Phase 3.16)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it('mounts and attempts to fetch scheduled jobs on load', async () => {
    const wrapper = mount(ScheduledJobs, { global: { stubs } })
    await nextTick()
    await new Promise(r => setTimeout(r, 10))
    expect(wrapper.find('.dt').exists()).toBe(true)
    // fetch was called (even though stub returns empty list)
    expect(mockFetch).toHaveBeenCalled()
  })

  it('hides Create button (Hangfire jobs are registered in code, not via admin)', async () => {
    const wrapper = mount(ScheduledJobs, { global: { stubs } })
    await nextTick()
    await new Promise(r => setTimeout(r, 10))
    // No createData callback → canCreate=false → neither TCrudPage's own
    // create button nor TListShell's fallback may render.
    expect(wrapper.find('.t-crud-page__create').exists()).toBe(false)
    expect(wrapper.find('.t-list-shell__create').exists()).toBe(false)
  })
})
