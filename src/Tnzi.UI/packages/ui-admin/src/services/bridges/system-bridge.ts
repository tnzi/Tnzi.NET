/**
 * System bridge - full implementation (Phase 3 Task 3.11).
 *
 * Adapts the system backend APIs to BridgeCrudContract + custom method shapes
 * used by all TCrudPage-based system management pages.
 *
 * Sub-contracts:
 *   - settings      → useAdminSettingApi (full CRUD; backs both the Parameter
 *                     and Dictionary pages - same /admin/settings endpoint,
 *                     different UI framing)
 *   - accessLogs    → useAdminAccessLogApi (read-only)
 *   - scheduledJobs → live wiring to /admin/scheduled-jobs (Tnzi.Hangfire
 *                     DefaultScheduledJobAdminController). Calls go through the
 *                     HttpClient directly because @tnzi/core/services/system has
 *                     not been regenerated since those endpoints shipped.
 *   - settingsCenter→ useAdminSettingsCenterApi (schema-driven module settings)
 *
 * Feature flags moved to `feature-bridge.ts` (`@tnzi/core/services/feature`)
 * once the module grew a values and a usage surface next to definitions.
 */
import {
  useAdminSettingApi,
  useAdminAccessLogApi,
  useAdminSettingsCenterApi,
  useAppearanceApi,
  useAdminAppearanceApi,
  type AdminGlobalThemeDto,
  type SettingDto,
  type CreateSettingDto,
  type UpdateSettingDto,
  type AccessLogInfoDto,
  type AccessLogQueryDto,
  type AccessLogStatisticsDto,
  type SettingsCenterGroupDto,
  useAdminScheduledJobApi,
  type ScheduledJobDto,
} from '@tnzi/core/services/system'
import type { BridgeCrudContract, CrudPageQuery, CrudPageResult } from '../types'
import { ensureOk, mapQueryToListRequest, pageArray, pagedResult, unwrapResult as unwrap, unwrapOk } from '../_mappers'

type HttpClient = Parameters<typeof useAdminSettingApi>[0]

export interface SystemBridgeDeps {
  client?: HttpClient
  settingApi?: ReturnType<typeof useAdminSettingApi>
  accessLogApi?: ReturnType<typeof useAdminAccessLogApi>
  settingsCenterApi?: ReturnType<typeof useAdminSettingsCenterApi>
  appearanceApi?: ReturnType<typeof useAppearanceApi>
  adminAppearanceApi?: ReturnType<typeof useAdminAppearanceApi>
}

