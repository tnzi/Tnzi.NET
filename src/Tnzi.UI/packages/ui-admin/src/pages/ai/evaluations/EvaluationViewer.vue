<template>
  <TContentPage
    :title="t('title')"
    :translate="t"
    scroll="fill"
    card
  >
    <template #actions>
      <NButton size="small" @click="refresh">{{ t('refresh') }}</NButton>
      <NButton size="small" data-test="open-trend" @click="openTrendDrawer">{{ t('scoreTrend') }}</NButton>
      <NButton size="small" data-test="open-compare" @click="openCompareDrawer">{{ t('versionCompare') }}</NButton>
      <NButton size="small" data-test="open-batch" @click="openBatchDrawer">{{ t('runBatch') }}</NButton>
      <NButton size="small" type="primary" @click="openNewRunModal">
        {{ t('newRun') }}
      </NButton>
    </template>

    <template #default>
      <p class="t-eval-page__note">{{ t('note') }}</p>

      <TMasterDetailLayout :master-width="320">
        <!-- Left: runs list (selectable for diff) -->
        <template #master>
          <div class="t-eval-page__list-header">
            <span>{{ t('list.heading') }}</span>
            <span class="t-eval-page__hint">{{ t('list.diffHint') }}</span>
          </div>
          <NSpin :show="listLoading">
            <div v-if="!runs.length && !listLoading" class="t-eval-page__empty">
              {{ t('empty') }}
            </div>
            <ul v-else class="t-eval-page__run-list">
              <li
                v-for="run in runs"
                :key="run.id"
                class="t-eval-page__run-item"
                :class="{
                  'is-left': run.id === leftId,
                  'is-right': run.id === rightId,
                  'is-selected': run.id === leftId || run.id === rightId,
                }"
                @click="onPickRun(run.id)"
              >
                <div class="t-eval-page__run-row">
                  <NTag size="small" :type="statusTypeFor(run.status)" :bordered="false">
                    {{ statusLabel(run.status) }}
                  </NTag>
                  <span class="t-eval-page__score" :data-pass="passRatio(run) >= 0.5">
                    {{ (passRatio(run) * 100).toFixed(0) }}%
                  </span>
                </div>
                <div class="t-eval-page__run-meta">
                  <code>{{ shortId(run.id) }}</code>
                  <span>{{ run.caseCount }} cases · avg {{ run.averageScore.toFixed(2) }}</span>
                </div>
                <div class="t-eval-page__run-time">{{ formatTime(run.creationTime) }}</div>
              </li>
            </ul>
          </NSpin>
        </template>

        <!-- Right: detail / diff -->
        <template #detail>
          <div v-if="!leftId && !rightId" class="t-eval-page__placeholder">
            {{ t('selectPrompt') }}
          </div>
          <div v-else class="t-eval-page__diff">
            <div class="t-eval-page__diff-col">
              <header class="t-eval-page__diff-head">
                <span class="t-eval-page__col-label is-left">A</span>
                <div v-if="leftDetail">
                  <strong>{{ shortId(leftDetail.id) }}</strong>
                  <span class="t-eval-page__col-score">
                    {{ (passRatio(leftDetail) * 100).toFixed(0) }}%
                    · avg {{ leftDetail.averageScore.toFixed(2) }}
                  </span>
                </div>
                <div v-else class="t-eval-page__placeholder">{{ t('pickLeft') }}</div>
              </header>
              <pre v-if="leftDetail?.resultsJson" class="t-eval-page__results">{{ formatJson(leftDetail.resultsJson) }}</pre>
            </div>
            <div class="t-eval-page__diff-col">
              <header class="t-eval-page__diff-head">
                <span class="t-eval-page__col-label is-right">B</span>
                <div v-if="rightDetail">
                  <strong>{{ shortId(rightDetail.id) }}</strong>
                  <span class="t-eval-page__col-score">
                    {{ (passRatio(rightDetail) * 100).toFixed(0) }}%
                    · avg {{ rightDetail.averageScore.toFixed(2) }}
                  </span>
                </div>
                <div v-else class="t-eval-page__placeholder">{{ t('pickRight') }}</div>
              </header>
              <pre v-if="rightDetail?.resultsJson" class="t-eval-page__results">{{ formatJson(rightDetail.resultsJson) }}</pre>
            </div>
          </div>
        </template>
      </TMasterDetailLayout>

      <!-- New run modal - uses TFormModal so width auto-adapts on
           narrow viewports (Phase 1 responsive). -->
      <TFormModal
        :state="newRunModal as unknown as UseFormModalReturn<unknown>"
        :title="t('newRun')"
        :width="560"
        :translate="t"
        @submit="submitNewRun"
      >
        <template #default="{ formData }">
          <NForm v-if="formData" label-placement="left" label-width="120px">
            <NFormItem :label="t('form.agentId')" required>
              <NInput
                :value="(formData as NewRunForm).agentId"
                :placeholder="t('admin.shared.placeholder.guid')"
                @update:value="(v: string) => ((formData as NewRunForm).agentId = v)"
              />
            </NFormItem>
            <NFormItem :label="t('form.versionNumber')">
              <NInputNumber
                :value="(formData as NewRunForm).versionNumber"
                :min="1"
                clearable
                @update:value="(v: number | null) => ((formData as NewRunForm).versionNumber = v)"
              />
            </NFormItem>
            <NFormItem :label="t('form.caseInput')" required>
              <NInput
                :value="(formData as NewRunForm).caseInput"
                type="textarea"
                :rows="3"
                @update:value="(v: string) => ((formData as NewRunForm).caseInput = v)"
              />
            </NFormItem>
            <NFormItem :label="t('form.caseExpected')">
              <NInput
                :value="(formData as NewRunForm).caseExpected"
                type="textarea"
                :rows="2"
                @update:value="(v: string) => ((formData as NewRunForm).caseExpected = v)"
              />
            </NFormItem>
          </NForm>
        </template>
        <template #footer>
          <div class="t-eval-page__modal-footer">
            <NButton @click="newRunModal.close">{{ t('form.cancel') }}</NButton>
            <NButton
              type="primary"
              :loading="creating"
              :disabled="!newRunFormValid"
              @click="submitNewRun"
            >
              {{ t('form.runNow') }}
            </NButton>
          </div>
        </template>
      </TFormModal>

      <!-- ===================== Batch evaluation editor ===================== -->
      <TOverlayTheme>
      <NDrawer v-model:show="batchVisible" :width="batchDrawerWidth" placement="right">
        <NDrawerContent :title="t('batch.title')" closable data-test="batch-drawer">
          <p class="t-eval-page__note">{{ t('batch.note') }}</p>

          <!-- Targets: each target is an agent (+ optional version). -->
          <section class="t-eval-page__section">
            <header class="t-eval-page__section-head">
              <span>{{ t('batch.targets') }}</span>
              <NButton size="small" data-test="batch-add-target" @click="addBatchTarget">
                {{ t('batch.addTarget') }}
              </NButton>
            </header>
            <div
              v-for="(target, i) in batchTargets"
              :key="`target-${i}`"
              class="t-eval-page__row"
            >
              <NSelect
                class="t-eval-page__row-grow"
                size="small"
                filterable
                clearable
                :value="target.agentId || null"
                :options="agentOptions"
                :loading="agentsLoading"
                :placeholder="t('batch.agentPlaceholder')"
                @update:value="(v: string | null) => (target.agentId = v ?? '')"
              />
              <NInputNumber
                class="w-120px"
                size="small"
                :value="target.versionNumber"
                :min="1"
                clearable
                :placeholder="t('form.versionNumber')"
                @update:value="(v: number | null) => (target.versionNumber = v)"
              />
              <NButton
                size="small"
                quaternary
                type="error"
                :disabled="batchTargets.length <= 1"
                @click="removeBatchTarget(i)"
              >
                {{ t('batch.remove') }}
              </NButton>
            </div>
          </section>

          <!-- Cases: shared across all targets. -->
          <section class="t-eval-page__section">
            <header class="t-eval-page__section-head">
              <span>{{ t('batch.cases') }}</span>
              <NButton size="small" data-test="batch-add-case" @click="addBatchCase">
                {{ t('batch.addCase') }}
              </NButton>
            </header>
            <div
              v-for="(c, i) in batchCases"
              :key="`case-${i}`"
              class="t-eval-page__case"
            >
              <div class="t-eval-page__case-head">
                <span class="t-eval-page__case-index">#{{ i + 1 }}</span>
                <NButton
                  size="small"
                  quaternary
                  type="error"
                  :disabled="batchCases.length <= 1"
                  @click="removeBatchCase(i)"
                >
                  {{ t('batch.remove') }}
                </NButton>
              </div>
              <NInput
                v-model:value="c.input"
                type="textarea"
                :rows="2"
                :placeholder="t('form.caseInput')"
              />
              <NInput
                v-model:value="c.expectedOutput"
                type="textarea"
                :rows="2"
                :placeholder="t('form.caseExpected')"
              />
            </div>
          </section>

          <!-- Per-target results after the batch resolves. -->
          <section v-if="batchResults.length" class="t-eval-page__section" data-test="batch-results">
            <header class="t-eval-page__section-head">
              <span>{{ t('batch.results') }}</span>
              <span class="t-eval-page__hint">{{ batchSummary }}</span>
            </header>
            <ul class="t-eval-page__result-list">
              <li
                v-for="(r, i) in batchResults"
                :key="`result-${r.id}-${i}`"
                class="t-eval-page__result-item"
              >
                <div class="t-eval-page__result-row">
                  <NTag size="small" :type="statusTypeFor(r.status)" :bordered="false">
                    {{ statusLabel(r.status) }}
                  </NTag>
                  <code>{{ agentLabel(r.agentId) }}</code>
                  <span class="t-eval-page__score" :data-pass="passRatio(r) >= 0.5">
                    {{ r.passedCount }}/{{ r.caseCount }}
                  </span>
                </div>
                <div class="t-eval-page__result-meta">
                  {{ t('batch.resultMeta', {
                    passed: r.passedCount,
                    total: r.caseCount,
                    score: r.averageScore.toFixed(2),
                  }) }}
                </div>
              </li>
            </ul>
          </section>

          <template #footer>
            <NButton @click="batchVisible = false">{{ t('form.cancel') }}</NButton>
            <NButton
              type="primary"
              :loading="batchRunning"
              :disabled="!batchFormValid"
              data-test="batch-run"
              @click="runBatchEval"
            >
              {{ t('batch.run') }}
            </NButton>
          </template>
        </NDrawerContent>
      </NDrawer>
      </TOverlayTheme>

      <!-- ===================== Score trend ===================== -->
      <TOverlayTheme>
      <NDrawer v-model:show="trendVisible" :width="trendDrawerWidth" placement="right">
        <NDrawerContent :title="t('trend.title')" closable data-test="trend-drawer">
          <p class="t-eval-page__note">{{ t('trend.note') }}</p>
          <div class="t-eval-page__row">
            <NSelect
              class="t-eval-page__row-grow"
              size="small"
              filterable
              clearable
              :value="trendAgentId || null"
              :options="agentOptions"
              :loading="agentsLoading"
              :placeholder="t('trend.agentPlaceholder')"
              @update:value="(v: string | null) => (trendAgentId = v ?? '')"
            />
            <NInputNumber
              class="w-140px"
              size="small"
              :value="trendLastN"
              :min="1"
              :max="100"
              :placeholder="t('trend.lastN')"
              @update:value="(v: number | null) => (trendLastN = v)"
            />
            <NButton
              size="small"
              type="primary"
              :loading="trendLoading"
              :disabled="!trendAgentId"
              data-test="trend-load"
              @click="loadTrend"
            >
              {{ t('trend.load') }}
            </NButton>
          </div>

          <NSpin :show="trendLoading">
            <div v-if="!trendPoints.length" class="t-eval-page__empty">
              {{ t('trend.empty') }}
            </div>
            <template v-else>
              <!-- Chart when echarts is usable; falls back to a progress-bar
                   list (still shows exact numbers) in jsdom / SSR. -->
              <TChartPanel :option="trendOption" :height="260" />
              <ul class="t-eval-page__trend-list" data-test="trend-points">
                <li
                  v-for="p in trendPoints"
                  :key="p.runId"
                  class="t-eval-page__trend-item"
                >
                  <div class="t-eval-page__trend-row">
                    <span class="t-eval-page__trend-date">{{ formatTime(p.date) }}</span>
                    <span class="t-eval-page__trend-vals">
                      {{ t('trend.point', {
                        score: (p.score * 100).toFixed(0),
                        pass: (p.passRate * 100).toFixed(0),
                      }) }}
                    </span>
                  </div>
                  <div class="t-eval-page__bars">
                    <div class="t-eval-page__bar">
                      <span class="t-eval-page__bar-fill is-score" :style="barWidth(p.score)" />
                    </div>
                    <div class="t-eval-page__bar">
                      <span class="t-eval-page__bar-fill is-pass" :style="barWidth(p.passRate)" />
                    </div>
                  </div>
                </li>
              </ul>
            </template>
          </NSpin>
        </NDrawerContent>
      </NDrawer>
      </TOverlayTheme>

      <!-- ===================== Version comparison ===================== -->
      <TOverlayTheme>
      <NDrawer v-model:show="compareVisible" :width="compareDrawerWidth" placement="right">
        <NDrawerContent :title="t('compare.title')" closable data-test="compare-drawer">
          <p class="t-eval-page__note">{{ t('compare.note') }}</p>
          <div class="t-eval-page__row">
            <NSelect
              class="t-eval-page__row-grow"
              size="small"
              filterable
              clearable
              :value="compareAgentId || null"
              :options="agentOptions"
              :loading="agentsLoading"
              :placeholder="t('compare.agentPlaceholder')"
              @update:value="(v: string | null) => (compareAgentId = v ?? '')"
            />
            <NInputNumber
              class="w-120px"
              size="small"
              :value="compareVersionA"
              :min="1"
              :placeholder="t('compare.versionA')"
              @update:value="(v: number | null) => (compareVersionA = v)"
            />
            <NInputNumber
              class="w-120px"
              size="small"
              :value="compareVersionB"
              :min="1"
              :placeholder="t('compare.versionB')"
              @update:value="(v: number | null) => (compareVersionB = v)"
            />
            <NButton
              size="small"
              type="primary"
              :loading="compareLoading"
              :disabled="!compareFormValid"
              data-test="compare-load"
              @click="loadComparison"
            >
              {{ t('compare.load') }}
            </NButton>
          </div>

          <NSpin :show="compareLoading">
            <div v-if="!comparison" class="t-eval-page__empty">
              {{ t('compare.empty') }}
            </div>
            <template v-else>
              <div class="t-eval-page__compare" data-test="compare-result">
                <div
                  class="t-eval-page__compare-col"
                  :class="{ 'is-winner': comparison.winner === comparison.versionA.versionNumber }"
                >
                  <header class="t-eval-page__compare-head">
                    {{ t('compare.versionLabel', { v: comparison.versionA.versionNumber }) }}
                    <NTag
                      v-if="comparison.winner === comparison.versionA.versionNumber"
                      size="small"
                      type="success"
                      :bordered="false"
                    >
                      {{ t('compare.winner') }}
                    </NTag>
                  </header>
                  <dl class="t-eval-page__stats">
                    <div><dt>{{ t('compare.averageScore') }}</dt><dd>{{ comparison.versionA.averageScore.toFixed(3) }}</dd></div>
                    <div><dt>{{ t('compare.averagePassRate') }}</dt><dd>{{ (comparison.versionA.averagePassRate * 100).toFixed(0) }}%</dd></div>
                    <div><dt>{{ t('compare.runCount') }}</dt><dd>{{ comparison.versionA.runCount }}</dd></div>
                    <div><dt>{{ t('compare.cases') }}</dt><dd>{{ comparison.versionA.totalPassed }}/{{ comparison.versionA.totalCases }}</dd></div>
                  </dl>
                </div>
                <div
                  class="t-eval-page__compare-col"
                  :class="{ 'is-winner': comparison.winner === comparison.versionB.versionNumber }"
                >
                  <header class="t-eval-page__compare-head">
                    {{ t('compare.versionLabel', { v: comparison.versionB.versionNumber }) }}
                    <NTag
                      v-if="comparison.winner === comparison.versionB.versionNumber"
                      size="small"
                      type="success"
                      :bordered="false"
                    >
                      {{ t('compare.winner') }}
                    </NTag>
                  </header>
                  <dl class="t-eval-page__stats">
                    <div><dt>{{ t('compare.averageScore') }}</dt><dd>{{ comparison.versionB.averageScore.toFixed(3) }}</dd></div>
                    <div><dt>{{ t('compare.averagePassRate') }}</dt><dd>{{ (comparison.versionB.averagePassRate * 100).toFixed(0) }}%</dd></div>
                    <div><dt>{{ t('compare.runCount') }}</dt><dd>{{ comparison.versionB.runCount }}</dd></div>
                    <div><dt>{{ t('compare.cases') }}</dt><dd>{{ comparison.versionB.totalPassed }}/{{ comparison.versionB.totalCases }}</dd></div>
                  </dl>
                </div>
              </div>
              <div class="t-eval-page__delta" :data-positive="comparison.scoreDelta >= 0">
                {{ t('compare.delta', { delta: signedDelta(comparison.scoreDelta) }) }}
              </div>
            </template>
          </NSpin>
        </NDrawerContent>
      </NDrawer>
      </TOverlayTheme>
    </template>
  </TContentPage>
