import { describe, it, expect, vi } from 'vitest'
import { createSystemBridge } from '../../../src/services/bridges/system-bridge'

function mockSettingApi() {
  return {
    getList: vi.fn(async () => [
      { id: 's1', key: 'site.name', value: 'My App', group: 'general', isSystem: false, sortOrder: 1 },
      { id: 's2', key: 'site.description', value: 'App desc', group: 'general', isSystem: false, sortOrder: 2 },
      { id: 's3', key: 'mail.from', value: 'noreply@x', group: 'mail', isSystem: false, sortOrder: 3 },
      { id: 's4', key: 'mail.smtp', value: 'localhost', group: 'mail', isSystem: false, sortOrder: 4 },
    ]),
    getById: vi.fn(async () => null),
    create: vi.fn(async (d: unknown) => ({ ...(d as object), id: 'new-setting' })),
    update: vi.fn(async (id: string, d: unknown) => ({ id, ...(d as object) })),
    delete: vi.fn(async () => undefined),
    batchDelete: vi.fn(async () => undefined),
    getSystemInfo: vi.fn(async () => ({})),
    getGroups: vi.fn(async () => []),
  }
}

function mockAccessLogApi() {
  return {
    getList: vi.fn(async () => ({
      items: [
        { id: 'a1', path: '/api/users', method: 'GET', statusCode: 200, responseTime: 42 },
      ],
      totalCount: 1,
      pageIndex: 1,
      pageSize: 20,
    })),
    getById: vi.fn(async () => null),
    logAccess: vi.fn(async () => undefined),
    getStatistics: vi.fn(async () => ({})),
    getTrend: vi.fn(async () => ({})),
    getTopEndpoints: vi.fn(async () => []),
    deleteExpired: vi.fn(async () => undefined),
    batchDelete: vi.fn(async () => undefined),
  }
}

function mockSettingsCenterApi() {
  const group = { key: 'g', moduleName: 'M', displayName: 'G', order: 0, fields: [] }
  return {
    getDefinitions: vi.fn(async () => ({ success: true, data: [group] })),
    saveGroup: vi.fn(async () => ({ success: true, data: group })),
    resetGroup: vi.fn(async () => ({ success: true, data: group })),
  }
}

