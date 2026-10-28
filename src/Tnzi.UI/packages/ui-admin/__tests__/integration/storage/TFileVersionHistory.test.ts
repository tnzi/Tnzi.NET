import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import type { FileRecordDto, FileVersionDto } from '@tnzi/core/services/storage'

// The version history inside the file manager's detail drawer: list, upload a new
// version, download / restore / delete one, and the "this host keeps no versions"
// state. The bridge is mocked; what is under test is what the operator can do and
// what reaches the bridge (or, for a cancelled confirm, what does not).

const downloadBlob = vi.fn()
vi.mock('@tnzi/core', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@tnzi/core')>()),
  downloadBlob: (...args: unknown[]) => downloadBlob(...args),
}))

const message = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn(), warning: vi.fn(), info: vi.fn() }))
vi.mock('../../../src/pages/_shared/safe-message', () => ({ useSafeMessage: () => message }))
vi.mock('../../../src/plugin/client', () => ({ useAdminClient: () => ({}) }))

const versionsApi = vi.hoisted(() => ({
  list: vi.fn(),
  upload: vi.fn(),
  restore: vi.fn(),
  download: vi.fn(),
  delete: vi.fn(),
}))
vi.mock('../../../src/services/bridges/storage-bridge', () => ({
  createStorageBridge: () => ({ versions: versionsApi }),
}))

import TFileVersionHistory from '../../../src/pages/storage/components/TFileVersionHistory.vue'
import { useAdminAuthStore } from '../../../src/stores/useAdminAuthStore'

const FILE: FileRecordDto = {
  id: 'f1',
  fileName: '0199-key.pdf',
  originalName: 'contract.pdf',
  size: 3000,
  contentType: 'application/pdf',
} as FileRecordDto

const HISTORY: FileVersionDto[] = [
  { id: 'v3', fileId: 'f1', version: 3, size: 3000, isCurrent: true, creationTime: '2026-10-03T10:00:00Z', creatorId: 'u-1', description: 'Signed copy' },
  { id: 'v2', fileId: 'f1', version: 2, size: 2000, isCurrent: false, creationTime: '2026-10-02T10:00:00Z', creatorId: 'u-1', description: null },
  { id: 'v1', fileId: 'f1', version: 1, size: 1000, isCurrent: false, creationTime: '2026-10-01T10:00:00Z', creatorId: null, description: 'Initial version' },
]

const stubs = {
  TSvgIcon: { template: '<i />' },
  Spin: { template: '<div><slot /></div>' },
  Empty: { props: ['description'], template: '<div class="empty">{{ description }}</div>' },
  Tag: { template: '<span class="tag"><slot /></span>' },
  Input: {
    props: ['value'],
    emits: ['update:value'],
    template: '<input class="desc" :value="value" @input="$emit(\'update:value\', $event.target.value)" />',
  },
  Button: {
    props: ['disabled', 'loading'],
    emits: ['click'],
    template: '<button :disabled="disabled" @click="$emit(\'click\')"><slot /></button>',
  },
  // Renders the trigger, the confirm text, and a "yes" button standing in for the
  // popconfirm's positive action: nothing fires unless the operator confirms.
  Popconfirm: {
    emits: ['positive-click'],
    template:
      '<div class="confirm"><slot name="trigger" /><p class="confirm-text"><slot /></p><button class="confirm-yes" @click="$emit(\'positive-click\')">yes</button></div>',
  },
}

// Echo the key with its params so assertions can see which message was used.
const translate = (key: string, params?: Record<string, unknown>) =>
  params ? `${key}:${JSON.stringify(params)}` : key

function mountHistory(file: FileRecordDto = FILE): VueWrapper {
  return mount(TFileVersionHistory, { props: { file, translate }, global: { stubs } })
}

function items(wrapper: VueWrapper) {
  return wrapper.findAll('.t-file-versions__item')
}

function button(scope: VueWrapper | ReturnType<VueWrapper['find']>, label: string) {
  const found = scope.findAll('button').find((b) => b.text().includes(label))
  if (!found) throw new Error(`no button "${label}"`)
  return found
}

/** The confirm block whose trigger carries `label`, within one version row. */
function confirmFor(row: ReturnType<VueWrapper['find']>, label: string) {
  const found = row.findAll('.confirm').find((c) => c.text().includes(label))
  if (!found) throw new Error(`no confirm for "${label}"`)
  return found
}

