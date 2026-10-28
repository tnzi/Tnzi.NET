import type { FormSchemaItem } from '../_shared/form-schema'
import type { FormFooterSwitch } from '../../headless/useFormModal'
import type { SubscriptionPlanDto, SubscriptionPlanWriteDto } from '../../services/bridges/subscription-plan-bridge'

/**
 * Subscription plan catalogue config - aligned with `SubscriptionPlanDto`
 * (Tnzi.Payment.Subscriptions.Dtos.SubscriptionPlanDto).
 *
 * `cycleType` serialises as the member name (global JsonStringEnumConverter):
 * Day / Week / Month / Year / OneTime.
 */

/** 表单里「编辑时锁定」的字段类型：PlanCode 在 PUT 上被后端忽略，可编辑的输入框等于撒谎。 */
export const PLAN_CODE_LOCKED_TYPE = 'plan-code-locked'

/**
 * 包含存储的字段类型：表单里按 GB（2^30 字节）输入，模型里存字节（`includedStorageBytes`）。
 * 空 = 不计量存储（不限量、没有超额费）。
 */
export const STORAGE_GB_TYPE = 'storage-gb'

export const CYCLE_TYPES = ['Day', 'Week', 'Month', 'Year', 'OneTime'] as const
type CycleType = (typeof CYCLE_TYPES)[number]

/** member name → i18n leaf under `payment.subscriptionPlans.cycle.*` */
const CYCLE_KEY: Record<CycleType, string> = {
  Day: 'day',
  Week: 'week',
  Month: 'month',
  Year: 'year',
  OneTime: 'oneTime',
}

type Translate = (key: string, params?: Record<string, unknown>) => string

/**
 * The billing cadence as a reader says it: "per month", "every 3 months",
 * "one-time". A bare "Month" next to a price reads as the plan's name.
 */
export function describeCycle(plan: Pick<SubscriptionPlanDto, 'cycleType' | 'cycleValue'>, t: Translate): string {
  const type = String(plan.cycleType ?? '') as CycleType
  const key = CYCLE_KEY[type]
  if (!key) return type
  if (type === 'OneTime') return t('cycle.oneTime')
  const n = Number(plan.cycleValue ?? 1)
  return n > 1 ? t(`cycle.${key}Many`, { n }) : t(`cycle.${key}One`)
}

/**
 * Active is a state of the plan, not one of its terms: it sits at the dialog footer's left, opposite Confirm, and
 * only when editing (a new plan starts active; an "off" switch on create would read as "this will be created off").
 */
export const planActiveSwitch: FormFooterSwitch = { key: 'isActive', label: 'form.isActive', hint: 'form.isActiveHint' }

const lockedOnEdit = (type: string) => (model: Record<string, unknown>) =>
  model.id ? PLAN_CODE_LOCKED_TYPE : type

const isRecurring = (model: Record<string, unknown>) => model.cycleType !== 'OneTime'
const offersTrial = (model: Record<string, unknown>) => model.allowTrial === true
const metersStorage = (model: Record<string, unknown>) =>
  isRecurring(model) && model.includedStorageBytes !== null && model.includedStorageBytes !== undefined && model.includedStorageBytes !== ''

export const planFormSchema: FormSchemaItem[] = [
  {
    key: 'planCode', labelKey: 'form.planCode', label: 'Plan Code', type: 'text', typeFn: lockedOnEdit('text'),
    required: true, hintKey: 'form.planCodeHint', hintMode: 'popover',
  },
  { key: 'planName', labelKey: 'form.planName', label: 'Plan Name', type: 'text', required: true },
  { key: 'productCode', labelKey: 'form.productCode', label: 'Product Code', type: 'text', hintKey: 'form.productCodeHint', hintMode: 'popover' },
  { key: 'description', labelKey: 'form.description', label: 'Description', type: 'textarea', span: 'full' },
  { key: 'price', labelKey: 'form.price', label: 'Price', type: 'number', min: 0, required: true },
  {
    key: 'currency', labelKey: 'form.currency', label: 'Currency', type: 'text', required: true,
    hintKey: 'form.termsHint', hintMode: 'popover',
  },
  {
    key: 'cycleType', labelKey: 'form.cycleType', label: 'Billing Cycle', type: 'select', required: true,
    options: CYCLE_TYPES.map((v) => ({ value: v, label: v, labelKey: `cycleType.${CYCLE_KEY[v]}` })),
  },
  {
    key: 'cycleValue', labelKey: 'form.cycleValue', label: 'Cycle Length', type: 'number', min: 1, required: true,
    visible: isRecurring, hintKey: 'form.cycleValueHint', hintMode: 'popover',
  },
  { key: 'allowTrial', labelKey: 'form.allowTrial', label: 'Offer a Trial', type: 'switch' },
  { key: 'trialDays', labelKey: 'form.trialDays', label: 'Trial Days', type: 'number', min: 0, visible: offersTrial },
  {
    key: 'trialDiscount', labelKey: 'form.trialDiscount', label: 'Trial Discount', type: 'number', min: 0,
    visible: offersTrial, hintKey: 'form.trialDiscountHint', hintMode: 'popover',
  },
  {
    key: 'includedStorageBytes', labelKey: 'form.includedStorage', label: 'Included Storage (GB)', type: STORAGE_GB_TYPE,
    visible: isRecurring, hintKey: 'form.includedStorageHint', hintMode: 'popover',
  },
  {
    key: 'storageOveragePricePerGbMonth', labelKey: 'form.storageOveragePrice', label: 'Overage per GB-month', type: 'number', min: 0,
    visible: metersStorage, hintKey: 'form.storageOveragePriceHint', hintMode: 'popover',
  },
]

