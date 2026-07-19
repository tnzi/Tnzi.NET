/**
 * System bridge — full implementation (Phase 3 Task 3.11).
 *
 * Adapts the system backend APIs to BridgeCrudContract + custom method shapes
 * used by all TCrudPage-based system management pages.
 *
 * Sub-contracts:
 *   - menus         → useAdminMenuApi (full CRUD + reorder)
 *   - settings      → useAdminSettingApi (full CRUD; backs both the Parameter
 *                     and Dictionary pages — same /admin/settings endpoint,
 *                     different UI framing)
 *   - accessLogs    → useAdminAccessLogApi (read-only)
 *   - scheduledJobs → live wiring to /admin/scheduled-jobs (Tnzi.Hangfire
 *                     DefaultScheduledJobAdminController). Calls go through the
 *                     HttpClient directly because @tnzi/core/services/system has
 *                     not been regenerated since those endpoints shipped.
 *   - features      → live wiring to /admin/feature-definitions (Tnzi.Feature),
 *                     same direct-HttpClient reason as scheduledJobs.
 *   - settingsCenter→ useAdminSettingsCenterApi (schema-driven module settings)
 */
import {
  useAdminMenuApi,
  useAdminSettingApi,
  useAdminAccessLogApi,
  useAdminSettingsCenterApi,
  useAppearanceApi,
  useAdminAppearanceApi,
  type AdminGlobalThemeDto,
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
import { ensureOk, mapQueryToListRequest, pageArray, pagedResult, unwrapResult as unwrap } from '../_mappers'

type HttpClient = Parameters<typeof useAdminMenuApi>[0]

export interface SystemBridgeDeps {
  client?: HttpClient
  menuApi?: ReturnType<typeof useAdminMenuApi>
  settingApi?: ReturnType<typeof useAdminSettingApi>
  accessLogApi?: ReturnType<typeof useAdminAccessLogApi>
  settingsCenterApi?: ReturnType<typeof useAdminSettingsCenterApi>
  appearanceApi?: ReturnType<typeof useAppearanceApi>
  adminAppearanceApi?: ReturnType<typeof useAdminAppearanceApi>
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
   * Scheduled jobs — Hangfire recurring-job admin, fully wired via direct
   * HttpClient calls to /admin/scheduled-jobs (Tnzi.Hangfire ships
   * DefaultScheduledJobAdminController). This bypasses the generated factory
   * only because @tnzi/core/services/system has not been regenerated since those
   * endpoints shipped; swap to useAdminScheduledJobApi after `pnpm contracts:sync`.
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
  /**
   * Appearance - global admin theme snapshot. `getGlobal` reads the
   * ANONYMOUS endpoint (deployment-level public appearance, so the login page
   * and pre-auth exception pages get it too; theme = null when unset / endpoint
   * missing on older backends); `saveGlobal` / `resetGlobal` hit the admin
   * endpoints (system.appearance.update) and THROW on a failure envelope so
   * callers never mistake a 403 for a saved theme.
   */
  appearance: {
    getGlobal(): Promise<AdminGlobalThemeDto | null>
    saveGlobal(theme: Record<string, unknown>): Promise<AdminGlobalThemeDto>
    resetGlobal(): Promise<void>
  }
}

/**
 * Mirror of Tnzi.Feature.Dtos.FeatureDefinitionDto.
 * Kept inline here until `pnpm contracts:sync` regenerates
 * `@tnzi/core/services/system` with the feature endpoints.
 *
 * `valueType` is the `FeatureValueType` enum, serialized by the backend's global
 * JsonStringEnumConverter as its member name: "Boolean" | "Integer" | "String"
 * (input still accepts the legacy integer too).
 *
 * `source` is "Database" (DB-stored, fully editable) or "Code" (defined by
 * `IFeatureDefinitionProvider`, edit/delete disabled in the admin UI).
 */
export type FeatureValueType = 'Boolean' | 'Integer' | 'String'

export interface FeatureDto {
  id: string
  name: string
  displayName?: string | null
  description?: string | null
  defaultValue?: string | null
  valueType: FeatureValueType
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
  valueType: FeatureValueType
  parentName?: string | null
  group?: string | null
}

export interface UpdateFeatureDto {
  displayName?: string | null
  description?: string | null
  defaultValue?: string | null
  valueType: FeatureValueType
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
      appearance: {
        getGlobal: noOp as never,
        saveGlobal: noOp as never,
        resetGlobal: noOp as never,
      },
    }
  }
  const appearanceApi = deps.appearanceApi ?? (deps.client ? useAppearanceApi(deps.client) : null)
  const adminAppearanceApi = deps.adminAppearanceApi ?? (deps.client ? useAdminAppearanceApi(deps.client) : null)

  const menus: SystemBridge['menus'] = {
    fetch: async (query: CrudPageQuery): Promise<CrudPageResult<MenuInfoDto>> => {
      const items = unwrap<MenuInfoDto[]>(await menuApi.getList())
      return pageArray(items, query)
    },
    create: async (data) => unwrap(await menuApi.create(data)) as MenuInfoDto,
    update: async (id, data) => unwrap(await menuApi.update(String(id), data)) as MenuInfoDto,
    delete: async (ids) => {
      ensureOk(await menuApi.batchDelete(ids.map(String)))
    },
    reorder: async (orders) => {
      ensureOk(await menuApi.batchUpdateOrders(orders))
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
      ensureOk(await settingApi.batchDelete(ids.map(String)))
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
      ensureOk(await deps.client.post(`/admin/scheduled-jobs/${encodeURIComponent(id)}/trigger`))
    },
    delete: async (id: string): Promise<void> => {
      if (!deps.client) {
        throw new Error('scheduledJobs.delete: HttpClient (deps.client) is required')
      }
      ensureOk(await deps.client.delete(`/admin/scheduled-jobs/${encodeURIComponent(id)}`))
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
        ensureOk(await deps.client.delete(`${FEATURES_BASE}/${encodeURIComponent(String(id))}`))
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

  /**
   * Unwrap an ApiResult but THROW on a failure envelope. `unwrapResult`
   * resolves failures to `undefined` (or the envelope itself), which is
   * fine for reads but would let a 403 masquerade as a successful write.
   * Failure detection is delegated to the shared `ensureOk` helper.
   */
  function unwrapOrThrow<T>(res: unknown, fallbackMessage: string): T {
    ensureOk(res, fallbackMessage)
    return unwrap(res as T)
  }

  const appearance: SystemBridge['appearance'] = {
    getGlobal: async () => {
      if (!appearanceApi) throw new Error('appearance.getGlobal: HttpClient required')
      const dto = unwrap<AdminGlobalThemeDto>(await appearanceApi.getAdminTheme())
      // Failure envelopes can resolve to undefined or to the envelope object
      // itself - only a shape with a `theme` key counts as a real payload.
      return dto && typeof dto === 'object' && 'theme' in dto ? dto : null
    },
    saveGlobal: async (theme) => {
      if (!adminAppearanceApi) throw new Error('appearance.saveGlobal: HttpClient required')
      return unwrapOrThrow<AdminGlobalThemeDto>(
        await adminAppearanceApi.saveTheme({ theme }),
        'Failed to save the global theme',
      )
    },
    resetGlobal: async () => {
      if (!adminAppearanceApi) throw new Error('appearance.resetGlobal: HttpClient required')
      unwrapOrThrow<void>(await adminAppearanceApi.resetTheme(), 'Failed to reset the global theme')
    },
  }

  return { menus, settings, accessLogs, scheduledJobs, features, settingsCenter, appearance }
}
