<template>
  <!-- 单一裸主体（筛选条 + 一张表）→ card scroll="fill"：主体白底并撑满。
       裸放在灰底上是规范点名的反模式。ⓘ 的 prop 是 help，不是 titleHelp
       （后者是 TCrudPage 的）——传错名字会静默落进 $attrs。 -->
  <TContentPage :title="tp('title')" :help="tp('help')" card scroll="fill">
    <template #actions>
      <NButton size="small" tertiary :disabled="!ret" @click="copyAll">
        <template #icon><TSvgIcon icon="mdi:content-copy" :size="16" /></template>
        {{ tp('actions.copy') }}
      </NButton>
    </template>

    <div class="flex flex-col gap-12px flex-1 min-h-0 h-full">
      <div class="flex flex-wrap items-center gap-8px">
        <NSelect
          v-model:value="selectedForm"
          size="small"
          class="w-220px max-w-full"
          :options="formOptions"
          :placeholder="tp('form.placeholder')"
          @update:value="load"
        />
        <NDatePicker
          v-model:value="rangeTs"
          type="daterange"
          size="small"
          clearable
          class="w-250px max-w-full"
          @update:value="load"
        />
        <NButton size="small" type="primary" :disabled="!selectedForm" :loading="loading" @click="load">
          {{ tp('actions.run') }}
        </NButton>
      </div>

      <!-- No country pack loaded is a deployment fact, not an error: say what to
           do about it instead of rendering an empty table. -->
      <NAlert v-if="!loadingForms && formOptions.length === 0" type="info" class="flex-none">
        {{ tp('noForms') }}
      </NAlert>

      <NAlert v-else-if="error" type="warning" closable class="flex-none" @close="error = ''">
        {{ error }}
      </NAlert>

      <NSpin :show="loading" class="fin-tax__spin">
        <TEmpty v-if="!ret && !loading && formOptions.length > 0" :text="tp('empty')" />
        <div v-else-if="ret" class="fin-tax__doc flex flex-col gap-12px flex-1 min-h-0 overflow-y-auto max-w-760px">
          <div class="flex flex-wrap items-baseline justify-between gap-12px">
            <h3 class="m-0 text-15px font-600">{{ ret.formName }}</h3>
            <span class="text-12px text-muted">
              {{ formatAccountingDateRange(ret.periodFrom, ret.periodTo) }}
            </span>
          </div>

          <TResponsiveTable
            :columns="lineColumns"
            :data="ret.lines"
            :row-key="rowKey"
            :row-props="rowProps"
            :pagination="false"
            :bordered="false"
            size="small"
            mobile="cards"
          />

          <div class="fin-tax__net flex items-center justify-between gap-12px pt-10px px-10px font-700 max-w-760px">
            <span>{{ tp('netTax') }}</span>
            <TMoney :value="ret.netTax" :currency="ret.currency" tone="auto" />
          </div>

          <p class="m-0 text-12px text-muted">{{ tp('note') }}</p>
        </div>
      </NSpin>
    </div>
  </TContentPage>
</template>

<script setup lang="ts">
/**
 * Tax return figures.
 *
 * The framework stops at the **line amounts**: electronic filing formats change
 * by year and need registration with the tax authority, so producing a filing
 * file belongs to the deployment. What this page guarantees is that the numbers
 * you type into the authority's own form came from the ledger, not a spreadsheet.
 *
 * Which forms appear depends on which country pack is loaded
 * (`Tnzi.Finance.Tax.Ca` ships CRA GST34). None loaded = nothing to file here.
 */
import { computed, onMounted, ref } from 'vue'
import { NAlert, NButton, NDatePicker, NSelect, NSpin } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import TContentPage from '../../components/layout/TContentPage.vue'
import TResponsiveTable from '../../components/data/TResponsiveTable.vue'
import { TEmpty } from '@tnzi/ui'
import TMoney from '../../components/finance/TMoney.vue'
import { useAdminClient } from '../../plugin/client'
import { useTabTitle } from '../../headless/useTabTitle'
import { makePageTranslator } from '../_shared/translate'
import { useSafeMessage } from '../_shared/safe-message'
import { formatAccountingDateRange, formatMoney, tsToIsoDate } from '../../utils/finance-format'
import { buildTaxReturnColumns, taxReturnRowClass } from './tax-return-config'
import {
  createFinanceBridge,
  type TaxReturnDto,
  type TaxReturnLineDto,
} from '../../services/bridges/finance-bridge'