/** What a new plan starts with; the switches must show what the save will send. */
export const planCreateDefaults: Partial<SubscriptionPlanDto> = {
  price: 0,
  // No currency here: the page fills in the deployment's default (Payment:DefaultCurrency) once it has read it,
  // and a blank one is resolved to the same default on the server.
  cycleType: 'Month' as SubscriptionPlanDto['cycleType'],
  cycleValue: 1,
  allowTrial: false,
  trialDays: 0,
  trialDiscount: null,
  includedStorageBytes: null,
  storageOveragePricePerGbMonth: null,
  isActive: true,
}

const text = (v: unknown): string => (typeof v === 'string' ? v.trim() : '')
const optionalText = (v: unknown): string | null => text(v) || null
const num = (v: unknown, fallback = 0): number => {
  const n = typeof v === 'number' ? v : Number(v)
  return Number.isFinite(n) ? n : fallback
}

/**
 * Form model → the full `SubscriptionPlanDto` the backend binds.
 *
 * ★ PUT 逐字段覆盖，所以 `sortOrder` 与 `isActive` 必须原样带回：缺省会把计划排到第 0 位、
 * 把它悄悄停用。`base` 是被编辑的那一行（新建时为 undefined）。
 */
export function toPlanWrite(form: Partial<SubscriptionPlanDto>, base?: Partial<SubscriptionPlanDto>): SubscriptionPlanWriteDto {
  const merged = { ...base, ...form }
  const allowTrial = merged.allowTrial === true
  const discountRaw = merged.trialDiscount
  const hasDiscount = allowTrial && discountRaw !== null && discountRaw !== undefined && String(discountRaw) !== ''
  // `id` only when there is one: an empty string cannot bind to the server's Guid and the whole create fails
  const id = merged.id ? String(merged.id) : undefined
  return {
    ...(id ? { id } : {}),
    planCode: text(merged.planCode),
    productCode: optionalText(merged.productCode),
    planName: text(merged.planName),
    description: optionalText(merged.description),
    price: num(merged.price),
    currency: text(merged.currency).toUpperCase(),
    cycleType: (merged.cycleType ?? 'Month') as SubscriptionPlanDto['cycleType'],
    cycleValue: merged.cycleType === 'OneTime' ? num(merged.cycleValue, 1) || 1 : num(merged.cycleValue, 1),
    allowTrial,
    trialDays: allowTrial ? num(merged.trialDays) : 0,
    // 不提供试用时清掉折扣：留着一个看不见的折扣会在日后重新打开试用时悄悄生效。
    trialDiscount: hasDiscount ? num(discountRaw) : null,
    ...storageTerms(merged),
    sortOrder: num(merged.sortOrder),
    isActive: merged.isActive !== false,
  }
}

/**
 * 计划的存储条款：一次性计划与没设包含量的计划不计量，超额单价随之清空（留着一个看不见的单价，
 * 日后一旦设了包含量就悄悄开始收钱）。
 */
function storageTerms(plan: Partial<SubscriptionPlanDto>): Pick<SubscriptionPlanWriteDto, 'includedStorageBytes' | 'storageOveragePricePerGbMonth'> {
  const raw = plan.includedStorageBytes
  const included = plan.cycleType !== 'OneTime' && raw !== null && raw !== undefined && String(raw) !== '' && num(raw, -1) >= 0
    ? Math.round(num(raw))
    : null
  const priceRaw = plan.storageOveragePricePerGbMonth
  const price = included !== null && priceRaw !== null && priceRaw !== undefined && String(priceRaw) !== '' ? Math.max(0, num(priceRaw)) : null
  return { includedStorageBytes: included, storageOveragePricePerGbMonth: price }
}

/** The next free slot at the end of the catalogue, so a new plan lands last instead of at 0. */
export function nextSortOrder(plans: ReadonlyArray<Pick<SubscriptionPlanDto, 'sortOrder'>>): number {
  return plans.reduce((max, p) => Math.max(max, num(p.sortOrder)), -1) + 1
}

/**
 * The server's refusal, readable. These endpoints answer with the error code
 * as the message (`SUBSCRIPTION_PLAN_CODE_ALREADY_EXISTS`), which tells an
 * operator nothing about what to change. Known codes map to a sentence; any
 * other refusal keeps the server's own message.
 */
export function describePlanError(err: unknown, op: string, t: Translate, deactivating = false): string {
  const e = err as { errorCode?: string; message?: string } | null
  const code = e?.errorCode || e?.message || ''
  switch (code) {
    case 'SUBSCRIPTION_PLAN_CODE_ALREADY_EXISTS':
      return t('errors.codeExists')
    case 'SUBSCRIPTION_TRIAL_DISCOUNT_OUT_OF_RANGE':
      return t('errors.trialDiscountOutOfRange')
    case 'SUBSCRIPTION_PLAN_HAS_ACTIVE_SUBSCRIPTIONS':
      // The server checks a deactivation before a terms change, so a save that switched the plan off was refused for that
      return op === 'delete' || deactivating ? t('errors.hasSubscribersDeactivate') : t('errors.hasSubscribersTerms')
    case 'PAYMENT_INVALID_AMOUNT':
      return t('errors.invalidPricing')
    case 'SUBSCRIPTION_PLAN_NOT_FOUND':
      return t('errors.notFound')
    default:
      return e?.message || String(err)
  }
}