describe('TFileVersionHistory', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    versionsApi.list.mockResolvedValue(HISTORY)
  })

  it('lists the history newest first, marks the current version, and shows size, time, author and note', async () => {
    const wrapper = mountHistory()
    await flushPromises()

    expect(versionsApi.list).toHaveBeenCalledWith('f1')
    const rows = items(wrapper)
    expect(rows).toHaveLength(3)
    expect(rows[0].classes()).toContain('is-current')
    expect(rows[0].find('.tag').text()).toBe('versions.current')
    expect(rows[1].find('.tag').exists()).toBe(false)
    expect(rows[0].text()).toContain('versions.label:{"n":3}')
    expect(rows[0].text()).toContain('2.93 KB')
    expect(rows[0].text()).toContain('u-1')
    expect(rows[0].text()).toContain('Signed copy')
  })

  it('offers restore and delete only on versions that are not current', async () => {
    const wrapper = mountHistory()
    await flushPromises()
    const [current, older] = items(wrapper)

    expect(current.text()).toContain('versions.download')
    expect(current.text()).not.toContain('versions.restore')
    expect(current.text()).not.toContain('versions.delete')
    expect(older.text()).toContain('versions.restore')
    expect(older.text()).toContain('versions.delete')
  })

  it('hides every write for a viewer without storage.file.update, but keeps download', async () => {
    useAdminAuthStore().setUserInfo({ id: 'op', username: 'viewer', roles: [], permissions: ['storage.file.view'] } as never)
    const wrapper = mountHistory()
    await flushPromises()

    expect(wrapper.text()).not.toContain('versions.upload')
    expect(wrapper.text()).not.toContain('versions.restore')
    expect(wrapper.text()).not.toContain('versions.delete')
    expect(items(wrapper)[1].text()).toContain('versions.download')
  })

  it('downloads a version through the bridge and saves it under the versioned original name', async () => {
    const blob = new Blob(['v2 bytes'])
    versionsApi.download.mockResolvedValue(blob)
    const wrapper = mountHistory()
    await flushPromises()

    await button(items(wrapper)[1], 'versions.download').trigger('click')
    await flushPromises()

    expect(versionsApi.download).toHaveBeenCalledWith('f1', 2)
    expect(downloadBlob).toHaveBeenCalledWith(blob, 'contract.v2.pdf')
  })

  it('saves an in-flight download under the file it started on, even after the drawer moved to another file', async () => {
    let release: (blob: Blob) => void = () => {}
    versionsApi.download.mockImplementation(() => new Promise((r) => { release = r }))
    const wrapper = mountHistory()
    await flushPromises()

    await button(items(wrapper)[1], 'versions.download').trigger('click')
    await wrapper.setProps({ file: { ...FILE, id: 'f2', originalName: 'invoice.xlsx' } as FileRecordDto })
    const blob = new Blob(['contract v2 bytes'])
    release(blob)
    await flushPromises()

    expect(versionsApi.download).toHaveBeenCalledWith('f1', 2)
    expect(downloadBlob).toHaveBeenCalledWith(blob, 'contract.v2.pdf')
  })

  it('reports a write against the file it started on, even after the drawer moved on', async () => {
    let release: () => void = () => {}
    versionsApi.restore.mockImplementation(() => new Promise<void>((r) => { release = r }))
    const wrapper = mountHistory()
    await flushPromises()

    await confirmFor(items(wrapper)[2], 'versions.restore').find('.confirm-yes').trigger('click')
    await wrapper.setProps({ file: { ...FILE, id: 'f2', originalName: 'invoice.xlsx' } as FileRecordDto })
    release()
    await flushPromises()

    expect(versionsApi.restore).toHaveBeenCalledWith('f1', 1)
    expect(wrapper.emitted('changed')).toEqual([['f1']])
  })

  it('warns that a new version reaches every holder when the file is shared', async () => {
    const shared = mountHistory({ ...FILE, referenceCount: 3 } as FileRecordDto)
    await flushPromises()
    expect(shared.find('.t-file-versions__shared').text()).toBe('versions.sharedHint:{"n":3}')

    const single = mountHistory({ ...FILE, referenceCount: 1 } as FileRecordDto)
    await flushPromises()
    expect(single.find('.t-file-versions__shared').exists()).toBe(false)
  })

  it('reports a refused download instead of saving anything', async () => {
    versionsApi.download.mockRejectedValue(new Error('File not found'))
    const wrapper = mountHistory()
    await flushPromises()

    await button(items(wrapper)[1], 'versions.download').trigger('click')
    await flushPromises()

    expect(downloadBlob).not.toHaveBeenCalled()
    expect(message.error).toHaveBeenCalledWith('File not found')
  })

  it('restores only after the confirm, then reloads and tells the page the content changed', async () => {
    versionsApi.restore.mockResolvedValue(undefined)
    const wrapper = mountHistory()
    await flushPromises()
    const confirm = confirmFor(items(wrapper)[2], 'versions.restore')

    expect(confirm.find('.confirm-text').text()).toBe('versions.confirmRestore:{"n":1}')
    expect(versionsApi.restore).not.toHaveBeenCalled()

    await confirm.find('.confirm-yes').trigger('click')
    await flushPromises()

    expect(versionsApi.restore).toHaveBeenCalledWith('f1', 1)
    expect(versionsApi.list).toHaveBeenCalledTimes(2)
    expect(wrapper.emitted('changed')).toEqual([['f1']])
    expect(message.success).toHaveBeenCalledWith('versions.restoreSuccess:{"n":1}')
  })

  it('deletes only after the confirm and does not report a content change', async () => {
    versionsApi.delete.mockResolvedValue(undefined)
    const wrapper = mountHistory()
    await flushPromises()
    const confirm = confirmFor(items(wrapper)[1], 'versions.delete')

    expect(confirm.find('.confirm-text').text()).toBe('versions.confirmDelete:{"n":2}')
    expect(versionsApi.delete).not.toHaveBeenCalled()

    await confirm.find('.confirm-yes').trigger('click')
    await flushPromises()

    expect(versionsApi.delete).toHaveBeenCalledWith('f1', 2)
    expect(versionsApi.list).toHaveBeenCalledTimes(2)
    expect(wrapper.emitted('changed')).toBeUndefined()
  })

  it('surfaces the server refusal of a delete', async () => {
    versionsApi.delete.mockRejectedValue(new Error('Cannot delete the current version'))
    const wrapper = mountHistory()
    await flushPromises()

    await confirmFor(items(wrapper)[1], 'versions.delete').find('.confirm-yes').trigger('click')
    await flushPromises()

    expect(message.error).toHaveBeenCalledWith('Cannot delete the current version')
  })

  it('uploads the picked file with its note as the next version, then reloads and reports the change', async () => {
    versionsApi.upload.mockResolvedValue({ ...HISTORY[0], id: 'v4', version: 4 })
    const wrapper = mountHistory()
    await flushPromises()

    const picked = new File(['new bytes'], 'contract-final.pdf', { type: 'application/pdf' })
    const input = wrapper.find('input[type="file"]')
    Object.defineProperty(input.element, 'files', { value: [picked], configurable: true })
    await input.trigger('change')

    expect(wrapper.text()).toContain('contract-final.pdf')
    await wrapper.find('input.desc').setValue('  Countersigned  ')
    await button(wrapper, 'versions.submit').trigger('click')
    await flushPromises()

    expect(versionsApi.upload).toHaveBeenCalledWith('f1', picked, 'Countersigned')
    expect(versionsApi.list).toHaveBeenCalledTimes(2)
    expect(wrapper.emitted('changed')).toEqual([['f1']])
    expect(message.success).toHaveBeenCalledWith('versions.uploadSuccess:{"n":4}')
    // The pending upload form is gone once the version is stored.
    expect(wrapper.find('input.desc').exists()).toBe(false)
  })

  it('keeps the picked file when the upload is refused, so the operator can retry', async () => {
    versionsApi.upload.mockRejectedValue(new Error('File type is not allowed'))
    const wrapper = mountHistory()
    await flushPromises()

    const input = wrapper.find('input[type="file"]')
    Object.defineProperty(input.element, 'files', { value: [new File(['x'], 'x.exe')], configurable: true })
    await input.trigger('change')
    await button(wrapper, 'versions.submit').trigger('click')
    await flushPromises()

    expect(message.error).toHaveBeenCalledWith('File type is not allowed')
    expect(wrapper.emitted('changed')).toBeUndefined()
    expect(wrapper.text()).toContain('x.exe')
  })

  it('says the host keeps no versions (bridge null = 501) instead of an error, and offers no upload', async () => {
    versionsApi.list.mockResolvedValue(null)
    const wrapper = mountHistory()
    await flushPromises()

    expect(wrapper.text()).toContain('versions.unavailable')
    expect(wrapper.text()).not.toContain('versions.upload')
    expect(message.error).not.toHaveBeenCalled()
  })

  it('shows a load failure inline with a retry', async () => {
    versionsApi.list.mockRejectedValueOnce(new Error('boom')).mockResolvedValueOnce(HISTORY)
    const wrapper = mountHistory()
    await flushPromises()

    expect(wrapper.find('.t-file-versions__error').text()).toContain('boom')
    await button(wrapper, 'versions.retry').trigger('click')
    await flushPromises()

    expect(items(wrapper)).toHaveLength(3)
  })

  it('shows the empty state for a file that has never been versioned', async () => {
    versionsApi.list.mockResolvedValue([])
    const wrapper = mountHistory()
    await flushPromises()

    expect(wrapper.find('.empty').text()).toBe('versions.empty')
  })

  it('discards a slow answer for the previous file once the drawer has moved on', async () => {
    let releaseFirst: (v: FileVersionDto[]) => void = () => {}
    versionsApi.list
      .mockImplementationOnce(() => new Promise((r) => { releaseFirst = r }))
      .mockResolvedValueOnce([{ ...HISTORY[0], id: 'other', fileId: 'f2', version: 7 }])
    const wrapper = mountHistory()

    await wrapper.setProps({ file: { ...FILE, id: 'f2', originalName: 'other.pdf' } as FileRecordDto })
    await flushPromises()
    releaseFirst(HISTORY)
    await flushPromises()

    const rows = items(wrapper)
    expect(rows).toHaveLength(1)
    expect(rows[0].text()).toContain('versions.label:{"n":7}')
  })
})