</template>

<script setup lang="ts">
import { formatDateTime } from '@tnzi/core'
import { computed, reactive, ref, watch, onMounted } from 'vue'
import type { CSSProperties } from 'vue'
import {
  NButton, NSpin, NTag, NForm, NFormItem, NInput, NInputNumber,
  NDrawer, NDrawerContent, NSelect,
} from 'naive-ui'
import type { EChartsOption } from 'echarts'
import { useSafeMessage } from '../../_shared/safe-message'
import { useFormModal, type UseFormModalReturn } from '../../../headless/useFormModal'
import { useBreakpoint } from '../../../headless/useBreakpoint'
import TContentPage from '../../../components/layout/TContentPage.vue'
import TMasterDetailLayout from '../../../components/layout/TMasterDetailLayout.vue'
import TFormModal from '../../../components/crud/TFormModal.vue'
import TChartPanel from '../../../components/display/TChartPanel.vue'
import { TOverlayTheme } from '../../../components/overlay'
import { createAiBridge } from '../../../services/bridges/ai-bridge'
import { useAdminClient } from '../../../plugin/client'
import { makePageTranslator } from '../../_shared/translate'
import type {
  AgentDto,
  CreateEvaluationRunDto,
  EvaluationRunDto,
  EvaluationRunDetailDto,
  BatchEvaluationDto,
  BatchEvaluationTargetDto,
  EvaluationCaseDto,
  EvaluationTrendPointDto,
  VersionComparisonDto,
} from '@tnzi/core/services/ai'