describe('system-bridge', () => {
  it('exposes settings / accessLogs / scheduledJobs / settingsCenter sub-contracts', () => {
    const bridge = createSystemBridge({
      settingApi: mockSettingApi() as never,
      accessLogApi: mockAccessLogApi() as never,
      settingsCenterApi: mockSettingsCenterApi() as never,
    })
    expect(typeof bridge.settings.fetch).toBe('function')
    expect(typeof bridge.accessLogs.fetch).toBe('function')
    expect(typeof bridge.scheduledJobs.fetch).toBe('function')
    expect(typeof bridge.scheduledJobs.trigger).toBe('function')
    expect(typeof bridge.settingsCenter.getDefinitions).toBe('function')
    expect(typeof bridge.settingsCenter.saveGroup).toBe('function')
    expect(typeof bridge.settingsCenter.resetGroup).toBe('function')
  })

  it('settings.fetch honors query.filters.groupPrefix (in-memory filter)', async () => {
    const settingApi = mockSettingApi()
    const bridge = createSystemBridge({
      settingApi: settingApi as never,
      accessLogApi: mockAccessLogApi() as never,
      settingsCenterApi: mockSettingsCenterApi() as never,
    })
    const result = await bridge.settings.fetch({
      pageIndex: 1,
      pageSize: 20,
      searchText: '',
      filters: { groupPrefix: 'mail' },
    })
    expect(settingApi.getList).toHaveBeenCalled()
    expect(result.items).toHaveLength(2)
    expect(result.items.every((s) => (s.group ?? '').startsWith('mail'))).toBe(true)
    expect(result.totalCount).toBe(2)
  })

  it('settings.fetch returns all rows when groupPrefix is empty string', async () => {
    const bridge = createSystemBridge({
      settingApi: mockSettingApi() as never,
      accessLogApi: mockAccessLogApi() as never,
      settingsCenterApi: mockSettingsCenterApi() as never,
    })
    const result = await bridge.settings.fetch({
      pageIndex: 1,
      pageSize: 20,
      searchText: '',
      filters: { groupPrefix: '' },
    })
    expect(result.totalCount).toBe(4)
  })

  it('settings.delete calls settingApi.batchDelete with all ids', async () => {
    const settingApi = mockSettingApi()
    const bridge = createSystemBridge({
      settingApi: settingApi as never,
      accessLogApi: mockAccessLogApi() as never,
      settingsCenterApi: mockSettingsCenterApi() as never,
    })
    await bridge.settings.delete(['s1', 's2'])
    expect(settingApi.batchDelete).toHaveBeenCalledWith(['s1', 's2'])
  })

  it('accessLogs.fetch delegates to accessLogApi.getList', async () => {
    const accessLogApi = mockAccessLogApi()
    const bridge = createSystemBridge({
      settingApi: mockSettingApi() as never,
      accessLogApi: accessLogApi as never,
      settingsCenterApi: mockSettingsCenterApi() as never,
    })
    const result = await bridge.accessLogs.fetch({ pageIndex: 1, pageSize: 20, searchText: '', filters: {} })
    expect(accessLogApi.getList).toHaveBeenCalled()
    expect(result.items).toHaveLength(1)
  })

  it('scheduledJobs.fetch requires an HttpClient (no stub fallback)', async () => {
    // Plan E wired scheduledJobs to real /admin/scheduled-jobs via direct
    // HttpClient calls. When deps.client is missing, the bridge surfaces a
    // clear 'HttpClient required' error instead of silently returning an
    // empty list - fails fast at call time rather than hiding the gap.
    const bridge = createSystemBridge({
      settingApi: mockSettingApi() as never,
      accessLogApi: mockAccessLogApi() as never,
      settingsCenterApi: mockSettingsCenterApi() as never,
    })
    await expect(bridge.scheduledJobs.fetch({ pageIndex: 1, pageSize: 20, searchText: '', filters: {} }))
      .rejects.toThrow(/HttpClient.*required/)
  })

  it('scheduledJobs.fetch calls GET /admin/scheduled-jobs when client is provided', async () => {
    const mockClient = {
      get: vi.fn(async () => ({ succeeded: true, data: [{ id: 'job-1', cron: '0 * * * *', removed: false }] })),
      post: vi.fn(async () => ({ succeeded: true, data: null })),
      delete: vi.fn(async () => ({ succeeded: true, data: null })),
    }
    const bridge = createSystemBridge({
      client: mockClient as never,
      settingApi: mockSettingApi() as never,
      accessLogApi: mockAccessLogApi() as never,
      settingsCenterApi: mockSettingsCenterApi() as never,
    })
    const result = await bridge.scheduledJobs.fetch({ pageIndex: 1, pageSize: 20, searchText: '', filters: {} })
    expect(mockClient.get).toHaveBeenCalledWith('/admin/scheduled-jobs')
    expect(result.items).toHaveLength(1)
    expect(result.items[0].id).toBe('job-1')
  })
})

describe('system-bridge settingsCenter', () => {
  it('unwraps definitions / saveGroup / resetGroup', async () => {
    const group = { key: 'g', moduleName: 'M', displayName: 'G', order: 0, fields: [] }
    const settingsCenterApi = {
      getDefinitions: vi.fn().mockResolvedValue({ success: true, data: [group] }),
      saveGroup: vi.fn().mockResolvedValue({ success: true, data: group }),
      resetGroup: vi.fn().mockResolvedValue({ success: true, data: group }),
    }
    const bridge = createSystemBridge({
      settingApi: mockSettingApi() as never,
      accessLogApi: mockAccessLogApi() as never,
      settingsCenterApi: settingsCenterApi as never,
    })

    expect(await bridge.settingsCenter.getDefinitions()).toEqual([group])
    expect(await bridge.settingsCenter.saveGroup('g', { 'A:B': '1' })).toEqual(group)
    expect(settingsCenterApi.saveGroup).toHaveBeenCalledWith('g', { 'A:B': '1' })
    expect(await bridge.settingsCenter.resetGroup('g')).toEqual(group)
  })

  it('rejects when constructed without deps', async () => {
    const bridge = createSystemBridge()
    await expect(bridge.settingsCenter.getDefinitions()).rejects.toThrow()
  })
})
