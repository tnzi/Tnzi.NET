import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * The system pages' write actions (disconnect, clear) and the log viewer's reads
 * go through bridges that throw the server's refusal. Each used to swallow it in
 * a `catch {}` whose comment said the bridge already had: a refused disconnect or
 * clear read as done, and a 403 on the log viewer read as "no logs".
 */
const h = vi.hoisted(() => ({
  error: vi.fn(),
  disconnectUser: vi.fn(),
  clearExceptions: vi.fn(),
  clearMissing: vi.fn(),
  exportMissing: vi.fn(),
  clearPerformance: vi.fn(),
  getLevels: vi.fn(),
  getFiles: vi.fn(),
  tail: vi.fn(),
  search: vi.fn(),
}))

vi.mock('../../../src/pages/_shared/safe-message', () => ({
  useSafeMessage: () => ({ success: vi.fn(), error: h.error, warning: vi.fn(), info: vi.fn() }),
}))
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))
vi.mock('../../../src/services/bridges/signalr-bridge', () => ({
  createSignalRBridge: () => ({
    getStats: vi.fn(async () => null),
    getOnlineUsers: vi.fn(async () => []),
    disconnectUser: h.disconnectUser,
  }),
}))
vi.mock('../../../src/services/bridges/diagnostics-bridge', () => ({
  createDiagnosticsBridge: () => ({
    exceptions: {
      getSummary: vi.fn(async () => ({ totalCount: 0, topExceptions: [], byStatusCode: {}, byErrorCode: {} })),
      getRecent: vi.fn(async () => []),
      clear: h.clearExceptions,
    },
    controllers: { list: vi.fn(async () => ({ totalCount: 0, controllers: [] })) },
    modules: { list: vi.fn(async () => []) },
    sensitiveEndpoints: { list: vi.fn(async () => ({ totalCount: 0, endpoints: [] })) },
  }),
}))
vi.mock('../../../src/services/bridges/localization-bridge', () => ({
  createLocalizationBridge: () => ({
    getSummary: vi.fn(async () => null),
    getMissing: vi.fn(async () => []),
    exportMissing: h.exportMissing,
    clearMissing: h.clearMissing,
  }),
}))
vi.mock('../../../src/services/bridges/performance-bridge', () => ({
  createPerformanceBridge: () => ({
    getSummary: vi.fn(async () => null),
    getEndpoints: vi.fn(async () => []),
    getSlowRequests: vi.fn(async () => []),
    clear: h.clearPerformance,
  }),
}))
vi.mock('../../../src/services/bridges/logging-bridge', () => ({
  createLoggingBridge: () => ({
    logFiles: { getLevels: h.getLevels, getFiles: h.getFiles, tail: h.tail, search: h.search },
  }),
}))

import SignalRMonitor from '../../../src/pages/system/SignalRMonitor.vue'
import Diagnostics from '../../../src/pages/system/Diagnostics.vue'
import MissingTranslations from '../../../src/pages/system/MissingTranslations.vue'
import Performance from '../../../src/pages/system/Performance.vue'
import LogViewer from '../../../src/pages/system/LogViewer.vue'

const refused = () => Promise.reject(new Error('Forbidden'))

describe('system page actions report a refusal', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    h.error.mockReset()
    h.getLevels.mockReset().mockResolvedValue([])
    h.getFiles.mockReset().mockResolvedValue([])
    h.tail.mockReset().mockResolvedValue({ lines: [] })
  })

  it.each([
    ['SignalR disconnect', SignalRMonitor, h.disconnectUser, (vm: Record<string, (arg?: string) => Promise<void>>) => vm.disconnect!('u1')],
    ['Diagnostics clear exceptions', Diagnostics, h.clearExceptions, (vm: Record<string, (arg?: string) => Promise<void>>) => vm.clearExceptions!()],
    ['Missing translations clear', MissingTranslations, h.clearMissing, (vm: Record<string, (arg?: string) => Promise<void>>) => vm.clearAll!()],
    ['Missing translations export', MissingTranslations, h.exportMissing, (vm: Record<string, (arg?: string) => Promise<void>>) => vm.downloadStubs!()],
    ['Performance clear', Performance, h.clearPerformance, (vm: Record<string, (arg?: string) => Promise<void>>) => vm.clearAll!()],
  ] as const)('★ %s', async (_name, page, action, run) => {
    action.mockReset().mockImplementation(refused)
    const wrapper = mount(page, { global: { stubs: { TTabsPage: { template: '<div><slot /></div>' } } } })
    await flushPromises()

    await expect(run(wrapper.vm as unknown as Record<string, (arg?: string) => Promise<void>>)).resolves.toBeUndefined()
    expect(h.error).toHaveBeenCalledWith('Forbidden')
    wrapper.unmount()
  })
})

describe('log viewer', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    h.error.mockReset()
    h.getLevels.mockReset().mockResolvedValue([])
    h.getFiles.mockReset().mockResolvedValue([])
    h.tail.mockReset().mockResolvedValue({ lines: [] })
    h.search.mockReset()
  })

  it('★ a refused levels read is reported, not shown as "no logs"', async () => {
    h.getLevels.mockImplementation(refused)
    const wrapper = mount(LogViewer)
    await flushPromises()

    expect(h.error).toHaveBeenCalledWith('Forbidden')
    wrapper.unmount()
  })

  it('★ a refused search is reported, not silently dropped', async () => {
    h.search.mockImplementation(refused)
    const wrapper = mount(LogViewer)
    await flushPromises()
    const vm = wrapper.vm as unknown as { searchKeyword: string; runSearch: () => Promise<void> }

    vm.searchKeyword = 'timeout'
    await vm.runSearch()

    expect(h.error).toHaveBeenCalledWith('Forbidden')
    wrapper.unmount()
  })

  // Jumping to a search hit in another level used to auto-select that level's
  // newest file (starting its tail read) and then read the hit's file: two tails
  // in flight, and whichever answered last was shown under the hit file's name.
  it('★ a jump to a hit in another level shows the hit file, whatever answers last', async () => {
    const tails = new Map<string, (value: unknown) => void>()
    h.tail.mockImplementation(({ file }: { file: string }) => new Promise((resolve) => { tails.set(file, resolve) }))
    h.getFiles.mockImplementation(async (level: string) =>
      level === 'Error'
        ? [{ fileName: 'error-newest.log', size: 1, lastModified: '' }, { fileName: 'error-hit.log', size: 1, lastModified: '' }]
        : [],
    )
    const wrapper = mount(LogViewer)
    await flushPromises()
    const vm = wrapper.vm as unknown as {
      jumpToHit: (hit: { level: string; fileName: string; lineNumber: number; line: string }) => void
      selectedFile: string
      tail: { lines: string[] } | null
    }

    vm.jumpToHit({ level: 'Error', fileName: 'error-hit.log', lineNumber: 1, line: 'x' })
    await flushPromises()

    tails.get('error-hit.log')?.({ lines: ['hit'] })
    await flushPromises()
    tails.get('error-newest.log')?.({ lines: ['newest'] })
    await flushPromises()

    expect(vm.selectedFile).toBe('error-hit.log')
    expect(vm.tail?.lines).toEqual(['hit'])
    wrapper.unmount()
  })
})