interface NewRunForm {
  agentId: string
  versionNumber: number | null
  caseInput: string
  caseExpected: string
}

/** Mutable editor row for a batch target. */
interface BatchTargetRow {
  agentId: string
  versionNumber: number | null
}

/** Mutable editor row for a case (input + expected). */
interface CaseRow {
  input: string
  expectedOutput: string
}

const bridge = createAiBridge({ client: useAdminClient() })
const t = makePageTranslator('ai.evaluations')

const message = useSafeMessage()
const { isSm } = useBreakpoint()

const runs = ref<EvaluationRunDto[]>([])
const listLoading = ref(false)
const leftId = ref<string | null>(null)
const rightId = ref<string | null>(null)
const leftDetail = ref<EvaluationRunDetailDto | null>(null)
const rightDetail = ref<EvaluationRunDetailDto | null>(null)
const creating = ref(false)

// ---- agents (shared picker source for batch/trend/compare) ---------------
const agents = ref<AgentDto[]>([])
const agentsLoading = ref(false)
const agentOptions = computed(() =>
  agents.value.map((a) => ({ label: a.name, value: a.id })),
)
function agentLabel(id: string): string {
  return agents.value.find((a) => a.id === id)?.name ?? shortId(id)
}

async function loadAgents(): Promise<void> {
  if (agents.value.length || agentsLoading.value) return
  agentsLoading.value = true
  try {
    const result = await bridge.agents.fetch({
      pageIndex: 1,
      pageSize: 100,
      sortField: 'name',
      sortOrder: 'asc',
      searchText: '',
      filters: {},
    })
    agents.value = result.items
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  } finally {
    agentsLoading.value = false
  }
}