const bridge = createFinanceBridge({ client: useAdminClient() })
const tp = makePageTranslator('finance.taxReturns')
const message = useSafeMessage()
useTabTitle(() => tp('title'))

const formOptions = ref<Array<{ label: string; value: string }>>([])
const selectedForm = ref<string | null>(null)
const rangeTs = ref<[number, number] | null>(null)
const ret = ref<TaxReturnDto | null>(null)
const loading = ref(false)
const loadingForms = ref(true)
const error = ref('')

const lineColumns = computed(() => buildTaxReturnColumns(tp, ret.value?.currency))
const rowKey = (row: TaxReturnLineDto) => row.line
const rowProps = (row: TaxReturnLineDto) => ({ class: taxReturnRowClass(row) })

async function loadForms() {
  loadingForms.value = true
  try {
    const forms = await bridge.taxReturns.forms()
    formOptions.value = forms.map((f) => ({ label: `${f.country} · ${f.formCode}`, value: `${f.country}/${f.formCode}` }))
    if (formOptions.value.length === 1) selectedForm.value = formOptions.value[0]!.value
  } catch {
    formOptions.value = []
  } finally {
    loadingForms.value = false
  }
}

async function load() {
  if (!selectedForm.value || !rangeTs.value) return
  const [country, formCode] = selectedForm.value.split('/')
  error.value = ''
  loading.value = true
  try {
    ret.value = await bridge.taxReturns.get(
      country!,
      formCode!,
      tsToIsoDate(rangeTs.value[0]),
      tsToIsoDate(rangeTs.value[1]),
    )
  } catch (err) {
    error.value = err instanceof Error ? err.message : tp('failed')
    ret.value = null
  } finally {
    loading.value = false
  }
}

/** Copy every line as `code<TAB>amount` - the shape people paste into the portal. */
async function copyAll() {
  if (!ret.value) return
  const text = ret.value.lines
    .map((l) => `${l.line}\t${formatMoney(l.amount, { currency: ret.value!.currency })}`)
    .join('\n')
  try {
    await navigator.clipboard.writeText(text)
    message.success(tp('actions.copied'))
  } catch {
    message.error(tp('actions.copyFailed'))
  }
}

const currentQuarter = computed(() => {
  const now = new Date()
  const q = Math.floor(now.getMonth() / 3)
  return [new Date(now.getFullYear(), q * 3, 1).getTime(), now.getTime()] as [number, number]
})

onMounted(() => {
  rangeTs.value = currentQuarter.value
  void loadForms()
})
</script>

<style scoped>
/* NSpin 渲染两层 block 包裹，会切断 flex 链——unocss 够不到子组件内部，只能 :deep。 */
.fin-tax__spin,
.fin-tax__spin :deep(.n-spin-content) {
  flex: 1 1 auto;
  min-height: 0;
  display: flex;
  flex-direction: column;
}

/* 行次列等宽数字：行号是对照税务机关表格用的，跳动的字宽会让人对错行。 */
.fin-tax__doc :deep(.fin-tax__c-line) { font-variant-numeric: tabular-nums; }

/* 计算行（净税额）不是谁填进去的，着色让它与"要填的框"区分开。
   ★背景必须打在 td 上：naive 的单元格自带背景色，打在 tr 上会被整行盖掉。 */
.fin-tax__doc :deep(.fin-tax__row--calc td),
.fin-tax__doc :deep(article.fin-tax__row--calc) {
  background-color: var(--tnzi-layout-bg, #f7f7fa);
  font-weight: 600;
}

/* 合计线：token 化的语义边框，非布局。 */
.fin-tax__net { border-top: 2px solid var(--tnzi-base-text, #333); }
</style>
