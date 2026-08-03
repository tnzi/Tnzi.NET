<template>
  <TKpiRow cols="1 s:2 m:4">
    <TKpiCard
      :label="t('kpi.outstanding')"
      :value="fmt(totals?.total)"
      :animated="false"
      icon="mdi:scale-balance"
      :to="reportTarget"
    />
    <TKpiCard
      :label="t('kpi.overdue')"
      :value="fmt(overdue)"
      :animated="false"
      icon="mdi:clock-alert-outline"
      :tone="overdue > 0 ? 'error' : 'success'"
      :to="reportTarget"
    />
    <TKpiCard
      :label="t('kpi.current')"
      :value="fmt(totals?.current)"
      :animated="false"
      icon="mdi:calendar-check-outline"
      :to="reportTarget"
    />
    <TKpiCard
      :label="t('kpi.parties')"
      :value="partyCount"
      icon="mdi:account-multiple-outline"
      :to="reportTarget"
    />
  </TKpiRow>
</template>

<script setup lang="ts">
/**
 * 单据列表页的概览条（规范 §5 标准 2「概览区必须回答现在的状态」+ 标准 5「每个数字都能验证」）。
 *
 * ★数字取自**账龄报表**而不是当前页的行合计：列表只有当前页，把当前页加总当成
 * "未清总额"是会被信任、然后被发现是错的那种数字。四张卡都可点，落到报表页对应
 * 的账龄 tab —— 汇总能下钻到构成它的行，才算数得清。
 */
import { computed, onMounted, ref } from 'vue'
import TKpiRow from '../../../components/data/TKpiRow.vue'
import TKpiCard from '../../../components/data/TKpiCard.vue'
import { formatMoney } from '../../../utils/finance-format'
import type { AgingBucketsDto, FinanceBridge } from '../../../services/bridges/finance-bridge'

const props = defineProps<{
  bridge: FinanceBridge
  /** `ar` = 客户欠我们（发票 / 贷项）；`ap` = 我们欠供应商（账单 / 费用）。 */
  kind: 'ar' | 'ap'
  translate: (key: string) => string
}>()

const t = props.translate
const totals = ref<AgingBucketsDto | null>(null)
const partyCount = ref<number | null>(null)
const currency = ref<string | undefined>(undefined)

/** 逾期 = 合计 − 未到期。不写死桶名，账龄分桶已参数化。 */
const overdue = computed(() => (totals.value ? totals.value.total - totals.value.current : 0))

// 按路由名而非路径字面量：`defineAdminApp({ basePath })` 会重写前缀，
// 写死 `/admin/...` 的目标在自定义前缀下会落到 404。
const reportTarget = computed(() => ({
  name: 'finance.reports',
  query: { section: props.kind === 'ar' ? 'ar-aging' : 'ap-aging' },
}))

const fmt = (v?: number | null) => formatMoney(v, { currency: currency.value })

onMounted(async () => {
  try {
    const asOf = new Date().toISOString().slice(0, 10)
    const report = props.kind === 'ar'
      ? await props.bridge.reports.arAging(asOf)
      : await props.bridge.reports.apAging(asOf)
    totals.value = report.totals
    partyCount.value = report.rows.length
    currency.value = report.baseCurrency
  } catch {
    // 概览失败不该拖垮列表——列表本身是这一页的主体。
    totals.value = null
  }
})
</script>