// Responsive drawer widths - full-width on phones, fixed on desktop.
const batchDrawerWidth = computed(() => (isSm.value ? '100%' : 640))
const trendDrawerWidth = computed(() => (isSm.value ? '100%' : 560))
const compareDrawerWidth = computed(() => (isSm.value ? '100%' : 620))

// ---- new single run modal ------------------------------------------------
const newRunModal = useFormModal<NewRunForm>()

function openNewRunModal(): void {
  newRunModal.open('create', {
    agentId: '',
    versionNumber: null,
    caseInput: '',
    caseExpected: '',
  })
}

const newRunFormValid = computed<boolean>(() => {
  const f = newRunModal.formData.value
  if (!f) return false
  return !!f.agentId && f.caseInput.trim().length > 0
})

// ---- batch evaluation editor ---------------------------------------------
const batchVisible = ref(false)
const batchRunning = ref(false)
const batchTargets = reactive<BatchTargetRow[]>([])
const batchCases = reactive<CaseRow[]>([])
const batchResults = ref<EvaluationRunDetailDto[]>([])

function openBatchDrawer(): void {
  if (!batchTargets.length) batchTargets.push({ agentId: '', versionNumber: null })
  if (!batchCases.length) batchCases.push({ input: '', expectedOutput: '' })
  void loadAgents()
  batchVisible.value = true
}
function addBatchTarget(): void {
  batchTargets.push({ agentId: '', versionNumber: null })
}
function removeBatchTarget(i: number): void {
  if (batchTargets.length > 1) batchTargets.splice(i, 1)
}
function addBatchCase(): void {
  batchCases.push({ input: '', expectedOutput: '' })
}
function removeBatchCase(i: number): void {
  if (batchCases.length > 1) batchCases.splice(i, 1)
}

