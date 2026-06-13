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
  useAdminSettingsCenterApi,
  type MenuInfoDto,
  type CreateMenuDto,
  type UpdateMenuDto,
  type MenuOrderDto,
  type SettingDto,
  type CreateSettingDto,
  type UpdateSettingDto,
  type AccessLogInfoDto,
  type AccessLogQueryDto,
  type SettingsCenterGroupDto,
} from '@tnzi/core/services/system'
import type { BridgeCrudContract, CrudPageQuery, CrudPageResult } from '../types'
import { mapQueryToListRequest, pageArray, pagedResult, unwrapResult as unwrap } from '../_mappers'

type HttpClient = Parameters<typeof useAdminMenuApi>[0]

export interface SystemBridgeDeps {
  client?: HttpClient
  menuApi?: ReturnType<typeof useAdminMenuApi>
  settingApi?: ReturnType<typeof useAdminSettingApi>
  accessLogApi?: ReturnType<typeof useAdminAccessLogApi>
  settingsCenterApi?: ReturnType<typeof useAdminSettingsCenterApi>
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
  features: BridgeCrudContract<FeatureDto, CreateFeatureDto, UpdateFeatureDto>
  /** Settings center — schema-driven module settings (definitions / save / reset). */
  settingsCenter: {
    getDefinitions(): Promise<SettingsCenterGroupDto[]>
    saveGroup(groupKey: string, changedValues: Record<string, string | null>): Promise<SettingsCenterGroupDto>
    resetGroup(groupKey: string): Promise<SettingsCenterGroupDto>
  }
}

/**
 * Mirror of Tnzi.Feature.Dtos.FeatureDefinitionDto.
 * Kept inline here until `pnpm contracts:sync` regenerates
 * `@tnzi/core/services/system` with the feature endpoints.
 *
 * `valueType` is the int form of `FeatureValueType` enum:
 *   0 = Boolean, 1 = Integer, 2 = String
 *
 * `source` is "Database" (DB-stored, fully editable) or "Code" (defined by
 * `IFeatureDefinitionProvider`, edit/delete disabled in the admin UI).
 */
export interface FeatureDto {
  id: string
  name: string
  displayName?: string | null
  description?: string | null
  defaultValue?: string | null
  valueType: number
  parentName?: string | null
  isEnabled: boolean
  group?: string | null
  source?: string
  isReadOnly?: boolean
}

export interface CreateFeatureDto {
  name: string
  displayName?: string | null
  description?: string | null
  defaultValue?: string | null
  valueType: number
  parentName?: string | null
  group?: string | null
}

export interface UpdateFeatureDto {
  displayName?: string | null
  description?: string | null
  defaultValue?: string | null
  valueType: number
  parentName?: string | null
  isEnabled?: boolean
  group?: string | null
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

export function createSystemBridge(deps: SystemBridgeDeps = {}): SystemBridge {
  const menuApi = deps.menuApi ?? (deps.client ? useAdminMenuApi(deps.client) : null)
  const settingApi = deps.settingApi ?? (deps.client ? useAdminSettingApi(deps.client) : null)
  const accessLogApi = deps.accessLogApi ?? (deps.client ? useAdminAccessLogApi(deps.client) : null)
  const settingsCenterApi = deps.settingsCenterApi ?? (deps.client ? useAdminSettingsCenterApi(deps.client) : null)

  if (!menuApi || !settingApi || !accessLogApi || !settingsCenterApi) {
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
      settingsCenter: {
        getDefinitions: noOp as never,
        saveGroup: noOp as never,
        resetGroup: noOp as never,
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
      return pagedResult({
        items: result.items ?? [],
        totalCount: result.totalCount ?? 0,
        pageIndex: result.pageIndex ?? query.pageIndex,
        pageSize: result.pageSize ?? query.pageSize,
      })
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

  // Wired directly to /admin/feature-definitions (Tnzi.Feature module).
  // Bypasses @tnzi/core's generated factory because contracts:sync hasn't been
  // re-run since DefaultFeatureDefinitionAdminController shipped — same pattern
  // as scheduledJobs above. Replace with useAdminFeatureDefinitionApi once the
  // SDK is regenerated.
  const FEATURES_BASE = '/admin/feature-definitions'
  const features: SystemBridge['features'] = {
    fetch: async (query: CrudPageQuery): Promise<CrudPageResult<FeatureDto>> => {
      if (!deps.client) {
        throw new Error('features.fetch: HttpClient (deps.client) is required')
      }
      const res = await deps.client.get<FeatureDto[]>(FEATURES_BASE)
      const items = unwrap<FeatureDto[]>(res) ?? []
      // Backend returns the full list; client-side filter by keyword on
      // name/displayName/group + paginate locally — feature definition
      // counts are small (typically <100) so this stays cheap.
      const keyword = typeof query.searchText === 'string'
        ? query.searchText.trim().toLowerCase()
        : ''
      const filtered = keyword
        ? items.filter((f) =>
            (f.name ?? '').toLowerCase().includes(keyword) ||
            (f.displayName ?? '').toLowerCase().includes(keyword) ||
            (f.group ?? '').toLowerCase().includes(keyword),
          )
        : items
      return pageArray(filtered, query)
    },
    create: async (data) => {
      if (!deps.client) throw new Error('features.create: HttpClient required')
      const res = await deps.client.post<FeatureDto>(FEATURES_BASE, data)
      return unwrap(res) as FeatureDto
    },
    update: async (id, data) => {
      if (!deps.client) throw new Error('features.update: HttpClient required')
      const res = await deps.client.put<FeatureDto>(`${FEATURES_BASE}/${encodeURIComponent(String(id))}`, data)
      return unwrap(res) as FeatureDto
    },
    delete: async (ids) => {
      if (!deps.client) throw new Error('features.delete: HttpClient required')
      // Backend has no batch endpoint — loop sequentially.
      for (const id of ids) {
        await deps.client.delete(`${FEATURES_BASE}/${encodeURIComponent(String(id))}`)
      }
    },
  }

  const settingsCenter: SystemBridge['settingsCenter'] = {
    getDefinitions: async () =>
      unwrap<SettingsCenterGroupDto[]>(await settingsCenterApi.getDefinitions()),
    saveGroup: async (groupKey, changedValues) =>
      unwrap<SettingsCenterGroupDto>(await settingsCenterApi.saveGroup(groupKey, changedValues)),
    resetGroup: async (groupKey) =>
      unwrap<SettingsCenterGroupDto>(await settingsCenterApi.resetGroup(groupKey)),
  }

  return { menus, settings, accessLogs, scheduledJobs, features, settingsCenter }
}
