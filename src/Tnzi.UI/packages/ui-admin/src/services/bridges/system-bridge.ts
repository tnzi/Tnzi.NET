/**
 * System bridge — full implementation (Phase 3 Task 3.11).
 *
 * Adapts the system backend APIs to BridgeCrudContract + custom method shapes
 * used by all TCrudPage-based system management pages.
 *
 * Sub-contracts:
 *   - menus       → useAdminMenuApi  (full CRUD + reorder)
 *   - settings    → useAdminSettingApi (full CRUD; used by Parameter page 3.14)
 *   - accessLogs  → useAdminAccessLogApi (read-only)
 *   - scheduledJobs → stub; Hangfire admin endpoints are NOT yet exposed via
 *                     @tnzi/core. Bridge rejects all calls until backend ships.
 *
 * PLAN DEVIATION: The plan (3.11) expected 5 sub-contracts including
 * DictionaryService and ScheduledJobService. Neither exists in
 * @tnzi/core/services/system. The system API ships Menu, Setting, and AccessLog.
 * scheduledJobs is exposed as a stub sub-contract so Task 3.16 (ScheduledJob page)
 * can be wired and tested without breaking builds.
 * Dictionary (Task 3.13) is re-scoped to Settings (same API, different UI label).
 */
import {
  useAdminMenuApi,
  useAdminSettingApi,
  useAdminAccessLogApi,
  type MenuInfoDto,
  type CreateMenuDto,
  type UpdateMenuDto,
  type MenuOrderDto,
  type SettingDto,
  type CreateSettingDto,
  type UpdateSettingDto,
  type AccessLogInfoDto,
  type AccessLogQueryDto,
} from '@tnzi/core/services/system'
import type { BridgeCrudContract, CrudPageQuery, CrudPageResult } from '../types'
import { mapQueryToListRequest, pageArray } from '../_mappers'

type HttpClient = Parameters<typeof useAdminMenuApi>[0]

export interface SystemBridgeDeps {
  client?: HttpClient
  menuApi?: ReturnType<typeof useAdminMenuApi>
  settingApi?: ReturnType<typeof useAdminSettingApi>
  accessLogApi?: ReturnType<typeof useAdminAccessLogApi>
}

export interface SystemBridge {
  menus: BridgeCrudContract<MenuInfoDto, CreateMenuDto, UpdateMenuDto> & {
    reorder(orders: MenuOrderDto[]): Promise<void>
  }
  /** Settings (shown as "Parameter" in the UI). */
  settings: BridgeCrudContract<SettingDto, CreateSettingDto, UpdateSettingDto>
  /** Access logs — read-only. create/update/delete reject. */
  accessLogs: {
    fetch(query: CrudPageQuery): Promise<CrudPageResult<AccessLogInfoDto>>
  }
  /**
   * Scheduled jobs — Hangfire recurring-job admin, wired via direct HttpClient
   * calls to /admin/scheduled-jobs. The Tnzi.Hangfire module ships
   * DefaultScheduledJobAdminController as of 2026-04-14, but @tnzi/core/services/system
   * has not been regenerated since then, so this path intentionally bypasses
   * the generated factory. Replace with useAdminScheduledJobApi after the next
   * `pnpm contracts:sync`.
   */
  scheduledJobs: {
    fetch(query: CrudPageQuery): Promise<CrudPageResult<ScheduledJobDto>>
    trigger(id: string): Promise<void>
    delete(id: string): Promise<void>
  }
  /**
   * Feature flags — Tnzi.Feature module's FeatureDefinition/Value admin
   * surface. Scaffolded in 0.2.8 (Phase E). Endpoints wired in Phase E
   * backend follow-up.
   */
  features: BridgeCrudContract<FeatureDto, Partial<FeatureDto>, Partial<FeatureDto>>
}

export interface FeatureDto {
  id: string
  name: string
  code: string
  description?: string
  valueType?: 'boolean' | 'string' | 'number' | 'json'
  defaultValue?: string
  isEnabled?: boolean
}

/**
 * Inline ScheduledJobDto mirror of Tnzi.Hangfire.Dtos.ScheduledJobDto.
 * Lives here until contracts:sync regenerates @tnzi/core/services/system.
 * Keep in sync with src/Tnzi.Hangfire/Dtos/ScheduledJobDtos.cs.
 */
export interface ScheduledJobDto {
  id: string
  cron?: string | null
  queue?: string | null
  lastExecution?: string | null
  nextExecution?: string | null
  createdAt?: string | null
  timeZoneId?: string | null
  lastJobId?: string | null
  lastJobState?: string | null
  error?: string | null
  removed: boolean
}

function unwrap<T>(res: T | { data: T; succeeded: boolean }): T {
  if (res && typeof res === 'object' && 'succeeded' in (res as object) && 'data' in (res as object)) {
    return (res as { data: T; succeeded: boolean }).data
  }
  return res as T
}