export interface SystemBridge {
  /** Settings (shown as "Parameter" in the UI). */
  settings: BridgeCrudContract<SettingDto, CreateSettingDto, UpdateSettingDto>
  /** Access logs - read-only. create/update/delete reject. */
  accessLogs: {
    fetch(query: CrudPageQuery): Promise<CrudPageResult<AccessLogInfoDto>>
    /**
     * Totals plus `captureEnabled`: capture is opt-in on the backend
     * (`System:AccessLog:Enabled`, default off), so an empty table needs this
     * bit to tell "not capturing" from "no traffic".
     */
    statistics(): Promise<AccessLogStatisticsDto>
  }
  /**
   * Scheduled jobs - Hangfire recurring-job admin, fully wired via direct
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
  /** Settings center - schema-driven module settings (definitions / save / reset). */
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


// ScheduledJobDto now comes from @tnzi/core (re-exported so the scheduled-jobs
// page keeps importing it from this bridge).
export type { ScheduledJobDto } from '@tnzi/core/services/system'

export function createSystemBridge(deps: SystemBridgeDeps = {}): SystemBridge {
  const settingApi = deps.settingApi ?? (deps.client ? useAdminSettingApi(deps.client) : null)
  const accessLogApi = deps.accessLogApi ?? (deps.client ? useAdminAccessLogApi(deps.client) : null)
  const settingsCenterApi = deps.settingsCenterApi ?? (deps.client ? useAdminSettingsCenterApi(deps.client) : null)
  // Hangfire's admin surface; went through hand-written paths until 2026-09-04.
  const jobApi = deps.client ? useAdminScheduledJobApi(deps.client) : null
  const requireJobApi = (op: string) => {
    if (!jobApi) throw new Error(`${op}: HttpClient (deps.client) is required`)
    return jobApi
  }

  if (!settingApi || !accessLogApi || !settingsCenterApi) {
    const noOp = () => Promise.reject(new Error('createSystemBridge: no deps provided'))
    return {
      settings: { fetch: noOp as never, create: noOp as never, update: noOp as never, delete: noOp as never },
      accessLogs: { fetch: noOp as never, statistics: noOp as never },
      scheduledJobs: {
        fetch: noOp as never,
        trigger: noOp as never,
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
    create: async (data) => unwrapOk(await settingApi.create(data)) as SettingDto,
    update: async (id, data) => unwrapOk(await settingApi.update(String(id), data)) as SettingDto,
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
    statistics: async (): Promise<AccessLogStatisticsDto> =>
      unwrapOk<AccessLogStatisticsDto>(await accessLogApi.getStatistics()),
  }

  const scheduledJobs: SystemBridge['scheduledJobs'] = {
    fetch: async (query: CrudPageQuery): Promise<CrudPageResult<ScheduledJobDto>> => {
      const items = unwrap<ScheduledJobDto[]>(await requireJobApi('scheduledJobs.fetch').getList()) ?? []
      return pageArray(items, query)
    },
    trigger: async (id: string): Promise<void> => {
      ensureOk(await requireJobApi('scheduledJobs.trigger').trigger(id))
    },
    delete: async (id: string): Promise<void> => {
      ensureOk(await requireJobApi('scheduledJobs.delete').delete(id))
    },
  }

  const settingsCenter: SystemBridge['settingsCenter'] = {
    getDefinitions: async () =>
      unwrap<SettingsCenterGroupDto[]>(await settingsCenterApi.getDefinitions()),
    saveGroup: async (groupKey, changedValues) =>
      unwrapOk<SettingsCenterGroupDto>(await settingsCenterApi.saveGroup(groupKey, changedValues)),
    resetGroup: async (groupKey) =>
      unwrapOk<SettingsCenterGroupDto>(await settingsCenterApi.resetGroup(groupKey)),
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

  // Themes are stored per front-end product; this bridge always speaks for the
  // admin console. The chat app addresses its own scope through the same
  // endpoints - which is the whole point of scoping them.
  const ADMIN_THEME_SCOPE = 'admin'

  const appearance: SystemBridge['appearance'] = {
    getGlobal: async () => {
      if (!appearanceApi) throw new Error('appearance.getGlobal: HttpClient required')
      const dto = unwrap<AdminGlobalThemeDto>(await appearanceApi.getTheme(ADMIN_THEME_SCOPE))
      // Failure envelopes can resolve to undefined or to the envelope object
      // itself - only a shape with a `theme` key counts as a real payload.
      return dto && typeof dto === 'object' && 'theme' in dto ? dto : null
    },
    saveGlobal: async (theme) => {
      if (!adminAppearanceApi) throw new Error('appearance.saveGlobal: HttpClient required')
      return unwrapOrThrow<AdminGlobalThemeDto>(
        await adminAppearanceApi.saveTheme(ADMIN_THEME_SCOPE, { theme }),
        'Failed to save the global theme',
      )
    },
    resetGlobal: async () => {
      if (!adminAppearanceApi) throw new Error('appearance.resetGlobal: HttpClient required')
      unwrapOrThrow<void>(
        await adminAppearanceApi.resetTheme(ADMIN_THEME_SCOPE),
        'Failed to reset the global theme',
      )
    },
  }

  return { settings, accessLogs, scheduledJobs, settingsCenter, appearance }
}
