/**
 * Promotion bridge — wraps `/admin/promotions/*` exposed by
 * `Tnzi.Payment.Controllers.Admin.DefaultPromotionAdminController`.
 *
 * Standard CRUD + lifecycle ops (deactivate / sync-to-stripe).
 */
import type { HttpClient } from '@tnzi/core/http'

// Mirror of Tnzi.Payment enums (numeric backing).
// PromotionType: 0=Coupon, 1=AutoApply, 2=ReferralBonus, 3=Trial
// DiscountType:  0=Percentage, 1=FixedAmount, 2=FreeTrialDays
// ProductType:   0=All, 1=Subscription, 2=OneTime
// ApplyScope:    0=Global, 1=SpecificPlans, 2=SpecificProducts

export interface PromotionDto {
  id: string
  promotionCode: string
  name: string
  description?: string | null
  type: number
  discountValue: number
  discountType: number
  maxDiscountAmount?: number | null
  minimumOrderAmount?: number | null
  productType: number
  applyScope: number
  startTime: string
  endTime?: string | null
  totalUsageLimit?: number | null
  usedCount: number
  perUserUsageLimit?: number | null
  stackable: boolean
  priority: number
  isActive: boolean
  firstSubscriptionOnly: boolean
  isValid: boolean
}

export interface CreatePromotionDto {
  promotionCode: string
  name: string
  description?: string | null
  type: number
  discountValue: number
  discountType: number
  maxDiscountAmount?: number | null
  minimumOrderAmount?: number | null
  productType?: number
  applyScope?: number
  startTime: string
  endTime?: string | null
  totalUsageLimit?: number | null
  perUserUsageLimit?: number | null
  stackable?: boolean
  priority?: number
  firstSubscriptionOnly?: boolean
}

export interface UpdatePromotionDto extends Partial<CreatePromotionDto> {
  isActive?: boolean
}

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

function unwrap<T>(res: T | { data?: T | null }): T {
  if (res && typeof res === 'object' && 'data' in (res as object) && (res as { data?: unknown }).data != null) {
    return (res as { data: T }).data
  }
  return res as T
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

  return {
    getList: async (query: PromotionQueryDto) => {
      const params = new URLSearchParams({
        pageIndex: String(query.pageIndex),
        pageSize: String(query.pageSize),
      })
      if (query.type != null) params.set('type', String(query.type))
      if (query.isActive != null) params.set('isActive', String(query.isActive))
      if (query.searchText) params.set('searchText', query.searchText)
      const result = unwrap<{ items: PromotionDto[]; totalCount: number; pageIndex: number; pageSize: number }>(
        await client.get(`/admin/promotions?${params.toString()}`),
      )
      return {
        items: result.items ?? [],
        totalCount: result.totalCount ?? 0,
        pageIndex: result.pageIndex ?? query.pageIndex,
        pageSize: result.pageSize ?? query.pageSize,
      }
    },
    getById: async (id: string) =>
      unwrap<PromotionDto | null>(await client.get<PromotionDto>(`/admin/promotions/${encodeURIComponent(id)}`)),
    create: async (payload: CreatePromotionDto) =>
      unwrap<PromotionDto | null>(await client.post<PromotionDto>('/admin/promotions', payload)),
    update: async (id: string, payload: UpdatePromotionDto) => {
      await client.put(`/admin/promotions/${encodeURIComponent(id)}`, payload)
    },
    deactivate: async (id: string) => {
      await client.post(`/admin/promotions/${encodeURIComponent(id)}/deactivate`)
    },
  }
}
