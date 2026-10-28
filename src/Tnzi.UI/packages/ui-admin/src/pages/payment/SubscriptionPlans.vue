<template>
  <TCardPage
    :state="crud"
    mode="page"
    :title="t('title')"
    :title-help="t('help')"
    :translate="t"
    :cols="{ xs: 1, sm: 2, lg: 3, xl: 4 }"
    :search-placeholder="t('search.placeholder')"
    :form-modal-width="720"
    :draggable="canReorder()"
    @reorder="onReorder"
  >
    <template #card="{ item }">
      <TEntityCard
        class="plan-card"
        :class="{ 'plan-card--off': !item.isActive }"
        clickable
        :data-testid="`plan-card-${item.planCode}`"
        @click="openPlan(item)"
      >
        <div class="plan-card__head">
          <div class="plan-card__ident">
            <span class="plan-card__name" :title="item.planName">{{ item.planName }}</span>
            <code class="plan-card__code">{{ item.planCode }}</code>
          </div>
          <TStatusBadge
            :value="item.isActive"
            :mapping="{
              true: { type: 'success', label: t('status.active') },
              false: { type: 'default', label: t('status.inactive') },
            }"
          />
        </div>

        <div class="plan-card__price">
          <span class="plan-card__amount">{{ formatCurrency(Number(item.price ?? 0), item.currency || 'USD') }}</span>
          <span class="plan-card__cycle">{{ describeCycle(item, t) }}</span>
        </div>

        <p class="plan-card__desc">{{ item.description || EMPTY_DASH }}</p>

        <div class="plan-card__foot">
          <span class="plan-card__fact">
            <TSvgIcon icon="mdi:calendar-sync-outline" :size="13" />
            <template v-if="item.allowTrial">
              {{ t('card.trial', { days: item.trialDays }) }}
              <template v-if="item.trialDiscount">
                · {{ t('card.trialDiscount', { amount: formatCurrency(Number(item.trialDiscount), item.currency || 'USD') }) }}
              </template>
            </template>
            <template v-else>{{ t('card.noTrial') }}</template>
          </span>
          <span v-if="item.productCode" class="plan-card__fact" :title="t('form.productCode')">
            <TSvgIcon icon="mdi:cube-outline" :size="13" />
            {{ item.productCode }}
          </span>
          <span v-if="item.includedStorageBytes != null" class="plan-card__fact" data-testid="plan-card-storage">
            <TSvgIcon icon="mdi:database-outline" :size="13" />
            {{ storageFact(item) }}
          </span>
        </div>
      </TEntityCard>
    </template>

    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="planFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
        :translate="t"
        :columns="2"
        :field-renderers="fieldRenderers"
      />
    </template>
  </TCardPage>
</template>

<script setup lang="ts">
/**
 * Subscription plans as cards.
 *
 * A deployment sells a handful of plans and the questions asked of this page
 * are "what do we charge, how often, is there a trial, is it on sale" - one
 * plan at a time, not columns compared across rows. The cards also carry the
 * catalogue order the pricing page reads top to bottom, so dragging a card is
 * how that order is changed.
 *
 * 点卡片即打开计划（能改的人进编辑，否则只读）；启用状态是对话框页脚左侧的开关，只在编辑时出现。
 * 页面不提供删除：后端的删除就是停用，那只开关已经是它唯一的入口。
 *
 * 后端规则（页面据此决定交互，而不是去猜）：
 * - 有存量订阅的计划不能停用；拒绝时说清是「停用」被拒。
 * - 有存量订阅的计划不能改币种 / 周期，只改价允许；拒绝时把原因说清楚，而不是吐一个错误码。
 */
import { h, onMounted } from 'vue'
import { NInput, NInputNumber } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { formatCurrency } from '@tnzi/core'
import { bytesToGb, formatStorageGb, gbToBytes } from '@tnzi/core/services/payment'
import TCardPage from '../../components/crud/TCardPage.vue'
import type { CardReorderPayload } from '../../components/crud/renderers/card-reorder'
import TEntityCard from '../../components/data/TEntityCard.vue'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import { useCrudPage, type CrudPageOp } from '../../headless/useCrudPage'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import {
  createSubscriptionPlanBridge,
  type SubscriptionPlanDto,
} from '../../services/bridges/subscription-plan-bridge'
import { useAdminClient } from '../../plugin/client'
import { EMPTY_DASH } from '../../utils/placeholders'
import TFormSchemaRenderer, { type FieldRenderer } from '../_shared/form-schema'
import { makePageTranslator } from '../_shared/translate'
import { useSafeMessage } from '../_shared/safe-message'
import {
  PLAN_CODE_LOCKED_TYPE,
  STORAGE_GB_TYPE,
  describeCycle,
  describePlanError,
  nextSortOrder,
  planActiveSwitch,
  planCreateDefaults,
  planFormSchema,
  toPlanWrite,
} from './subscription-plan-config'

const t = makePageTranslator('payment.subscriptionPlans')
const message = useSafeMessage()
const { can } = usePermissionGuard()

const bridge = createSubscriptionPlanBridge({ client: useAdminClient() })

/** The row being edited as the list holds it: the form may not carry every field the PUT overwrites. */
function rowOf(id: string): SubscriptionPlanDto | undefined {
  return crud.items.value.find((p) => p.id === id)
}

const WRITE_OPS: ReadonlySet<CrudPageOp> = new Set(['create', 'update'])

