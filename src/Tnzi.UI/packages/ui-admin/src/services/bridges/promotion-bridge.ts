/**
 * Promotion bridge — thin adapter over `@tnzi/core`'s admin promotion API
 * (`useAdminCouponApi`, wrapping `/admin/promotions/*` exposed by
 * `Tnzi.Payment.Controllers.Admin.DefaultPromotionAdminController`).
 *
 * Delegates to the canonical `useAdminCouponApi` factory in
 * `@tnzi/core/services/payment` (getList / getById / create / update /
 * deactivate). This file keeps the bridge's original public surface
 * (`PromotionDto` / `CreatePromotionDto` / `UpdatePromotionDto` /
 * `PromotionQueryDto` re-exports + `createPromotionBridge`) so consuming
 * pages are unaffected.
 *
 * Backend notes (server-side filtering on `/admin/promotions` GET):
 *   • `PromotionQueryDto` exposes promotionCode/type/productType/activeOnly
 *     only — there is no free-text `searchText` filter (the page's search box
 *     is a no-op server-side). The page's `isActive` boolean is mapped to the
 *     backend's `activeOnly` flag (true → only active rows).
 *   • `UpdatePromotionDto` server-side has NO `discountType` / `startTime`
 *     fields, so the page-facing update payload (which carries them for form
 *     convenience) strips them before delegating — sending them would be
 *     silently ignored by the C# DTO.
 */
import type { HttpClient } from '@tnzi/core/http'
import {
  useAdminCouponApi,
  type PromotionDto as CorePromotionDto,
  type CreatePromotionDto as CoreCreatePromotionDto,
  type UpdatePromotionDto as CoreUpdatePromotionDto,
} from '@tnzi/core/services/payment'
import { unwrapResult as unwrap } from '../_mappers'

// Re-export under the original bridge names consumed by pages.
export type PromotionDto = CorePromotionDto
export type CreatePromotionDto = CoreCreatePromotionDto

// Re-export the enums as VALUES so the page can use them at runtime (switch
// cases / form initializers) without importing @tnzi/core/services/* directly
// — pages route through the bridge per the no-restricted-imports gate.
export { DiscountType, PromotionType } from '@tnzi/core/services/payment'

/**
 * Page-facing update payload. Superset of the backend `UpdatePromotionDto`:
 * the promotion edit form also carries `discountType` / `startTime` for UX
 * symmetry with the create form, but the backend update DTO has neither —
 * the bridge strips them before delegating.
 */
export type UpdatePromotionDto = CoreUpdatePromotionDto & {
  discountType?: number
  startTime?: Date | string
}

/** Page-facing query shape (table page: page + active filter + free-text search). */
export interface PromotionQueryDto {
  pageIndex: number
  pageSize: number
  type?: number | null
  isActive?: boolean | null
  searchText?: string | null
}

export interface PromotionBridgeDeps {
  client?: HttpClient
}

export interface PromotionBridge {
  getList(query: PromotionQueryDto): Promise<{ items: PromotionDto[]; totalCount: number; pageIndex: number; pageSize: number }>
  getById(id: string): Promise<PromotionDto | null>
  create(payload: CreatePromotionDto): Promise<PromotionDto | null>
  update(id: string, payload: UpdatePromotionDto): Promise<void>
  deactivate(id: string): Promise<void>
}

export function createPromotionBridge(deps: PromotionBridgeDeps = {}): PromotionBridge {
  const { client } = deps

  if (!client) {
    const noOp = () => Promise.reject(new Error('createPromotionBridge: no HttpClient provided'))
    return {
      getList: noOp as never,
      getById: noOp as never,
      create: noOp as never,
      update: noOp as never,
      deactivate: noOp as never,
    }
  }

  const api = useAdminCouponApi(client)

  return {
    getList: async (query: PromotionQueryDto) => {
      const result = unwrap<{ items: PromotionDto[]; totalCount: number; pageIndex: number; pageSize: number }>(
        await api.getList({
          pageIndex: query.pageIndex,
          pageSize: query.pageSize,
          type: query.type ?? undefined,
          activeOnly: query.isActive === true,
        } as never),
      )
      return {
        items: result.items ?? [],
        totalCount: result.totalCount ?? 0,
        pageIndex: result.pageIndex ?? query.pageIndex,
        pageSize: result.pageSize ?? query.pageSize,
      }
    },
    getById: async (id: string) =>
      unwrap<PromotionDto | null>(await api.getById(id)),
    create: async (payload: CreatePromotionDto) =>
      unwrap<PromotionDto | null>(await api.create(payload)),
    update: async (id: string, payload: UpdatePromotionDto) => {
      // Strip fields the backend `UpdatePromotionDto` does not accept.
      const { discountType: _discountType, startTime: _startTime, ...rest } = payload
      void _discountType
      void _startTime
      await api.update(id, rest)
    },
    deactivate: async (id: string) => {
      await api.deactivate(id)
    },
  }
}