const batchFormValid = computed<boolean>(() => {
  const validTargets = batchTargets.filter((tg) => tg.agentId.trim().length > 0)
  const validCases = batchCases.filter((c) => c.input.trim().length > 0)
  return validTargets.length > 0 && validCases.length > 0
})

const batchSummary = computed<string>(() => {
  if (!batchResults.value.length) return ''
  return t('batch.summary', { count: batchResults.value.length })
})

async function runBatchEval(): Promise<void> {
  if (!batchFormValid.value) return
  batchRunning.value = true
  try {
    const targets: BatchEvaluationTargetDto[] = batchTargets
      .filter((tg) => tg.agentId.trim().length > 0)
      .map((tg) => ({ agentId: tg.agentId.trim(), versionNumber: tg.versionNumber }))
    const cases: EvaluationCaseDto[] = batchCases
      .filter((c) => c.input.trim().length > 0)
      .map((c) => ({
        input: c.input.trim(),
        expectedOutput: c.expectedOutput.trim() || null,
      }))
    const dto: BatchEvaluationDto = { targets, cases }
    const result = await bridge.evaluations.runBatch(dto)
    batchResults.value = result.results ?? []
    message.success(t('batch.success'))
    await loadList()
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  } finally {
    batchRunning.value = false
  }
}

