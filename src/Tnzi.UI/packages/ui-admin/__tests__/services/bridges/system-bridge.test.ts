import { describe, it, expect, vi } from 'vitest'
import { createSystemBridge } from '../../../src/services/bridges/system-bridge'

function mockMenuApi() {
  return {
    getList: vi.fn(async () => [
      { id: 'm1', name: 'Dashboard', path: '/dashboard', sortOrder: 1, isHidden: false, children: [] },
      { id: 'm2', name: 'Users', path: '/users', sortOrder: 2, isHidden: false, children: [] },
    ]),
    getById: vi.fn(async () => null),
    getUserTree: vi.fn(async () => []),
    create: vi.fn(async (d: unknown) => ({ ...(d as object), id: 'new-menu' })),
    update: vi.fn(async (id: string, d: unknown) => ({ id, ...(d as object) })),
    delete: vi.fn(async () => undefined),
    batchDelete: vi.fn(async () => undefined),
    batchUpdateOrders: vi.fn(async () => undefined),
    move: vi.fn(async () => undefined),
  }
}

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

describe('system-bridge', () => {
  it('exposes menus / settings / accessLogs / scheduledJobs sub-contracts', () => {
    const bridge = createSystemBridge({
      menuApi: mockMenuApi() as never,
      settingApi: mockSettingApi() as never,
      accessLogApi: mockAccessLogApi() as never,
    })
    expect(typeof bridge.menus.fetch).toBe('function')
    expect(typeof bridge.menus.reorder).toBe('function')
    expect(typeof bridge.settings.fetch).toBe('function')
    expect(typeof bridge.accessLogs.fetch).toBe('function')
    expect(typeof bridge.scheduledJobs.fetch).toBe('function')
    expect(typeof bridge.scheduledJobs.trigger).toBe('function')
  })

  it('menus.fetch calls menuApi.getList and returns paged items', async () => {
    const menuApi = mockMenuApi()
    const bridge = createSystemBridge({
      menuApi: menuApi as never,
      settingApi: mockSettingApi() as never,
      accessLogApi: mockAccessLogApi() as never,
    })
    const result = await bridge.menus.fetch({ pageIndex: 1, pageSize: 10, searchText: '', filters: {} })
    expect(menuApi.getList).toHaveBeenCalled()
    expect(result.items).toHaveLength(2)
    expect(result.totalCount).toBe(2)
  })

  it('settings.fetch honors query.filters.groupPrefix (in-memory filter)', async () => {
    const settingApi = mockSettingApi()
    const bridge = createSystemBridge({
      menuApi: mockMenuApi() as never,
      settingApi: settingApi as never,
      accessLogApi: mockAccessLogApi() as never,
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
      menuApi: mockMenuApi() as never,
      settingApi: mockSettingApi() as never,
      accessLogApi: mockAccessLogApi() as never,
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
      menuApi: mockMenuApi() as never,
      settingApi: settingApi as never,
      accessLogApi: mockAccessLogApi() as never,
    })
    await bridge.settings.delete(['s1', 's2'])
    expect(settingApi.batchDelete).toHaveBeenCalledWith(['s1', 's2'])
  })

  it('accessLogs.fetch delegates to accessLogApi.getList', async () => {
    const accessLogApi = mockAccessLogApi()
    const bridge = createSystemBridge({
      menuApi: mockMenuApi() as never,
      settingApi: mockSettingApi() as never,
      accessLogApi: accessLogApi as never,
    })
    const result = await bridge.accessLogs.fetch({ pageIndex: 1, pageSize: 20, searchText: '', filters: {} })
    expect(accessLogApi.getList).toHaveBeenCalled()
    expect(result.items).toHaveLength(1)
  })

  it('scheduledJobs.fetch requires an HttpClient (no stub fallback)', async () => {
    // Plan E wired scheduledJobs to real /admin/scheduled-jobs via direct
    // HttpClient calls. When deps.client is missing, the bridge surfaces a
    // clear 'HttpClient required' error instead of silently returning an
    // empty list — fails fast at call time rather than hiding the gap.
    const bridge = createSystemBridge({
      menuApi: mockMenuApi() as never,
      settingApi: mockSettingApi() as never,
      accessLogApi: mockAccessLogApi() as never,
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
      menuApi: mockMenuApi() as never,
      settingApi: mockSettingApi() as never,
      accessLogApi: mockAccessLogApi() as never,
    })
    const result = await bridge.scheduledJobs.fetch({ pageIndex: 1, pageSize: 20, searchText: '', filters: {} })
    expect(mockClient.get).toHaveBeenCalledWith('/admin/scheduled-jobs')
    expect(result.items).toHaveLength(1)
    expect(result.items[0].id).toBe('job-1')
  })
})
