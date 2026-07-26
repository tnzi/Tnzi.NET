import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Threads integration test - production-grade table page (TCrudPage) with an
 * edit-title modal, declarative row actions (View / Export JSON / Export MD /
 * Edit / Delete), a message-transcript detail drawer, and JSON/Markdown export.
 *
 * The drawer's open flow calls bridge.threads.getDetail; Export JSON calls
 * bridge.threads.exportJson and downloads a Blob; Export MD opens the URL from
 * bridge.threads.getExportMarkdownUrl. All three sub-contracts are mocked.
 */
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

const threadFetch = vi.fn(async () => ({
  items: [
    {
      id: 't1',
      agentId: 'a1',
      title: 'Onboarding chat',
      messageCount: 4,
      lastActivityTime: '2026-04-03T10:00:00Z',
      creationTime: '2026-04-01T09:00:00Z',
    },
    {
      id: 't2',
      agentId: 'a2',
      title: 'Billing question',
      messageCount: 2,
      lastActivityTime: '2026-04-04T12:00:00Z',
      creationTime: '2026-04-02T11:00:00Z',
    },
  ],
  totalCount: 2,
  pageIndex: 1,
  pageSize: 20,
}))

const getDetail = vi.fn(async (_id: string) => ({
  id: 't1',
  agentId: 'a1',
  agentName: 'Support Bot',
  title: 'Onboarding chat',
  messageCount: 2,
  lastActivityTime: '2026-04-03T10:00:00Z',
  creationTime: '2026-04-01T09:00:00Z',
  messages: [
    { id: 'm1', role: 'user', content: 'How do I sign up?', order: 0, creationTime: '2026-04-01T09:00:00Z' },
    { id: 'm2', role: 'assistant', content: 'Click the Register button.', order: 1, creationTime: '2026-04-01T09:00:05Z' },
  ],
}))

const exportJson = vi.fn(async (id: string) => ({
  id,
  agentId: 'a1',
  agentName: 'Support Bot',
  title: 'Onboarding chat',
  messageCount: 2,
  lastActivityTime: '2026-04-03T10:00:00Z',
  creationTime: '2026-04-01T09:00:00Z',
  exportedAt: '2026-04-05T00:00:00Z',
  messages: [],
}))

const getExportMarkdownUrl = vi.fn((id: string) => `/api/admin/ai/threads/${id}/export/markdown`)
const updateMock = vi.fn(async (id: string, data: unknown) => ({ id, ...(data as object) }))
const deleteMock = vi.fn(async () => undefined)

vi.mock('../../../src/services/bridges/ai-bridge', () => ({
  createAiBridge: () => ({
    threads: {
      fetch: threadFetch,
      update: updateMock,
      delete: deleteMock,
      getDetail,
      exportJson,
      getExportMarkdownUrl,
    },
  }),
}))

import Threads from '../../../src/pages/ai/threads/Threads.vue'

const stubs = {
  DataTable: { name: 'DataTable', props: ['data'], template: '<div class="n-data-table-stub" />' },
  Pagination: { name: 'Pagination', template: '<div class="n-pagination-stub" />' },
  Input: {
    name: 'Input',
    props: ['value'],
    emits: ['update:value'],
    template:
      '<input class="n-input-stub" :value="value" @input="$emit(\'update:value\', $event.target.value)" />',
  },
  Button: { name: 'Button', template: '<button @click="$emit(\'click\')"><slot /></button>' },
  Modal: {
    name: 'Modal',
    props: ['show'],
    emits: ['update:show'],
    template: '<div v-if="show" class="n-modal-stub"><slot /><slot name="footer" /></div>',
  },
  Popover: { name: 'Popover', template: '<div><slot name="trigger" /><slot /></div>' },
  Popconfirm: { name: 'Popconfirm', template: '<div><slot name="trigger" /><slot /></div>' },
  Drawer: {
    name: 'Drawer',
    props: ['show'],
    emits: ['update:show'],
    template: '<div v-if="show" class="n-drawer-stub"><slot /></div>',
  },
  DrawerContent: { name: 'DrawerContent', template: '<div class="n-drawer-content-stub"><slot /><slot name="footer" /></div>' },
  Spin: { name: 'Spin', props: ['show'], template: '<div class="n-spin-stub"><slot /></div>' },
  Tag: { name: 'Tag', template: '<span class="n-tag-stub"><slot /></span>' },
  Checkbox: { name: 'Checkbox', template: '<input type="checkbox" />' },
  Form: { name: 'Form', template: '<form><slot /></form>' },
  FormItem: { name: 'FormItem', template: '<div class="form-item"><slot /></div>' },
}