// ---- score trend ---------------------------------------------------------
const trendVisible = ref(false)
const trendLoading = ref(false)
const trendAgentId = ref<string>('')
const trendLastN = ref<number | null>(20)
const trendPoints = ref<EvaluationTrendPointDto[]>([])

function openTrendDrawer(): void {
  void loadAgents()
  trendVisible.value = true
}

async function loadTrend(): Promise<void> {
  if (!trendAgentId.value) return
  trendLoading.value = true
  try {
    const result = await bridge.evaluations.getTrend(
      trendAgentId.value,
      trendLastN.value ?? undefined,
    )
    trendPoints.value = result.points ?? []
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
    trendPoints.value = []
  } finally {
    trendLoading.value = false
  }
}

const trendOption = computed<EChartsOption>(() => ({
  tooltip: { trigger: 'axis' },
  legend: { data: ['Score', 'Pass rate'], top: 0 },
  grid: { left: 40, right: 24, top: 32, bottom: 24, containLabel: true },
  xAxis: { type: 'category', data: trendPoints.value.map((p) => shortId(p.runId)) },
  yAxis: { type: 'value', min: 0, max: 1 },
  series: [
    {
      name: 'Score',
      type: 'line',
      smooth: true,
      data: trendPoints.value.map((p) => Number(p.score.toFixed(3))),
      itemStyle: { color: 'var(--tnzi-primary)' },
    },
    {
      name: 'Pass rate',
      type: 'line',
      smooth: true,
      data: trendPoints.value.map((p) => Number(p.passRate.toFixed(3))),
      itemStyle: { color: 'var(--tnzi-success)' },
    },
  ],
}))

function barWidth(ratio: number): CSSProperties {
  const pct = Math.max(0, Math.min(1, ratio)) * 100
  return { width: `${pct}%` }
}

// ---- version comparison --------------------------------------------------
const compareVisible = ref(false)
const compareLoading = ref(false)
const compareAgentId = ref<string>('')
const compareVersionA = ref<number | null>(1)
const compareVersionB = ref<number | null>(2)
const comparison = ref<VersionComparisonDto | null>(null)

function openCompareDrawer(): void {
  void loadAgents()
  compareVisible.value = true
}

const compareFormValid = computed<boolean>(() =>
  !!compareAgentId.value &&
  compareVersionA.value != null &&
  compareVersionB.value != null &&
  compareVersionA.value !== compareVersionB.value,
)

async function loadComparison(): Promise<void> {
  if (!compareFormValid.value) return
  compareLoading.value = true
  try {
    comparison.value = await bridge.evaluations.compareVersions(
      compareAgentId.value,
      compareVersionA.value as number,
      compareVersionB.value as number,
    )
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
    comparison.value = null
  } finally {
    compareLoading.value = false
  }
}

function signedDelta(delta: number): string {
  const sign = delta >= 0 ? '+' : ''
  return `${sign}${delta.toFixed(3)}`
}

// ---- shared helpers ------------------------------------------------------
function passRatio(r: EvaluationRunDto): number {
  if (!r.caseCount) return 0
  return r.passedCount / r.caseCount
}

function statusTypeFor(status: unknown): 'success' | 'error' | 'warning' | 'info' | 'default' {
  switch (String(status)) {
    case 'Completed': return 'success'
    case 'Failed': return 'error'
    case 'Running': return 'info'
    default: return 'default'
  }
}

/** i18n label for an EvaluationRunStatus member (humanised fallback on miss). */
function statusLabel(status: unknown): string {
  const s = String(status ?? '')
  if (!s) return ''
  return t(`status.${s.charAt(0).toLowerCase()}${s.slice(1)}`)
}

function shortId(id: string): string {
  return id.length > 12 ? `${id.slice(0, 8)}…${id.slice(-4)}` : id
}

// Routed through @tnzi/core rather than a local toLocaleString: one
// implementation means one rendering of a timestamp across the whole admin.
// It also handles the case the try/catch here was reaching for - building a
// Date from unparseable input yields an Invalid Date, it does not throw, so
// that catch block never ran and the cell rendered the text "Invalid Date".
const formatTime = (v?: string | Date | null): string => formatDateTime(v, { fallback: '' })

function formatJson(s: string | null | undefined): string {
  if (!s) return ''
  try {
    return JSON.stringify(JSON.parse(s), null, 2)
  } catch {
    return s
  }
}

async function loadList(): Promise<void> {
  listLoading.value = true
  try {
    const result = await bridge.evaluations.fetch({
      pageIndex: 1,
      pageSize: 50,
      sortField: 'creationTime',
      sortOrder: 'desc',
      searchText: '',
      filters: {},
    })
    runs.value = result.items
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
    runs.value = []
  } finally {
    listLoading.value = false
  }
}

