import { h } from 'vue'
import { formatCurrency, formatDateTime } from '@tnzi/core'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import type { StatusType } from '@tnzi/ui'
import { DiscountType, PromotionType } from '../../services/bridges/promotion-bridge'
import type { PromotionDto } from '../../services/bridges/promotion-bridge'

/**
 * Promotions page config — aligned with `PromotionDto`
 * (Tnzi.Payment.Dtos.PromotionDto). `type` / `discountType` serialise as
 * member-name strings (global JsonStringEnumConverter).
 *
 * Sibling of Promotions.vue (standard useCrudPage + TCrudPage + form-schema);
 * `startTime` / `endTime` use the custom `promoDate` field renderer the page
 * registers (ISO string ⇄ picker timestamp).
 */

// member name → i18n leaf key under `payment.promotions.type.*`
const TYPE_KEY: Record<string, string> = {
  PercentageDiscount: 'percentageDiscount',
  FixedAmountDiscount: 'fixedAmountDiscount',
  FirstSubscription: 'firstSubscription',
  LimitedTime: 'limitedTime',
  ThresholdDiscount: 'thresholdDiscount',
}

function discountLabel(value: number, type: string): string {
  switch (type) {
    case DiscountType.Percentage:
      return `${value}%`
    case DiscountType.Fixed:
      return formatCurrency(Number(value ?? 0), 'USD')
    default:
      return String(value)
  }
}

/** Tri-state validity: valid (live) > active (enabled but out of window) > inactive. */
function stateBadge(r: PromotionDto, t: (k: string) => string) {
  const value = r.isValid ? 'valid' : r.isActive ? 'active' : 'inactive'
  const type: StatusType = r.isValid ? 'success' : r.isActive ? 'warning' : 'default'
  const label = r.isValid ? t('status.valid') : r.isActive ? t('status.active') : t('status.inactive')
  return h(TStatusBadge, { value, type, label })
}

/** Build the Promotions columns (factory → enum/discount labels resolve via `t`). */
export function buildPromotionColumns(t: (key: string) => string): ColumnDef[] {
  return [
    {
      key: 'isActive',
      title: 'cols.isActive',
      width: 100,
      render: (row) => stateBadge(row as unknown as PromotionDto, t),
    },
    {
      key: 'promotionCode',
      title: 'cols.code',
      minWidth: 140,
      render: (row) =>
        h('code', { class: 'tnzi-mono text-12px font-600' }, (row as unknown as PromotionDto).promotionCode),
    },
    { key: 'name', title: 'cols.name', minWidth: 140, ellipsis: { tooltip: true } },
    {
      key: 'type',
      title: 'cols.type',
      width: 130,
      render: (row) => {
        const v = String((row as unknown as PromotionDto).type ?? '')
        return TYPE_KEY[v] ? t(`type.${TYPE_KEY[v]}`) : v || '—'
      },
    },
    {
      key: 'discountValue',
      title: 'cols.discount',
      width: 110,
      align: 'right',
      render: (row) => {
        const r = row as unknown as PromotionDto
        return discountLabel(r.discountValue, String(r.discountType ?? ''))
      },
    },
    {
      key: 'usedCount',
      title: 'cols.usage',
      width: 110,
      align: 'right',
      render: (row) => {
        const r = row as unknown as PromotionDto
        return `${r.usedCount} / ${r.totalUsageLimit ?? '∞'}`
      },
    },
    {
      key: 'startTime',
      title: 'cols.startTime',
      width: 160,
      render: (row) => formatDateTime((row as unknown as PromotionDto).startTime),
    },
    {
      key: 'endTime',
      title: 'cols.endTime',
      width: 160,
      render: (row) => formatDateTime((row as unknown as PromotionDto).endTime),
    },
  ]
}

const typeOptions = [
  { value: PromotionType.PercentageDiscount, label: 'Percentage Discount', labelKey: 'type.percentageDiscount' },
  { value: PromotionType.FixedAmountDiscount, label: 'Fixed Amount', labelKey: 'type.fixedAmountDiscount' },
  { value: PromotionType.FirstSubscription, label: 'First Subscription', labelKey: 'type.firstSubscription' },
  { value: PromotionType.LimitedTime, label: 'Limited Time', labelKey: 'type.limitedTime' },
  { value: PromotionType.ThresholdDiscount, label: 'Threshold Discount', labelKey: 'type.thresholdDiscount' },
]
const discountTypeOptions = [
  { value: DiscountType.Percentage, label: 'Percentage', labelKey: 'discountType.percentage' },
  { value: DiscountType.Fixed, label: 'Fixed Amount', labelKey: 'discountType.fixedAmount' },
]

/**
 * Create/edit form schema. Fields immutable after creation
 * (promotionCode / type / discountType / startTime) are create-only via
 * `visible: (m) => !m.id`; `isActive` is edit-only (`!!m.id`).
 */
export const promotionFormSchema: FormSchemaItem[] = [
  { key: 'promotionCode', labelKey: 'form.promotionCode', label: 'Code', type: 'text', required: true, placeholder: 'WELCOME10', visible: (m) => !m.id },
  { key: 'name', labelKey: 'form.name', label: 'Name', type: 'text', required: true },
  { key: 'description', labelKey: 'form.description', label: 'Description', type: 'textarea', span: 2 },
  { key: 'type', labelKey: 'form.type', label: 'Type', type: 'select', required: true, options: typeOptions, visible: (m) => !m.id },
  { key: 'discountType', labelKey: 'form.discountType', label: 'Discount Type', type: 'select', required: true, options: discountTypeOptions, visible: (m) => !m.id },
  { key: 'discountValue', labelKey: 'form.discountValue', label: 'Discount Value', type: 'number', required: true, min: 0 },
  { key: 'maxDiscountAmount', labelKey: 'form.maxDiscountAmount', label: 'Max Discount', type: 'number', min: 0 },
  { key: 'minimumOrderAmount', labelKey: 'form.minimumOrderAmount', label: 'Min Order Amount', type: 'number', min: 0 },
  { key: 'totalUsageLimit', labelKey: 'form.totalUsageLimit', label: 'Total Usage Limit', type: 'number', min: 1 },
  { key: 'perUserUsageLimit', labelKey: 'form.perUserUsageLimit', label: 'Per-User Limit', type: 'number', min: 1 },
  { key: 'startTime', labelKey: 'form.startTime', label: 'Start Time', type: 'promoDate', visible: (m) => !m.id },
  { key: 'endTime', labelKey: 'form.endTime', label: 'End Time', type: 'promoDate' },
  { key: 'isActive', labelKey: 'form.isActive', label: 'Active', type: 'switch', visible: (m) => !!m.id },
]
