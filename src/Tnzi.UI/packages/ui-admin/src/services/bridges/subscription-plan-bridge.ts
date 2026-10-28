/**
 * Subscription plan bridge - adapts `@tnzi/core`'s admin subscription API
 * (`useAdminSubscriptionApi`, `/admin/subscriptions/plans*` on
 * `DefaultSubscriptionAdminController` in `Tnzi.Payment.Subscriptions`) to the
 * `useCrudPage` shape the plan catalogue page consumes.
 *
 * 后端事实（页面行为的依据）：
 * - `GET plans?activeOnly=false` 一次返回全部计划（数组，按 SortOrder 升序），无分页无关键字，
 *   所以这里整表读回再交给 `pageArray` 做本地关键字过滤与分页。
 * - `PUT plans/{id}` 逐字段覆盖，**包括 SortOrder 与 IsActive**：更新必须带上整行，
 *   只发表单字段会把排序号写回 0、把计划悄悄停用。`PlanCode` 更新时被忽略（不可改）。
 * - `DELETE plans/{id}` 不是物理删除，而是停用（IsActive=false）；有存量订阅时答 400。
 * - `POST plans/reorder` 收当前可见的顺序，范围外的计划保持原位。
 */
import type { HttpClient } from '@tnzi/core/http'
import {
  useAdminSubscriptionApi,
  type SubscriptionPlanDto,
  type SubscriptionPlanWriteDto,
} from '@tnzi/core/services/payment'
import type { CrudPageQuery, CrudPageResult } from '../types'
import { ensureOk, pageArray, unwrapOk } from '../_mappers'

export type { SubscriptionPlanDto, SubscriptionPlanWriteDto }

export interface SubscriptionPlanBridgeDeps {
  /** Production path: provide HttpClient; the bridge builds the API itself. */
  client?: HttpClient
  /** Test path: inject a mock API directly. */
  adminSubscriptionApi?: ReturnType<typeof useAdminSubscriptionApi>
}

export interface SubscriptionPlanBridge {
  /** Every plan (active and inactive), keyword-filtered and paged in memory. */
  fetch(query: CrudPageQuery): Promise<CrudPageResult<SubscriptionPlanDto>>
  /** `data` without an `id`: the server's Guid cannot bind an empty string (the whole body would fail). */
  create(data: SubscriptionPlanWriteDto): Promise<SubscriptionPlanDto>
  /** Full-row overwrite: `data` must carry every field, `sortOrder` and `isActive` included. */
  update(id: string, data: SubscriptionPlanWriteDto): Promise<SubscriptionPlanDto>
  /** The deployment's default currency, for a new plan's form; null when it cannot be read. */
  getDefaultCurrency(): Promise<string | null>
  /** DELETE on the backend deactivates the plan; it is refused while live subscriptions remain. */
  deactivate(id: string): Promise<void>
  /** Persist a dragged order (ids of the visible plans, in their new order). */
  reorder(ids: string[]): Promise<void>
}

/** Fields the keyword box matches: what the plan card shows. */
const keywordFields = (p: SubscriptionPlanDto) => [p.planCode, p.planName, p.productCode, p.description]

export function createSubscriptionPlanBridge(deps: SubscriptionPlanBridgeDeps = {}): SubscriptionPlanBridge {
  const api = deps.adminSubscriptionApi ?? (deps.client ? useAdminSubscriptionApi(deps.client) : null)

  if (!api) {
    const reject = (name: string) => (): Promise<never> =>
      Promise.reject(new Error(`subscription-plan-bridge: ${name} - no HttpClient / API provided`))
    return {
      fetch: reject('fetch'),
      create: reject('create'),
      getDefaultCurrency: async () => null,
      update: reject('update'),
      deactivate: reject('deactivate'),
      reorder: reject('reorder'),
    }
  }

  return {
    fetch: async (query: CrudPageQuery): Promise<CrudPageResult<SubscriptionPlanDto>> => {
      // 读取失败必须抛出：吞成空数组会让运营以为「一个计划都没有」然后重复建。
      const plans = unwrapOk<SubscriptionPlanDto[] | null>(await api.getPlans(false)) ?? []
      return pageArray(plans, query, keywordFields)
    },
    create: async (data: SubscriptionPlanWriteDto): Promise<SubscriptionPlanDto> => {
      const { id: _ignored, ...body } = data
      return unwrapOk<SubscriptionPlanDto>(await api.createPlan(body))
    },
    update: async (id: string, data: SubscriptionPlanWriteDto): Promise<SubscriptionPlanDto> => {
      // PUT 答空体：成功后回显本次写入的整行。
      ensureOk(await api.updatePlan(id, { ...data, id }))
      return { ...data, id }
    },
    getDefaultCurrency: async (): Promise<string | null> => {
      // 预填值，读不到不拦新建：表单留空提交时服务端同样取默认币种
      try {
        return unwrapOk<{ currency?: string } | null>(await api.getPlanDefaults())?.currency || null
      } catch {
        return null
      }
    },
    deactivate: async (id: string): Promise<void> => {
      ensureOk(await api.deletePlan(id))
    },
    reorder: async (ids: string[]): Promise<void> => {
      ensureOk(await api.reorderPlans({ ids }))
    },
  }
}