// Click-to-pick: first click sets A, second sets B, third resets B then sets new B,
// clicking the same row again clears it.
function onPickRun(id: string): void {
  if (id === leftId.value) {
    leftId.value = null
    leftDetail.value = null
    return
  }
  if (id === rightId.value) {
    rightId.value = null
    rightDetail.value = null
    return
  }
  if (!leftId.value) {
    leftId.value = id
  } else {
    rightId.value = id
  }
}

async function loadDetail(id: string): Promise<EvaluationRunDetailDto | null> {
  try {
    return await bridge.evaluations.getDetail(id)
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
    return null
  }
}

watch(leftId, async (id) => {
  leftDetail.value = id ? await loadDetail(id) : null
})
watch(rightId, async (id) => {
  rightDetail.value = id ? await loadDetail(id) : null
})

async function submitNewRun(): Promise<void> {
  const form = newRunModal.formData.value
  if (!form || !newRunFormValid.value) return
  creating.value = true
  try {
    const dto: CreateEvaluationRunDto = {
      agentId: form.agentId,
      versionNumber: form.versionNumber,
      cases: [
        {
          input: form.caseInput.trim(),
          expectedOutput: form.caseExpected.trim() || null,
        },
      ],
    }
    const created = await bridge.evaluations.create(dto)
    message.success(t('createSuccess'))
    newRunModal.close()
    await loadList()
    // Auto-pick the freshly created run on the left.
    leftId.value = (created as EvaluationRunDto).id
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  } finally {
    creating.value = false
  }
}

async function refresh(): Promise<void> {
  await loadList()
}

onMounted(() => {
  void loadList()
})

defineExpose({
  refresh,
  // Batch editor surface (exercised by integration tests; NDrawer teleports
  // outside the mounted tree so tests drive the runner programmatically).
  openBatchDrawer,
  batchTargets,
  batchCases,
  runBatchEval,
  // Trend surface.
  trendAgentId,
  trendLastN,
  loadTrend,
  trendPoints,
  // Compare surface.
  compareAgentId,
  compareVersionA,
  compareVersionB,
  loadComparison,
  comparison,
})
</script>

<style scoped>
.t-eval-page__modal-footer {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}
.t-eval-page__note {
  margin: 0 0 12px;
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
}
/* Master/detail split, responsive stacking and pane fill-height come from
   <TMasterDetailLayout>. Only page-specific content styling stays here. */
.t-eval-page__list-header {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  margin-bottom: 8px;
}
.t-eval-page__hint {
  font-size: 11px;
  color: var(--tnzi-base-text-muted);
}
.t-eval-page__run-list {
  list-style: none;
  padding: 0;
  margin: 0;
  max-height: 64vh;
  overflow: auto;
}
.t-eval-page__run-item {
  padding: 10px 12px;
  border: 2px solid transparent;
  border-radius: var(--tnzi-admin-radius-md, 4px);
  cursor: pointer;
  margin-bottom: 6px;
  transition: background-color 0.15s, border-color 0.15s;
}
.t-eval-page__run-item:hover {
  background: var(--tnzi-layout-bg);
}
.t-eval-page__run-item.is-selected {
  background: rgb(var(--tnzi-primary-rgb) / 0.08);
}
.t-eval-page__run-item.is-left {
  border-color: var(--tnzi-primary);
}
.t-eval-page__run-item.is-right {
  border-color: var(--tnzi-warning);
}
.t-eval-page__run-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 4px;
}
.t-eval-page__score {
  font-weight: 600;
  font-size: 14px;
}
.t-eval-page__score[data-pass="true"] {
  color: var(--tnzi-success);
}
.t-eval-page__score[data-pass="false"] {
  color: var(--tnzi-error);
}
.t-eval-page__run-meta {
  font-size: 11px;
  color: var(--tnzi-base-text-muted);
  display: flex;
  justify-content: space-between;
}
.t-eval-page__run-meta code {
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
}
.t-eval-page__run-time {
  font-size: 11px;
  color: var(--tnzi-base-text-muted);
  margin-top: 2px;
}
.t-eval-page__detail {
  padding: 0 4px;
}
.t-eval-page__placeholder,
.t-eval-page__empty {
  color: var(--tnzi-base-text-muted);
  text-align: center;
  padding: 40px 16px;
  font-size: 13px;
}
.t-eval-page__diff {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px;
}
.t-eval-page__diff-col {
  border: 1px solid var(--tnzi-border);
  border-radius: var(--tnzi-admin-radius-md, 4px);
  padding: 12px;
  display: flex;
  flex-direction: column;
}
.t-eval-page__diff-head {
  display: flex;
  align-items: center;
  gap: 8px;
  padding-bottom: 8px;
  border-bottom: 1px solid var(--tnzi-border);
  margin-bottom: 8px;
}
.t-eval-page__col-label {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 24px;
  height: 24px;
  border-radius: 50%;
  font-weight: 600;
  font-size: 12px;
  color: white;
}
.t-eval-page__col-label.is-left {
  background: var(--tnzi-primary);
}
.t-eval-page__col-label.is-right {
  background: var(--tnzi-warning);
}
.t-eval-page__col-score {
  margin-left: 8px;
  color: var(--tnzi-base-text-muted);
  font-size: 12px;
}
.t-eval-page__results {
  flex: 1;
  margin: 0;
  font-size: 12px;
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
  background: var(--tnzi-layout-bg);
  padding: 8px;
  border-radius: 4px;
  overflow: auto;
  max-height: 540px;
  white-space: pre-wrap;
  word-break: break-word;
}