// A new plan starts in the deployment's default currency (Payment:DefaultCurrency). Read once; the
// create form reads this object each time it opens, so filling it in later is enough.
const createDefaults: Partial<SubscriptionPlanDto> = { ...planCreateDefaults }
onMounted(async () => {
  const currency = await bridge.getDefaultCurrency()
  if (currency) createDefaults.currency = currency
})

const crud = useCrudPage<SubscriptionPlanDto, string>({
  pageId: 'payment.subscriptionPlans',
  permission: 'payment.subscription',
  columns: [],
  rowKey: (p) => p.id,
  // 计划通常只有几个；一页装下全部，拖拽排序时看到的就是整份目录。
  initialPageSize: 100,
  createDefaults,
  formFooterSwitch: planActiveSwitch,
  fetchData: (query) => bridge.fetch(query),
  createData: (data): Promise<SubscriptionPlanDto> => bridge.create(toPlanWrite({ ...data, sortOrder: nextSortOrder(crud.items.value) })),
  updateData: (id, data): Promise<SubscriptionPlanDto> => bridge.update(id, toPlanWrite(data, rowOf(id))),
  onError: (err, op) => {
    if (!WRITE_OPS.has(op)) return
    message.error(describePlanError(err, op, t, op === 'update' && isDeactivating()))
    return false
  },
})

const canReorder = (): boolean => can('payment.subscription.update')

/** PlanCode cannot change after creation (the PUT ignores it), so the edit form shows it read-only. */
const fieldRenderers: Record<string, FieldRenderer> = {
  [PLAN_CODE_LOCKED_TYPE]: (ctx) => h(NInput, { value: String(ctx.value ?? ''), disabled: true }),
  // 表单里是 GB（2^30 字节），模型里是字节；清空 = 不计量存储
  [STORAGE_GB_TYPE]: (ctx) =>
    h(NInputNumber, {
      value: bytesToGb(ctx.value as number | null | undefined),
      min: 0,
      clearable: true,
      disabled: ctx.readonly,
      placeholder: t('form.includedStoragePlaceholder'),
      style: { width: '100%' },
      'onUpdate:value': (gb: number | null) => ctx.onUpdate(gbToBytes(gb)),
    }),
}

/** `250 GB included · CAD 0.20 / GB-month over`; without a price only the allowance. */
function storageFact(plan: SubscriptionPlanDto): string {
  const included = t('card.storageIncluded', { size: formatStorageGb(plan.includedStorageBytes, 3) })
  const price = Number(plan.storageOveragePricePerGbMonth ?? 0)
  return price > 0
    ? `${included} · ${t('card.storageOverage', { price: formatCurrency(price, plan.currency || 'USD') })}`
    : included
}

/** Clicking a card opens the plan: to edit for whoever may change it, read-only otherwise. */
function openPlan(row: SubscriptionPlanDto): void {
  if (crud.canUpdate) crud.openEdit(row)
  else crud.openView(row)
}

/** The save being refused switched an active plan off (the Active switch in the dialog footer). */
function isDeactivating(): boolean {
  const form = crud.formModal.formData.value as Partial<SubscriptionPlanDto> | null
  const id = form?.id ? String(form.id) : ''
  return form?.isActive === false && rowOf(id)?.isActive === true
}

/**
 * Persist a dragged order. On failure the cards go back: otherwise the screen
 * keeps showing an order the server refused, and the pricing page does not.
 */
async function onReorder(e: CardReorderPayload<SubscriptionPlanDto>): Promise<void> {
  const ids = e.items.map((p) => p.id)
  if (ids.length === 0) return
  try {
    await bridge.reorder(ids)
  } catch (err) {
    e.revert()
    message.error(describePlanError(err, 'update', t))
  }
}

defineExpose({ onReorder, openPlan })
</script>

<style scoped>
.plan-card {
  height: 100%;
}
.plan-card :deep(.n-card__content) {
  display: flex;
  flex-direction: column;
  gap: 10px;
  height: 100%;
}
/* A deactivated plan reads as retired without disappearing from the catalogue. */
.plan-card--off {
  opacity: 0.72;
}
.plan-card__head {
  display: flex;
  align-items: flex-start;
  gap: 10px;
}
.plan-card__ident {
  flex: 1 1 auto;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.plan-card__name {
  font-size: 14.5px;
  font-weight: 600;
  color: var(--tnzi-base-text);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.plan-card__code {
  font-family: var(--tnzi-font-mono, ui-monospace, monospace);
  font-size: 11.5px;
  color: var(--tnzi-base-text-muted);
}
.plan-card__price {
  display: flex;
  align-items: baseline;
  gap: 6px;
  flex-wrap: wrap;
}
.plan-card__amount {
  font-size: 22px;
  font-weight: 600;
  font-variant-numeric: tabular-nums;
  color: var(--tnzi-base-text);
}
.plan-card__cycle {
  font-size: 12.5px;
  color: var(--tnzi-base-text-muted);
}
.plan-card__desc {
  margin: 0;
  font-size: 12.5px;
  line-height: 1.5;
  color: var(--tnzi-base-text-muted);
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
  min-height: 2.6em;
}
.plan-card__foot {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  flex-wrap: wrap;
  margin-top: auto;
  padding-top: 8px;
  border-top: 1px solid var(--tnzi-border);
}
.plan-card__fact {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
}
</style>