export function createSystemBridge(deps: SystemBridgeDeps = {}): SystemBridge {
  const menuApi = deps.menuApi ?? (deps.client ? useAdminMenuApi(deps.client) : null)
  const settingApi = deps.settingApi ?? (deps.client ? useAdminSettingApi(deps.client) : null)
  const accessLogApi = deps.accessLogApi ?? (deps.client ? useAdminAccessLogApi(deps.client) : null)

  if (!menuApi || !settingApi || !accessLogApi) {
    const noOp = () => Promise.reject(new Error('createSystemBridge: no deps provided'))
    return {
      menus: {
        fetch: noOp as never,
        create: noOp as never,
        update: noOp as never,
        delete: noOp as never,
        reorder: noOp as never,
      },
      settings: { fetch: noOp as never, create: noOp as never, update: noOp as never, delete: noOp as never },
      accessLogs: { fetch: noOp as never },
      scheduledJobs: {
        fetch: noOp as never,
        trigger: noOp as never,
        delete: noOp as never,
      },
      features: {
        fetch: noOp as never,
        create: noOp as never,
        update: noOp as never,
        delete: noOp as never,
      },
    }
  }

  const menus: SystemBridge['menus'] = {
    fetch: async (query: CrudPageQuery): Promise<CrudPageResult<MenuInfoDto>> => {
      const items = unwrap<MenuInfoDto[]>(await menuApi.getList())
      return pageArray(items, query)
    },
    create: async (data) => unwrap(await menuApi.create(data)) as MenuInfoDto,
    update: async (id, data) => unwrap(await menuApi.update(String(id), data)) as MenuInfoDto,
    delete: async (ids) => {
      await menuApi.batchDelete(ids.map(String))
    },
    reorder: async (orders) => {
      await menuApi.batchUpdateOrders(orders)
    },
  }

  const settings: BridgeCrudContract<SettingDto, CreateSettingDto, UpdateSettingDto> = {
    fetch: async (query: CrudPageQuery): Promise<CrudPageResult<SettingDto>> => {
      const raw = unwrap<SettingDto[]>(await settingApi.getList())
      const source = Array.isArray(raw) ? raw : []
      // Optional group-prefix filter exposed by Parameter/Dictionary pages via
      // crud.setFilters({ groupPrefix }). Empty string means "all groups".
      const groupPrefix = typeof query.filters?.groupPrefix === 'string'
        ? (query.filters.groupPrefix as string).trim()
        : ''
      const filtered = groupPrefix.length > 0
        ? source.filter((s) => (s.group ?? '').startsWith(groupPrefix))
        : source
      return pageArray(filtered, query)
    },
    create: async (data) => unwrap(await settingApi.create(data)) as SettingDto,
    update: async (id, data) => unwrap(await settingApi.update(String(id), data)) as SettingDto,
    delete: async (ids) => {
      await settingApi.batchDelete(ids.map(String))
    },
  }

  const accessLogs = {
    fetch: async (query: CrudPageQuery): Promise<CrudPageResult<AccessLogInfoDto>> => {
      const params = mapQueryToListRequest(query) as unknown as AccessLogQueryDto
      const result = unwrap<{ items: AccessLogInfoDto[]; totalCount: number; pageIndex: number; pageSize: number }>(
        await accessLogApi.getList(params),
      )
      return {
        items: result.items ?? [],
        totalCount: result.totalCount ?? 0,
        pageIndex: result.pageIndex ?? query.pageIndex,
        pageSize: result.pageSize ?? query.pageSize,
      }
    },
  }

  const scheduledJobs: SystemBridge['scheduledJobs'] = {
    fetch: async (query: CrudPageQuery): Promise<CrudPageResult<ScheduledJobDto>> => {
      if (!deps.client) {
        throw new Error('scheduledJobs.fetch: HttpClient (deps.client) is required')
      }
      // TODO(contracts-sync): replace with useAdminScheduledJobApi(deps.client)
      // once `pnpm contracts:sync` regenerates @tnzi/core/services/system with
      // the 2026-04-14 Hangfire admin endpoints.
      const res = await deps.client.get<ScheduledJobDto[]>('/admin/scheduled-jobs')
      const items = unwrap<ScheduledJobDto[]>(res) ?? []
      return pageArray(items, query)
    },
    trigger: async (id: string): Promise<void> => {
      if (!deps.client) {
        throw new Error('scheduledJobs.trigger: HttpClient (deps.client) is required')
      }
      await deps.client.post(`/admin/scheduled-jobs/${encodeURIComponent(id)}/trigger`)
    },
    delete: async (id: string): Promise<void> => {
      if (!deps.client) {
        throw new Error('scheduledJobs.delete: HttpClient (deps.client) is required')
      }
      await deps.client.delete(`/admin/scheduled-jobs/${encodeURIComponent(id)}`)
    },
  }

  const featureStub = <T>(label: string): Promise<T> =>
    Promise.reject(
      new Error(`system.features.${label}: backend endpoint pending (Phase E backend follow-up)`),
    )
  const features: SystemBridge['features'] = {
    fetch: () => featureStub('fetch'),
    create: () => featureStub('create'),
    update: () => featureStub('update'),
    delete: () => featureStub('delete'),
  }

  return { menus, settings, accessLogs, scheduledJobs, features }
}
