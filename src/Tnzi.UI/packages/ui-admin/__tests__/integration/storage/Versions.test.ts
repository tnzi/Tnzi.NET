import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import Versions from '../../../src/pages/storage/Versions.vue'
import { useAdminAuthStore } from '../../../src/stores/useAdminAuthStore'

const downloadBlob = vi.fn()
vi.mock('@tnzi/core', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@tnzi/core')>()),
  downloadBlob: (...args: unknown[]) => downloadBlob(...args),
}))

const message = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn(), warning: vi.fn(), info: vi.fn() }))
vi.mock('../../../src/pages/_shared/safe-message', () => ({ useSafeMessage: () => message }))

// Same plumbing mock as Chunks / ScheduledJob: useAdminClient is
// called by the page but the bridge is mocked below, so any truthy value works.
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), delete: vi.fn() }),
}))

const bridge = vi.hoisted(() => ({
  fetch: vi.fn(async () => ({
    items: [
      { id: 'v2', fileId: 'f1', version: 2, path: '2026/10/key2.pdf', size: 2048, isCurrent: true, creationTime: '2024-01-02T00:00:00Z', description: 'Updated' },
      { id: 'v1', fileId: 'f1', version: 1, path: '2026/10/key1.pdf', size: 1024, isCurrent: false, creationTime: '2024-01-01T00:00:00Z', description: 'Initial version' },
    ],
    totalCount: 2,
    pageIndex: 1,
    pageSize: 20,
  })),
  restore: vi.fn(async () => undefined),
  download: vi.fn(async () => new Blob(['v1 bytes'])),
  delete: vi.fn(async () => undefined),
  getFile: vi.fn(async () => ({ id: 'f1', fileName: 'key.pdf', originalName: 'contract.pdf' })),
}))

vi.mock('../../../src/services/bridges/storage-bridge', () => ({
  createStorageBridge: () => ({
    files: {
      fetch: vi.fn(),
      get: bridge.getFile,
      create: vi.fn(),
      update: vi.fn(),
      delete: vi.fn(),
      downloadUrl: vi.fn(() => ''),
      initUpload: vi.fn(),
      uploadChunk: vi.fn(),
      completeUpload: vi.fn(),
    },
    chunks: { fetch: vi.fn(), delete: vi.fn() },
    versions: {
      fetch: bridge.fetch,
      restore: bridge.restore,
      download: bridge.download,
      delete: bridge.delete,
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
  VueDraggable: { template: '<div><slot /></div>' },
}

type Row = { fileId: string; version: number; isCurrent: boolean }
type Action = {
  key: string
  onClick?: (row: Row) => unknown
  show?: (row: Row) => boolean
  disabled?: (row: Row) => boolean
  confirm?: boolean | string | ((row: Row) => string)
}

const CURRENT: Row = { fileId: 'f1', version: 2, isCurrent: true }
const OLDER: Row = { fileId: 'f1', version: 1, isCurrent: false }

async function mountPage() {
  const wrapper = mount(Versions, { global: { stubs } })
  await nextTick()
  await new Promise((r) => setTimeout(r, 10))
  const actions = (wrapper.vm as unknown as { rowActions: Action[] }).rowActions
  const action = (key: string) => {
    const found = actions.find((a) => a.key === key)
    if (!found) throw new Error(`no row action "${key}"`)
    return found
  }
  return { wrapper, action }
}

describe('Versions page (Phase 3.20)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it('mounts and fetches versions on load', async () => {
    const { wrapper } = await mountPage()
    expect(wrapper.find('.dt').exists()).toBe(true)
    expect(wrapper.find('.dt').attributes('data-rows')).toBe('2')
  })

  it('downloads a version under the file\'s original name with the version before the extension', async () => {
    const { action } = await mountPage()

    await action('download').onClick!(OLDER)
    await new Promise((r) => setTimeout(r, 0))

    expect(bridge.download).toHaveBeenCalledWith('f1', 1)
    expect(bridge.getFile).toHaveBeenCalledWith('f1')
    expect(downloadBlob).toHaveBeenCalledWith(expect.any(Blob), 'contract.v1.pdf')
  })

  it('reports a refused download and saves nothing', async () => {
    bridge.download.mockRejectedValueOnce(new Error('File not found'))
    const { action } = await mountPage()

    await action('download').onClick!(OLDER)
    await new Promise((r) => setTimeout(r, 0))

    expect(downloadBlob).not.toHaveBeenCalled()
    expect(message.error).toHaveBeenCalledWith('File not found')
  })

  it('delete is destructive: confirmed first, error-styled, and unavailable on the current version', async () => {
    const { action } = await mountPage()
    const del = action('deleteVersion') as Action & { type?: string }

    expect(del.type).toBe('error')
    expect(typeof del.confirm).toBe('function')
    expect((del.confirm as (row: Row) => string)(OLDER)).toContain('1')
    expect(del.disabled!(CURRENT)).toBe(true)
    expect(del.disabled!(OLDER)).toBe(false)
  })

  it('delete reaches the bridge and refreshes; a refusal is surfaced', async () => {
    const { action } = await mountPage()

    await action('deleteVersion').onClick!(OLDER)
    await new Promise((r) => setTimeout(r, 0))
    expect(bridge.delete).toHaveBeenCalledWith('f1', 1)
    expect(bridge.fetch).toHaveBeenCalledTimes(2)

    bridge.delete.mockRejectedValueOnce(new Error('Cannot delete the current version'))
    await action('deleteVersion').onClick!(OLDER)
    await new Promise((r) => setTimeout(r, 0))
    expect(message.error).toHaveBeenCalledWith('Cannot delete the current version')
  })

  it('restore asks for confirmation before swapping the current content', async () => {
    const { action } = await mountPage()
    const restore = action('restore')

    expect(typeof restore.confirm).toBe('function')
    expect(restore.disabled!(CURRENT)).toBe(true)

    await restore.onClick!(OLDER)
    await new Promise((r) => setTimeout(r, 0))
    expect(bridge.restore).toHaveBeenCalledWith('f1', 1)
  })

  it('a viewer without storage.file.update keeps download but loses restore and delete', async () => {
    useAdminAuthStore().setUserInfo({ id: 'op', username: 'viewer', roles: [], permissions: ['storage.file.view'] } as never)
    const { action } = await mountPage()

    expect(action('download').show).toBeUndefined()
    expect(action('restore').show!(OLDER)).toBe(false)
    expect(action('deleteVersion').show!(OLDER)).toBe(false)
  })
})
