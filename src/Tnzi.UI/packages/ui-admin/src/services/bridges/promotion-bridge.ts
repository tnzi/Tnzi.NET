/**
 * Promotion bridge — thin adapter over `@tnzi/core`'s admin promotion API
 * (`useAdminCouponApi`, wrapping `/admin/promotions/*` exposed by
 * `Tnzi.Payment.Controllers.Admin.DefaultPromotionAdminController`).
 *
 * Shaped for the standard `useCrudPage` flow: `create` / `update` accept the
 * page's `Partial<PromotionDto>` form model and project it onto the backend
 * `CreatePromotionDto` / `UpdatePromotionDto` by WHITELIST — so the create path
 * naturally drops `isActive` (not a create field) and the update path naturally
 * drops `discountType` / `startTime` / `type` / `promotionCode` (immutable
 * after creation, absent from `UpdatePromotionDto`).
 *
 * Backend note: `/admin/promotions` GET filters on
 * promotionCode/type/productType/activeOnly only — no free-text search. The
 * page's `isActive` boolean maps to the backend `activeOnly` flag.
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
export type UpdatePromotionDto = CoreUpdatePromotionDto

// Re-export the enums as VALUES so the page can use them at runtime (form
// initializers / select options) without importing @tnzi/core/services/*
// directly — pages route through the bridge per the no-restricted-imports gate.
export { DiscountType, PromotionType } from '@tnzi/core/services/payment'

/** Page-facing query shape (table page: page + active filter). */
export interface PromotionQueryDto {
  pageIndex: number
  pageSize: number
  type?: string | number | null
  isActive?: boolean | null
}

export interface PromotionBridgeDeps {
  client?: HttpClient
}

export interface PromotionBridge {
  getList(query: PromotionQueryDto): Promise<{ items: PromotionDto[]; totalCount: number; pageIndex: number; pageSize: number }>
  getById(id: string): Promise<PromotionDto | null>
  create(data: Partial<PromotionDto>): Promise<PromotionDto>
  update(id: string, data: Partial<PromotionDto>): Promise<PromotionDto>
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
    create: async (data: Partial<PromotionDto>) => {
      // Whitelist onto CreatePromotionDto — drops isActive / id / usedCount /
      // isValid that the create endpoint does not accept.
      const body: CoreCreatePromotionDto = {
        promotionCode: String(data.promotionCode ?? ''),
        name: String(data.name ?? ''),
        description: data.description ?? undefined,
        type: data.type as CorePromotionDto['type'],
        discountValue: Number(data.discountValue ?? 0),
        discountType: data.discountType as CorePromotionDto['discountType'],
        maxDiscountAmount: data.maxDiscountAmount ?? undefined,
        minimumOrderAmount: data.minimumOrderAmount ?? undefined,
        productType: data.productType ?? undefined,
        applyScope: data.applyScope ?? undefined,
        startTime: data.startTime ?? undefined,
        endTime: data.endTime ?? undefined,
        totalUsageLimit: data.totalUsageLimit ?? undefined,
        perUserUsageLimit: data.perUserUsageLimit ?? undefined,
        stackable: data.stackable ?? undefined,
        priority: data.priority ?? undefined,
        firstSubscriptionOnly: data.firstSubscriptionOnly ?? undefined,
      }
      return (unwrap<PromotionDto | null>(await api.create(body)) ?? ({} as PromotionDto))
    },
    update: async (id: string, data: Partial<PromotionDto>) => {
      // Whitelist onto UpdatePromotionDto — drops discountType / startTime /
      // type / promotionCode (immutable after creation) + read-only fields.
      const body: CoreUpdatePromotionDto = {
        name: data.name ?? undefined,
        description: data.description ?? undefined,
        discountValue: data.discountValue ?? undefined,
        maxDiscountAmount: data.maxDiscountAmount ?? undefined,
        minimumOrderAmount: data.minimumOrderAmount ?? undefined,
        endTime: data.endTime ?? undefined,
        totalUsageLimit: data.totalUsageLimit ?? undefined,
        perUserUsageLimit: data.perUserUsageLimit ?? undefined,
        stackable: data.stackable ?? undefined,
        priority: data.priority ?? undefined,
        isActive: data.isActive ?? undefined,
      }
      await api.update(id, body)
      return { ...(data as PromotionDto), id }
    },
    deactivate: async (id: string) => {
      await api.deactivate(id)
    },
  }
}
