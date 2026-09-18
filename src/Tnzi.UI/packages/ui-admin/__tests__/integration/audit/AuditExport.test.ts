import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, enableAutoUnmount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'

// The audit CSV / JSON export existed end to end (backend -> @tnzi/core ->
// audit-bridge) with no page calling it; an auditor could not produce the
// evidence file from the console at all. These tests pin the page-level slice:
// the export menu on Logs / Operations sends the ACTIVE filter to the bridge,
// writes the Blob, and shows the server's reason ("narrow the filter") when the
// export is refused instead of doing nothing.

const downloadBlob = vi.fn()
vi.mock('@tnzi/core/utils', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@tnzi/core/utils')>()),
  downloadBlob: (...args: unknown[]) => downloadBlob(...args),
}))

const messageApi = { success: vi.fn(), error: vi.fn(), warning: vi.fn(), info: vi.fn(), loading: vi.fn(), create: vi.fn(), destroyAll: vi.fn() }
vi.mock('../../../src/pages/_shared/safe-message', () => ({ useSafeMessage: () => messageApi }))

vi.mock('../../../src/plugin/client', () => ({ useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }) }))
vi.mock('../../../src/services/bridges/identity-bridge', () => ({
  createIdentityBridge: () => ({
    users: { fetch: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20 })) },
  }),
}))

const csvBlob = new Blob(['Id,Url\n1,/x'], { type: 'text/csv' })
const jsonBlob = new Blob(['[]'], { type: 'application/json' })
const emptyPage = { items: [], totalCount: 0, pageIndex: 1, pageSize: 20 }
const bridge = {
  logs: {
    fetch: vi.fn(async () => emptyPage),
    detail: vi.fn(async (id: string) => ({ id, entityEntries: [] })),
    exportCsv: vi.fn(async () => csvBlob),
    exportJson: vi.fn(async () => jsonBlob),
  },
  operations: {
    fetch: vi.fn(async () => emptyPage),
    detail: vi.fn(async (id: string) => ({ id, entityEntries: [] })),
    exportCsv: vi.fn(async () => csvBlob),
    exportJson: vi.fn(async () => jsonBlob),
  },
}
vi.mock('../../../src/services/bridges/audit-bridge', () => ({
  createAuditBridge: () => bridge,
  AuditResultType: { Success: 'Success', Failed: 'Failed', Warning: 'Warning' },
  EntityChangeType: { Unchanged: 'Unchanged', Added: 'Added', Modified: 'Modified', Deleted: 'Deleted', Detached: 'Detached' },
}))

import Logs from '../../../src/pages/audit/Logs.vue'
import Operations from '../../../src/pages/audit/Operations.vue'

async function mountPage(page: typeof Logs) {
  const wrapper = mount(page)
  await nextTick()
  await flushPromises()
  return wrapper
}

/** The menu teleports; select through the options the component hands NDropdown. */
async function chooseExport(wrapper: ReturnType<typeof mount>, key: 'csv' | 'json'): Promise<void> {
  const dropdown = wrapper.findComponent({ name: 'Dropdown' })
  expect(dropdown.exists(), 'export dropdown').toBe(true)
  const keys = (dropdown.props('options') as Array<{ key: string }>).map((o) => o.key)
  expect(keys).toEqual(['csv', 'json'])
  dropdown.vm.$emit('select', key)
  await flushPromises()
}

async function setFunctionName(wrapper: ReturnType<typeof mount>, value: string): Promise<void> {
  const inputs = wrapper.findAll('input')
  const input = inputs.find((i) => i.attributes('placeholder') === 'Action' || i.attributes('placeholder') === 'Operation')
  expect(input, 'function-name filter input').toBeDefined()
  await input!.setValue(value)
}

describe('audit export (Logs / Operations)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    bridge.logs.exportCsv.mockResolvedValue(csvBlob)
    bridge.operations.exportJson.mockResolvedValue(jsonBlob)
  })
  enableAutoUnmount(afterEach)

  it('Logs: exports CSV with the active filter and writes the file', async () => {
    const wrapper = await mountPage(Logs)
    await setFunctionName(wrapper, 'GetUser')
    await chooseExport(wrapper, 'csv')

    expect(bridge.logs.exportCsv).toHaveBeenCalledTimes(1)
    expect(bridge.logs.exportCsv).toHaveBeenCalledWith(expect.objectContaining({ functionName: 'GetUser' }))
    // No paging fields: the export is the whole filtered set, never one page of it.
    const sent = bridge.logs.exportCsv.mock.calls[0]![0] as Record<string, unknown>
    expect(sent).not.toHaveProperty('pageIndex')
    expect(sent).not.toHaveProperty('pageSize')
    expect(downloadBlob).toHaveBeenCalledTimes(1)
    const [blob, name] = downloadBlob.mock.calls[0] as [Blob, string]
    expect(blob).toBe(csvBlob)
    expect(name).toMatch(/^audit-logs-\d{8}\.csv$/)
    expect(messageApi.error).not.toHaveBeenCalled()
  })

  it('Operations: exports JSON through the operations sub-contract', async () => {
    const wrapper = await mountPage(Operations)
    await chooseExport(wrapper, 'json')

    expect(bridge.operations.exportJson).toHaveBeenCalledTimes(1)
    expect(bridge.logs.exportJson).not.toHaveBeenCalled()
    const [blob, name] = downloadBlob.mock.calls[0] as [Blob, string]
    expect(blob).toBe(jsonBlob)
    expect(name).toMatch(/^audit-operations-\d{8}\.json$/)
  })

  it('a refused export shows the server reason and writes no file', async () => {
    bridge.logs.exportCsv.mockRejectedValue(new Error('12000 rows matched; narrow the filter to at most 10000'))
    const wrapper = await mountPage(Logs)
    await chooseExport(wrapper, 'csv')

    expect(downloadBlob).not.toHaveBeenCalled()
    expect(messageApi.error).toHaveBeenCalledWith('12000 rows matched; narrow the filter to at most 10000')
  })
})