/* ---- drawer editor blocks (batch / trend / compare) ---- */
.t-eval-page__section {
  margin-bottom: 20px;
}
.t-eval-page__section-head {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 8px;
  font-weight: 600;
  font-size: 13px;
}
.t-eval-page__row {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 8px;
}
.t-eval-page__row-grow {
  flex: 1;
  min-width: 0;
}
.t-eval-page__case {
  border: 1px solid var(--tnzi-border);
  border-radius: var(--tnzi-admin-radius-md, 4px);
  padding: 10px;
  margin-bottom: 8px;
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.t-eval-page__case-head {
  display: flex;
  justify-content: space-between;
  align-items: center;
}
.t-eval-page__case-index {
  font-weight: 600;
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
}
.t-eval-page__result-list {
  list-style: none;
  padding: 0;
  margin: 0;
}
.t-eval-page__result-item {
  border: 1px solid var(--tnzi-border);
  border-radius: var(--tnzi-admin-radius-md, 4px);
  padding: 10px;
  margin-bottom: 8px;
}
.t-eval-page__result-row {
  display: flex;
  align-items: center;
  gap: 8px;
}
.t-eval-page__result-row code {
  flex: 1;
  min-width: 0;
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
  font-size: 12px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.t-eval-page__result-meta {
  margin-top: 4px;
  font-size: 11px;
  color: var(--tnzi-base-text-muted);
}

/* ---- trend ---- */
.t-eval-page__trend-list {
  list-style: none;
  padding: 0;
  margin: 12px 0 0;
}
.t-eval-page__trend-item {
  padding: 8px 0;
  border-bottom: 1px dashed var(--tnzi-border);
}
.t-eval-page__trend-row {
  display: flex;
  justify-content: space-between;
  font-size: 12px;
  margin-bottom: 4px;
}
.t-eval-page__trend-date {
  color: var(--tnzi-base-text-muted);
}
.t-eval-page__bars {
  display: flex;
  flex-direction: column;
  gap: 3px;
}
.t-eval-page__bar {
  height: 6px;
  background: var(--tnzi-layout-bg);
  border-radius: 3px;
  overflow: hidden;
}
.t-eval-page__bar-fill {
  display: block;
  height: 100%;
  border-radius: 3px;
}
.t-eval-page__bar-fill.is-score {
  background: var(--tnzi-primary);
}
.t-eval-page__bar-fill.is-pass {
  background: var(--tnzi-success);
}

/* ---- compare ---- */
.t-eval-page__compare {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px;
  margin-top: 8px;
}
@media (max-width: 767px) {
  .t-eval-page__compare {
    grid-template-columns: 1fr;
  }
}
.t-eval-page__compare-col {
  border: 1px solid var(--tnzi-border);
  border-radius: var(--tnzi-admin-radius-md, 4px);
  padding: 12px;
}
.t-eval-page__compare-col.is-winner {
  border-color: var(--tnzi-success);
  background: rgb(var(--tnzi-success-rgb) / 0.06);
}
.t-eval-page__compare-head {
  display: flex;
  align-items: center;
  gap: 8px;
  font-weight: 600;
  margin-bottom: 8px;
}
.t-eval-page__stats {
  margin: 0;
}
.t-eval-page__stats > div {
  display: flex;
  justify-content: space-between;
  padding: 4px 0;
  font-size: 13px;
  border-bottom: 1px dashed var(--tnzi-border);
}
.t-eval-page__stats dt {
  color: var(--tnzi-base-text-muted);
  margin: 0;
}
.t-eval-page__stats dd {
  margin: 0;
  font-weight: 600;
}
.t-eval-page__delta {
  margin-top: 12px;
  text-align: center;
  font-weight: 600;
  font-size: 14px;
  color: var(--tnzi-base-text-muted);
}
.t-eval-page__delta[data-positive="true"] {
  color: var(--tnzi-success);
}
.t-eval-page__delta[data-positive="false"] {
  color: var(--tnzi-error);
}
</style>