describe('Threads page (TCrudPage table + message drawer)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    threadFetch.mockClear()
    getDetail.mockClear()
    exportJson.mockClear()
    getExportMarkdownUrl.mockClear()
    updateMock.mockClear()
    deleteMock.mockClear()
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('mounts and fetches threads on mount', async () => {
    mount(Threads, { global: { stubs } })
    await flushPromises()
    expect(threadFetch).toHaveBeenCalledTimes(1)
  })

  it('renders the thread list and page title', async () => {
    const wrapper = mount(Threads, { global: { stubs } })
    await flushPromises()
    // Titles flow into the (stubbed) data table via the crud state's items.
    const vm = wrapper.vm as unknown as { crud: { items: { value: Array<{ id: string }> } } }
    expect(vm.crud.items.value.map((r) => r.id)).toEqual(['t1', 't2'])
    expect(wrapper.text()).toContain('Threads')
  })

  it('opening the view state loads the thread transcript via onView', async () => {
    const wrapper = mount(Threads, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as {
      crud: {
        openView: (row: { id: string; title: string }) => void
        formModal: { visible: { value: boolean }; mode: { value: string | null } }
      }
      detail: { id: string; messages: unknown[] } | null
    }
    // The read-only detail now rides the CRUD `view` open-state; `onView` lazy-
    // loads the heavier transcript payload (getDetail) on open.
    vm.crud.openView({ id: 't1', title: 'Onboarding chat' })
    await flushPromises()
    expect(getDetail).toHaveBeenCalledTimes(1)
    expect(getDetail).toHaveBeenCalledWith('t1')
    expect(vm.crud.formModal.visible.value).toBe(true)
    expect(vm.crud.formModal.mode.value).toBe('view')
    expect(vm.detail?.id).toBe('t1')
    expect(vm.detail?.messages).toHaveLength(2)
  })

  it('exportJson fetches the payload and triggers a Blob download', async () => {
    const createObjectURL = vi.fn(() => 'blob:thread-t1')
    const revokeObjectURL = vi.fn()
    vi.stubGlobal('URL', { createObjectURL, revokeObjectURL })
    const clickSpy = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined)

    const wrapper = mount(Threads, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as { exportJson: (id: string) => Promise<void> }

    vi.useFakeTimers()
    try {
      await vm.exportJson('t1')
      await flushPromises()

      expect(exportJson).toHaveBeenCalledWith('t1')
      expect(createObjectURL).toHaveBeenCalledTimes(1)
      expect(clickSpy).toHaveBeenCalledTimes(1)

      // The object URL must NOT be released in the click's own tick: the click
      // only SCHEDULES the download, and a same-tick revoke has been observed to
      // cancel it in Firefox and Safari. It is released after a grace period.
      expect(revokeObjectURL).not.toHaveBeenCalled()
      vi.runAllTimers()
      expect(revokeObjectURL).toHaveBeenCalledWith('blob:thread-t1')
    } finally {
      vi.useRealTimers()
    }

    vi.unstubAllGlobals()
  })

  it('exportMd resolves the markdown URL and triggers a download', async () => {
    const clickSpy = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined)

    const wrapper = mount(Threads, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as { exportMd: (id: string) => void }
    vm.exportMd('t2')
    await flushPromises()

    expect(getExportMarkdownUrl).toHaveBeenCalledWith('t2')
    expect(clickSpy).toHaveBeenCalledTimes(1)
  })

  it('edit submits a title-only update through the bridge', async () => {
    const wrapper = mount(Threads, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as {
      crud: {
        openEdit: (row: unknown) => void
        formModal: { formData: { value: Record<string, unknown> | null } }
        submit: () => Promise<unknown>
      }
    }
    vm.crud.openEdit({ id: 't1', title: 'Onboarding chat' })
    vm.crud.formModal.formData.value = { id: 't1', title: 'Renamed thread' }
    await vm.crud.submit()
    await flushPromises()
    expect(updateMock).toHaveBeenCalledWith('t1', { title: 'Renamed thread' })
  })

  it('delete removes the thread through the bridge', async () => {
    const wrapper = mount(Threads, { global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as { crud: { handleDelete: (ids: string[]) => Promise<void> } }
    await vm.crud.handleDelete(['t2'])
    await flushPromises()
    expect(deleteMock).toHaveBeenCalledWith(['t2'])
  })
})
