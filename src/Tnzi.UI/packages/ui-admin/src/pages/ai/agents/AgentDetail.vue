<template>
  <TDetailHost
    :state="detail"
    layout="side"
    :sections="sections"
    :back="'/admin/ai/agents'"
    :translate="t"
  >
    <template #title>
      <span class="t-agent-detail__head">
        <TSvgIcon icon="mdi:robot-happy-outline" :size="18" color="var(--tnzi-primary, #6d5ce7)" />
        <span class="t-agent-detail__name">{{ agent?.name ?? EMPTY_DASH }}</span>
        <NTag
          v-if="agent"
          size="small"
          :type="agent.isEnabled ? 'success' : 'warning'"
          :bordered="false"
        >
          {{ agent.isEnabled ? t('detail.enabled') : t('detail.disabled') }}
        </NTag>
        <span v-if="agent?.provider" class="t-agent-detail__head-meta font-mono">
          {{ agent.provider }}{{ agent.model ? ` · ${agent.model}` : '' }}
        </span>
      </span>
    </template>

    <template #default="{ section, sectionIcon }">
      <div class="t-agent-detail__panel">
        <NSpin :show="loading">
          <div v-if="loadError" class="t-agent-detail__error" role="alert">{{ loadError }}</div>
          <div v-else-if="!agent" class="t-agent-detail__placeholder">{{ EMPTY_DASH }}</div>
          <template v-else>
            <!-- Basic Info - identity + model & sampling + routing/capabilities (one page) -->
            <TDetailSection
              v-if="section === 'basicInfo'"
              :title="t('detail.panels.basicInfo')"
              :icon="sectionIcon"
              :hint="t('detail.basicInfo.hint')"
            >
              <NForm label-placement="left" label-width="120px">
                <div class="t-agent-detail__subhead">{{ t('detail.basicInfo.identity') }}</div>
                <NFormItem :label="t('form.name')" required>
                  <NInput v-model:value="edit.name" />
                </NFormItem>
                <NFormItem :label="t('form.description')">
                  <NInput v-model:value="edit.description" type="textarea" :rows="2" />
                </NFormItem>
                <NFormItem :label="t('form.isEnabled')">
                  <NSwitch v-model:value="edit.isEnabled" />
                </NFormItem>

                <div class="t-agent-detail__subhead">{{ t('detail.basicInfo.model') }}</div>
                <NFormItem :label="t('form.provider')" required>
                  <NSelect
                    v-model:value="edit.provider"
                    :options="providerSelectOptions"
                    :loading="providerOptionsLoading"
                    filterable
                    tag
                    :placeholder="t('detail.providerPlaceholder')"
                    @update:value="onProviderChange"
                  />
                </NFormItem>
                <NFormItem :label="t('form.model')">
                  <NSelect
                    v-model:value="edit.model"
                    :options="modelSelectOptions"
                    :loading="modelsLoading"
                    filterable
                    tag
                    clearable
                    :placeholder="t('detail.modelPlaceholder')"
                  />
                </NFormItem>
                <NFormItem :label="t('form.instructions')">
                  <NInput
                    v-model:value="edit.instructions"
                    type="textarea"
                    :rows="3"
                    :placeholder="t('detail.instructionsPlaceholder')"
                  />
                </NFormItem>
                <NFormItem :label="t('form.temperature')">
                  <NInputNumber v-model:value="edit.temperature" :step="0.1" :min="0" :max="2" clearable class="w-140px" />
                </NFormItem>
                <NFormItem :label="t('form.maxTokens')">
                  <NInputNumber v-model:value="edit.maxTokens" :min="0" clearable class="w-140px" />
                </NFormItem>

                <div class="t-agent-detail__subhead">{{ t('detail.basicInfo.capabilities') }}</div>
                <NFormItem :label="t('detail.capabilities.domains')">
                  <NDynamicTags v-model:value="domainsModel" />
                </NFormItem>
                <NFormItem :label="t('detail.capabilities.roles')">
                  <NDynamicTags v-model:value="rolesModel" />
                </NFormItem>
                <NFormItem :label="t('detail.capabilities.qualityTier')">
                  <NInputNumber v-model:value="edit.qualityTier" :min="1" :max="5" class="w-140px" />
                </NFormItem>
                <NFormItem :label="t('detail.capabilities.latencyTier')">
                  <NInputNumber v-model:value="edit.latencyTier" :min="1" :max="5" class="w-140px" />
                </NFormItem>
                <NFormItem :label="t('detail.capabilities.costTier')">
                  <NInputNumber v-model:value="edit.costTier" :min="1" :max="5" class="w-140px" />
                </NFormItem>
              </NForm>
              <template #savebar>
                <NButton size="small" :disabled="!isDirty" @click="resetEdits">{{ t('detail.reset') }}</NButton>
                <NButton v-if="can('ai.agent.update')" size="small" type="primary" :loading="saving" :disabled="!isDirty" @click="handleSave">{{ t('detail.save') }}</NButton>
              </template>
            </TDetailSection>

            <!-- Persona - inline soul content editor (single persona per agent) -->
            <AgentPersonaPanel
              v-else-if="section === 'persona'"
              :icon="sectionIcon"
              :persona="agent?.persona ?? null"
              :saving="personaSaving"
              @save="savePersona"
            />

            <!-- Tools - rich resource picker (assign / remove, immediate persist) -->
            <AgentResourcePicker
              v-else-if="section === 'tools'"
              :header-icon="sectionIcon"
              icon="mdi:wrench-outline"
              :title="t('detail.panels.tools')"
              :hint="t('detail.toolsHint')"
              :add-label="t('detail.tools.add')"
              :remove-label="t('detail.tools.remove')"
              :remove-confirm="t('detail.tools.removeConfirm')"
              :assign-label="t('detail.tools.assign')"
              :empty-text="t('detail.tools.empty')"
              :all-assigned-text="t('detail.tools.allAssigned')"
              :search-placeholder="t('detail.tools.searchPlaceholder')"
              :loading="toolGroupsLoading"
              :assigned="assignedToolItems"
              :available="availableToolItems"
              @assign="assignTool"
              @remove="removeTool"
            />

            <!-- Knowledge - resource picker + retrieval test -->
            <AgentResourcePicker
              v-else-if="section === 'knowledge'"
              :header-icon="sectionIcon"
              icon="mdi:book-open-variant"
              :title="t('detail.panels.knowledge')"
              :hint="t('detail.knowledgeHint')"
              :add-label="t('detail.knowledge.add')"
              :remove-label="t('detail.knowledge.remove')"
              :remove-confirm="t('detail.knowledge.removeConfirm')"
              :assign-label="t('detail.knowledge.assign')"
              :empty-text="t('detail.knowledge.empty')"
              :all-assigned-text="t('detail.knowledge.allAssigned')"
              :search-placeholder="t('detail.knowledge.searchPlaceholder')"
              :loading="knowledgeBasesLoading"
              :assigned="assignedKbItems"
              :available="availableKbItems"
              @assign="assignKnowledge"
              @remove="removeKnowledge"
            >
              <template #prepend>
                <div v-if="assignedKbItems.length" class="t-agent-detail__kb-test">
                  <div class="flex items-center gap-8px">
                    <NInput
                      v-model:value="kbTestQuery"
                      size="small"
                      class="flex-1"
                      :placeholder="t('detail.knowledge.testPlaceholder')"
                      @keyup.enter="runKnowledgeTest"
                    />
                    <NButton size="small" type="primary" :loading="kbTestLoading" @click="runKnowledgeTest">
                      {{ t('detail.knowledge.test') }}
                    </NButton>
                  </div>
                  <div v-if="kbTestResults.length" class="t-agent-detail__kb-hits">
                    <div v-for="(hit, i) in kbTestResults" :key="i" class="t-agent-detail__kb-hit">
                      <div class="t-agent-detail__kb-hit-head">
                        <span class="t-agent-detail__kb-hit-src">{{ hit.sourceName || t('detail.knowledge.unknownSource') }}</span>
                        <span class="t-agent-detail__kb-hit-score">{{ kbScorePercent(hit.score) }}%</span>
                      </div>
                      <NProgress type="line" :percentage="kbScorePercent(hit.score)" :show-indicator="false" :height="4" />
                      <div class="t-agent-detail__kb-hit-text">{{ hit.content }}</div>
                    </div>
                  </div>
                  <div v-else-if="kbTestedOnce && !kbTestLoading" class="t-agent-detail__hint">
                    {{ t('detail.knowledge.noHits') }}
                  </div>
                </div>
              </template>
            </AgentResourcePicker>

            <!-- Skills - rich resource picker -->
            <AgentResourcePicker
              v-else-if="section === 'skills'"
              :header-icon="sectionIcon"
              icon="mdi:puzzle-outline"
              :title="t('detail.panels.skills')"
              :hint="t('detail.skillsHint')"
              :add-label="t('detail.skills.add')"
              :remove-label="t('detail.skills.remove')"
              :remove-confirm="t('detail.skills.removeConfirm')"
              :assign-label="t('detail.skills.assign')"
              :empty-text="t('detail.skills.empty')"
              :all-assigned-text="t('detail.skills.allAssigned')"
              :search-placeholder="t('detail.skills.searchPlaceholder')"
              :loading="skillsLoading"
              :assigned="assignedSkillItems"
              :available="availableSkillItems"
              @assign="assignSkill"
              @remove="removeSkill"
            />

            <!-- Recent Runs -->
            <TDetailSection v-else-if="section === 'runs'" :title="t('detail.panels.runs')" :icon="sectionIcon" max-width="none">
              <div v-if="runsError" class="t-agent-detail__error">{{ runsError }}</div>
              <ul v-else-if="recentRuns.length" class="t-agent-detail__runs-list">
                <li v-for="run in recentRuns" :key="run.id">
                  <router-link :to="{ name: 'ai.agents.runs', params: { agentId: agent?.id, runId: run.id } }">
                    <code>{{ run.id.slice(0, 8) }}</code>
                  </router-link>
                  <NTag size="tiny" :type="runStatusType(run.status)" :bordered="false">
                    {{ runStatusLabel(run.status) }}
                  </NTag>
                  <span class="t-agent-detail__runs-time">{{ formatTime(run.creationTime) }}</span>
                </li>
              </ul>
              <div v-else class="t-agent-detail__hint">{{ t('detail.noRuns') }}</div>
            </TDetailSection>

            <!-- Versions / Rollback -->
            <TDetailSection
              v-else-if="section === 'versions'"
              :title="t('detail.versions.title')"
              :icon="sectionIcon"
              :hint="t('detail.versions.hint')"
              max-width="none"
            >
              <template #actions>
                <NButton size="small" :loading="versionsLoading" @click="reloadVersions">
                  {{ t('detail.versions.refresh') }}
                </NButton>
              </template>
              <div v-if="versionsError" class="t-agent-detail__error">{{ versionsError }}</div>
              <NList v-else-if="versions.length" bordered>
                  <NListItem v-for="v in versions" :key="v.id">
                    <div class="t-agent-detail__version">
                      <div class="t-agent-detail__version-head">
                        <NTag size="small" type="info" :bordered="false">v{{ v.version }}</NTag>
                        <span class="t-agent-detail__version-note">{{ v.changeNote || t('detail.versions.noNote') }}</span>
                        <span class="t-agent-detail__version-time">{{ formatTime(v.creationTime) }}</span>
                      </div>
                      <div class="t-agent-detail__version-actions">
                        <NButton
                          size="small"
                          quaternary
                          @click="toggleSnapshot(v.id)"
                        >
                          {{ expandedVersion === v.id ? t('detail.versions.hideConfig') : t('detail.versions.viewConfig') }}
                        </NButton>
                        <NPopconfirm v-if="can('ai.agent.update')" @positive-click="() => handleRollback(v.version)">
                          <template #trigger>
                            <NButton
                              size="small"
                              type="warning"
                              :loading="rollingBackTo === v.version"
                              :disabled="rollingBackTo !== null"
                            >
                              {{ t('detail.versions.rollback') }}
                            </NButton>
                          </template>
                          {{ t('detail.versions.rollbackConfirm') }}
                        </NPopconfirm>
                      </div>
                      <pre v-if="expandedVersion === v.id" class="t-agent-detail__snapshot">{{ formatSnapshot(v.configSnapshot) }}</pre>
                    </div>
                  </NListItem>
              </NList>
              <div v-else class="t-agent-detail__hint">{{ t('detail.versions.empty') }}</div>
            </TDetailSection>

            <!-- A/B Test -->
            <TDetailSection
              v-else-if="section === 'abtest'"
              :title="t('detail.abtest.title')"
              :icon="sectionIcon"
              :hint="t('detail.abtest.hint')"
            >
              <NForm label-placement="left" label-width="140px">
                <NFormItem :label="t('detail.abtest.versionA')">
                  <NInputNumber v-model:value="abTest.versionA" :min="1" :step="1" class="w-160px" />
                </NFormItem>
                <NFormItem :label="t('detail.abtest.versionB')">
                  <NInputNumber v-model:value="abTest.versionB" :min="1" :step="1" class="w-160px" />
                </NFormItem>
                <NFormItem :label="t('detail.abtest.trafficB')">
                  <div class="t-agent-detail__ab-traffic">
                    <NSlider v-model:value="abTest.trafficPercentB" :min="0" :max="100" :step="1" class="flex-1" />
                    <NInputNumber
                      v-model:value="abTest.trafficPercentB"
                      :min="0"
                      :max="100"
                      :step="1"
                      size="small"
                      class="w-120px"
                    >
                      <template #suffix>%</template>
                    </NInputNumber>
                  </div>
                </NFormItem>
              </NForm>
              <NSpace size="small">
                <NButton
                  v-if="can('ai.agent.update')"
                  size="small"
                  type="primary"
                  :loading="abStarting"
                  :disabled="!abTestValid"
                  @click="handleStartAbTest"
                >
                  {{ t('detail.abtest.start') }}
                </NButton>
                <NPopconfirm v-if="can('ai.agent.update')" @positive-click="handleStopAbTest">
                  <template #trigger>
                    <NButton size="small" :loading="abStopping">
                      {{ t('detail.abtest.stop') }}
                    </NButton>
                  </template>
                  {{ t('detail.abtest.stopConfirm') }}
                </NPopconfirm>
              </NSpace>
              <div v-if="abStatus" class="t-agent-detail__status" :data-state="abStatus.kind">
                {{ abStatus.message }}
              </div>
            </TDetailSection>

            <!-- Validation / Health -->
            <TDetailSection
              v-else-if="section === 'validation'"
              :title="t('detail.health.title')"
              :icon="sectionIcon"
              :hint="t('detail.health.hint')"
              max-width="none"
            >
              <template #actions>
                <NButton size="small" type="primary" :loading="validating" @click="runValidation">
                  {{ t('detail.health.validate') }}
                </NButton>
              </template>
              <div v-if="validationError" class="t-agent-detail__error">{{ validationError }}</div>
              <template v-else-if="validation">
                <NTag
                  size="small"
                  :type="validation.isValid ? 'success' : 'error'"
                  :bordered="false"
                >
                  {{ validation.isValid ? t('detail.health.valid') : t('detail.health.invalid') }}
                </NTag>
                <NList v-if="validation.checks.length" bordered>
                  <NListItem v-for="check in validation.checks" :key="check.name">
                    <div class="t-agent-detail__check">
                      <NTag
                        size="small"
                        :type="check.passed ? 'success' : 'error'"
                        :bordered="false"
                      >
                        {{ check.passed ? t('detail.health.pass') : t('detail.health.fail') }}
                      </NTag>
                      <div class="t-agent-detail__check-body">
                        <span class="t-agent-detail__check-name">{{ check.name }}</span>
                        <span v-if="check.details" class="t-agent-detail__check-details">{{ check.details }}</span>
                      </div>
                    </div>
                  </NListItem>
                </NList>
                <div v-else class="t-agent-detail__hint">{{ t('detail.health.noChecks') }}</div>
              </template>
              <div v-else class="t-agent-detail__hint">{{ t('detail.health.notRun') }}</div>
            </TDetailSection>

            <!-- Memory (admin-curated, agent-bound) -->
            <TDetailSection
              v-else-if="section === 'memory'"
              :title="t('detail.panels.memory')"
              :icon="sectionIcon"
              :hint="t('detail.memory.hint')"
              body-fill
            >
              <template #actions>
                <NButton v-if="can('ai.agent.update')" size="small" type="primary" @click="openMemoryCreate">
                  <template #icon><TSvgIcon icon="mdi:plus" :size="14" /></template>
                  {{ t('detail.memory.add') }}
                </NButton>
              </template>
              <TResponsiveTable
                class="t-agent-detail__memory-table"
                :columns="memoryColumns"
                :data="memoryRows"
                :row-key="(r: AgentMemoryDto) => r.id"
                :loading="memoryLoading"
                :flex-height="true"
                :pagination="{ pageSize: 20 }"
                :empty-text="t('detail.memory.empty')"
              />
            </TDetailSection>
          </template>
        </NSpin>

        <div v-if="saveStatus" class="t-agent-detail__status-bar" :data-state="saveStatus.kind">
          {{ saveStatus.message }}
        </div>

        <!-- Memory create / edit modal -->
        <TOverlayTheme>
        <NModal
          v-model:show="memoryModal.show"
          preset="card"
          size="small"
          :title="memoryModal.id ? t('detail.memory.editTitle') : t('detail.memory.addTitle')"
          class="w-520px max-w-96vw"
        >
          <NForm label-placement="top" :show-feedback="false">
            <NFormItem :label="t('detail.memory.content')" required>
              <NInput
                v-model:value="memoryModal.content"
                type="textarea"
                :rows="4"
                :placeholder="t('detail.memory.contentPlaceholder')"
              />
            </NFormItem>
            <NFormItem :label="t('detail.memory.category')">
              <NSelect
                v-model:value="memoryModal.category"
                :options="memoryCategoryOptions"
                tag
                filterable
                :placeholder="t('detail.memory.categoryPlaceholder')"
              />
            </NFormItem>
            <NFormItem :label="t('detail.memory.importance')">
              <div class="flex items-center gap-12px w-full">
                <NSlider v-model:value="memoryModal.importance" :min="0" :max="1" :step="0.05" class="flex-1" />
                <span class="font-mono text-12px w-40px text-right">{{ memoryModal.importance.toFixed(2) }}</span>
              </div>
            </NFormItem>
          </NForm>
          <template #footer>
            <div class="flex justify-end gap-8px">
              <NButton size="small" @click="memoryModal.show = false">{{ t('detail.memory.cancel') }}</NButton>
              <NButton
                size="small"
                type="primary"
                :loading="memorySaving"
                :disabled="!memoryModal.content.trim()"
                @click="saveMemory"
              >
                {{ t('detail.memory.save') }}
              </NButton>
            </div>
          </template>
        </NModal>
        </TOverlayTheme>
      </div>
    </template>
  </TDetailHost>
</template>

<script setup lang="ts">
import { EMPTY_DASH } from '../../../utils/placeholders'
import { computed, h, reactive, ref, watch, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import {
  NSpin, NForm, NFormItem, NInput, NInputNumber, NSwitch, NSelect, NTag, NButton,
  NSpace, NList, NListItem, NSlider, NPopconfirm, NModal, NDynamicTags, NProgress, useMessage,
  type DataTableColumns,
} from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import TDetailHost from '../../../components/detail/TDetailHost.vue'
import TDetailSection from '../../../components/detail/TDetailSection.vue'
import TResponsiveTable from '../../../components/data/TResponsiveTable.vue'
import { TOverlayTheme } from '../../../components/overlay'
import AgentResourcePicker, { type ResourcePickerItem } from './sections/AgentResourcePicker.vue'
import AgentPersonaPanel from './sections/AgentPersonaPanel.vue'
import { useDetail, type DetailSection } from '../../../headless/useDetail'
import { useTabTitle } from '../../../headless/useTabTitle'
import { usePermissionGuard } from '../../../headless/usePermissionGuard'
import { makePageTranslator } from '../../_shared/translate'
import { createAiBridge } from '../../../services/bridges/ai-bridge'
import { useAdminClient } from '../../../plugin/client'
import type {
  AgentDto, UpdateAgentDto, AgentRunDto,
  AgentVersionDto, AgentValidationResultDto,
  ProviderOptionDto, KnowledgeBaseDto, SkillSummaryDto, ToolGroupDto, AgentMemoryDto, SearchResultDto,
} from '@tnzi/core/services/ai'

const route = useRoute()
const bridge = createAiBridge({ client: useAdminClient() })
const t = makePageTranslator('ai.agents')
const { can } = usePermissionGuard()

// ui-admin shells don't always wrap with NMessageProvider, so `useMessage()`
// may throw - guard and fall back to the inline status panels each section
// already renders.
const message = (() => {
  try {
    return useMessage()
  } catch {
    return null
  }
})()

function toast(kind: 'ok' | 'err', text: string): void {
  if (!message) return
  if (kind === 'ok') message.success(text)
  else message.error(text)
}

// ---- Sections ----------------------------------------------------------------

const sections: DetailSection[] = [
  // Setup group - what the agent IS + the resources it can use
  { key: 'basicInfo', label: t('detail.panels.basicInfo'), icon: 'mdi:information-outline', group: t('detail.groups.setup') },
  { key: 'persona', label: t('detail.panels.persona'), icon: 'mdi:account-circle-outline', group: t('detail.groups.setup') },
  { key: 'skills', label: t('detail.panels.skills'), icon: 'mdi:puzzle-outline', group: t('detail.groups.setup') },
  { key: 'tools', label: t('detail.panels.tools'), icon: 'mdi:wrench-outline', group: t('detail.groups.setup') },
  { key: 'knowledge', label: t('detail.panels.knowledge'), icon: 'mdi:book-open-variant', group: t('detail.groups.setup') },
  { key: 'memory', label: t('detail.panels.memory'), icon: 'mdi:database-outline', group: t('detail.groups.setup') },
  // Operations group - runtime + lifecycle
  { key: 'runs', label: t('detail.panels.runs'), icon: 'mdi:play-circle-outline', group: t('detail.groups.operations') },
  { key: 'versions', label: t('detail.versions.title'), icon: 'mdi:history', group: t('detail.groups.operations') },
  { key: 'abtest', label: t('detail.abtest.title'), icon: 'mdi:ab-testing', group: t('detail.groups.operations') },
  { key: 'validation', label: t('detail.health.title'), icon: 'mdi:shield-check-outline', group: t('detail.groups.operations') },
]

// The single detail engine, page mode. The active section is two-way bound to
// the `?section=` query key (deep-linkable + the browser Back/Forward buttons
// step through sections) - no hand-wired router.replace, `push` history so Back
// returns to the previously viewed section within this agent's tab.
const detail = useDetail({
  mode: 'page',
  sectionUrl: true,
  sections,
  defaultSection: 'basicInfo',
})
const activeSection = detail.activeSection

watch(activeSection, (k) => {
  if (k) maybeLoadSection(k)
})

/**
 * Lazy-load the data a section needs the first time it becomes active.
 * Versions are paged & potentially large; validation runs an on-demand backend
 * check - neither should fire on the initial detail paint. The A/B section is a
 * pure config form (no read), so it has nothing to lazy-load.
 */
function maybeLoadSection(k: string | null): void {
  const id = currentRouteId()
  if (!id) return
  if (k === 'versions' && !versionsLoaded.value && !versionsLoading.value) {
    void loadVersions(id)
  } else if (k === 'validation' && !validation.value && !validating.value) {
    void runValidation()
  } else if (k === 'memory' && !memoryLoaded.value && !memoryLoading.value) {
    void loadMemory(id)
  }
}

// ---- State ----------------------------------------------------------------

const agent = ref<AgentDto | null>(null)
// This detail route opens its own tab per agent id; surface the agent name as
// the tab title so two open agent details are distinguishable (not both "Agent Detail").
useTabTitle(() => agent.value?.name)
const personaSaving = ref(false)
const recentRuns = ref<AgentRunDto[]>([])

// ---- Resource pickers (provider/model/knowledge/skills/tools catalogs) -----
const providerOptionList = ref<ProviderOptionDto[]>([])
const providerOptionsLoading = ref(false)
const modelList = ref<string[]>([])
const modelsLoading = ref(false)
const knowledgeBaseList = ref<KnowledgeBaseDto[]>([])
const knowledgeBasesLoading = ref(false)
const skillList = ref<SkillSummaryDto[]>([])
const skillsLoading = ref(false)
const toolGroupList = ref<ToolGroupDto[]>([])
const toolGroupsLoading = ref(false)
const loading = ref(false)
const saving = ref(false)
const loadError = ref<string | null>(null)
const runsError = ref<string | null>(null)
const saveStatus = ref<{ kind: 'ok' | 'err'; message: string } | null>(null)

// ---- Versions / A-B / Validation state ------------------------------------

const versions = ref<AgentVersionDto[]>([])
const versionsLoading = ref(false)
const versionsError = ref<string | null>(null)
const versionsLoaded = ref(false)
const expandedVersion = ref<string | null>(null)
const rollingBackTo = ref<number | null>(null)

interface AbTestState {
  versionA: number | null
  versionB: number | null
  trafficPercentB: number
}
const abTest = reactive<AbTestState>({ versionA: 1, versionB: 2, trafficPercentB: 10 })
const abStarting = ref(false)
const abStopping = ref(false)
const abStatus = ref<{ kind: 'ok' | 'err'; message: string } | null>(null)
const abTestValid = computed(
  () =>
    typeof abTest.versionA === 'number' &&
    typeof abTest.versionB === 'number' &&
    abTest.versionA !== abTest.versionB &&
    abTest.trafficPercentB >= 0 &&
    abTest.trafficPercentB <= 100,
)

const validation = ref<AgentValidationResultDto | null>(null)
const validating = ref(false)
const validationError = ref<string | null>(null)

interface EditState {
  name: string
  description: string
  instructions: string
  provider: string
  model: string
  isEnabled: boolean
  temperature: number | null
  maxTokens: number | null
  // Capabilities (part of the Basic Info page). Persona + resource assignments
  // (skills/tools/knowledge) are NOT here - they persist immediately via the
  // Persona panel / resource pickers, not through this save-bar form.
  domains: string[]
  roles: string[]
  qualityTier: number
  latencyTier: number
  costTier: number
}

function blankEdit(): EditState {
  return {
    name: '',
    description: '',
    instructions: '',
    provider: '',
    model: '',
    isEnabled: true,
    temperature: null,
    maxTokens: null,
    domains: [],
    roles: [],
    qualityTier: 3,
    latencyTier: 3,
    costTier: 3,
  }
}

const edit = reactive<EditState>(blankEdit())
const original = ref<EditState>(blankEdit())

// NDynamicTags needs a non-readonly array; sync via a computed getter/setter.
const domainsModel = computed<string[]>({
  get: () => edit.domains,
  set: (next) => { edit.domains = [...next] },
})
const rolesModel = computed<string[]>({
  get: () => edit.roles,
  set: (next) => { edit.roles = [...next] },
})

// Provider dropdown (from API) - value is the provider Name (what Agent.provider stores).
const providerSelectOptions = computed(() =>
  providerOptionList.value.map((p) => ({ label: `${p.name} · ${p.providerType}`, value: p.name })),
)
// Model dropdown - live /v1/models + static fallback for the selected provider.
const modelSelectOptions = computed(() => modelList.value.map((m) => ({ label: m, value: m })))
// ---- Resource assignment (Skills / Tools / Knowledge) - immediate persist ----
// Picker items are derived from the agent's stored assignment (source of truth,
// updated in place on each assign/remove) joined against the loaded catalogs.

async function persistResource(patch: UpdateAgentDto, okMsg: string): Promise<void> {
  if (!can('ai.agent.update')) return
  const id = currentRouteId()
  if (!id) return
  try {
    const updated = await bridge.agents.update(id, patch)
    agent.value = updated
    hydrateEdit(updated)
    toast('ok', okMsg)
  } catch (e) {
    toast('err', (e as Error).message ?? t('detail.saved'))
  }
}

// Skills
const assignedSkillItems = computed<ResourcePickerItem[]>(() =>
  (agent.value?.skillSlugs ?? []).map((slug) => {
    const s = skillList.value.find((x) => x.slug === slug)
    return { value: slug, title: s?.name ?? slug, subtitle: slug, description: s?.description ?? undefined, tags: s?.tags }
  }),
)
const availableSkillItems = computed<ResourcePickerItem[]>(() => {
  const used = new Set(agent.value?.skillSlugs ?? [])
  return skillList.value
    .filter((s) => !used.has(s.slug))
    .map((s) => ({ value: s.slug, title: s.name, subtitle: s.slug, description: s.description ?? undefined }))
})
const assignSkill = (slug: string) =>
  persistResource({ skillSlugs: [...(agent.value?.skillSlugs ?? []), slug] }, t('detail.skills.assigned'))
const removeSkill = (slug: string) =>
  persistResource({ skillSlugs: (agent.value?.skillSlugs ?? []).filter((x) => x !== slug) }, t('detail.skills.removed'))

// Tools (groups)
const assignedToolItems = computed<ResourcePickerItem[]>(() =>
  (agent.value?.toolGroups ?? []).map((name) => {
    const g = toolGroupList.value.find((x) => x.name === name)
    return {
      value: name,
      title: name,
      subtitle: g ? `${g.toolCount} ${t('detail.tools.toolsSuffix')}` : undefined,
      description: g?.toolNames.join(', ') || undefined,
    }
  }),
)
const availableToolItems = computed<ResourcePickerItem[]>(() => {
  const used = new Set(agent.value?.toolGroups ?? [])
  return toolGroupList.value
    .filter((g) => !used.has(g.name))
    .map((g) => ({ value: g.name, title: g.name, subtitle: `${g.toolCount} ${t('detail.tools.toolsSuffix')}`, description: g.toolNames.join(', ') || undefined }))
})
const assignTool = (name: string) =>
  persistResource({ toolGroups: [...(agent.value?.toolGroups ?? []), name] }, t('detail.tools.assigned'))
const removeTool = (name: string) =>
  persistResource({ toolGroups: (agent.value?.toolGroups ?? []).filter((x) => x !== name) }, t('detail.tools.removed'))

// Knowledge bases
const assignedKbItems = computed<ResourcePickerItem[]>(() =>
  (agent.value?.knowledgeBaseIds ?? []).map((id) => {
    const k = knowledgeBaseList.value.find((x) => x.id === id)
    return {
      value: id,
      title: k?.name ?? id,
      subtitle: k?.embeddingModel ?? undefined,
      meta: k ? `${k.documentCount ?? 0} ${t('detail.knowledge.docs')} · ${k.chunkCount ?? 0} ${t('detail.knowledge.chunks')}` : undefined,
    }
  }),
)
const availableKbItems = computed<ResourcePickerItem[]>(() => {
  const used = new Set(agent.value?.knowledgeBaseIds ?? [])
  return knowledgeBaseList.value
    .filter((k) => !used.has(k.id))
    .map((k) => ({ value: k.id, title: k.name, subtitle: k.embeddingModel ?? undefined, description: k.description ?? undefined }))
})
const assignKnowledge = (id: string) =>
  persistResource({ knowledgeBaseIds: [...(agent.value?.knowledgeBaseIds ?? []), id] }, t('detail.knowledge.assigned'))
const removeKnowledge = (id: string) =>
  persistResource({ knowledgeBaseIds: (agent.value?.knowledgeBaseIds ?? []).filter((x) => x !== id) }, t('detail.knowledge.removed'))

// Knowledge retrieval test - exercise what this agent would retrieve from its
// assigned knowledge bases (loops the per-KB search and merges by score).
const kbTestQuery = ref('')
const kbTestResults = ref<SearchResultDto[]>([])
const kbTestLoading = ref(false)
const kbTestedOnce = ref(false)
async function runKnowledgeTest(): Promise<void> {
  const ids = agent.value?.knowledgeBaseIds ?? []
  if (!kbTestQuery.value.trim() || ids.length === 0 || kbTestLoading.value) return
  kbTestLoading.value = true
  kbTestedOnce.value = true
  try {
    const batches = await Promise.all(
      ids.map((id) => bridge.knowledge.searchTest(id, { query: kbTestQuery.value.trim(), topK: 5 }).catch(() => [] as SearchResultDto[])),
    )
    kbTestResults.value = batches.flat().sort((a, b) => (b.score ?? 0) - (a.score ?? 0)).slice(0, 8)
  } finally {
    kbTestLoading.value = false
  }
}
function kbScorePercent(score: number | null | undefined): number {
  return Math.round((score ?? 0) * 100)
}

function arraysDiffer(a: string[], b: string[]): boolean {
  if (a.length !== b.length) return true
  for (let i = 0; i < a.length; i++) {
    if (a[i] !== b[i]) return true
  }
  return false
}

const isDirty = computed(() => {
  const keys: (keyof EditState)[] = [
    'name', 'description', 'instructions', 'provider', 'model',
    'isEnabled', 'temperature', 'maxTokens',
    'qualityTier', 'latencyTier', 'costTier',
  ]
  for (const k of keys) {
    if (edit[k] !== original.value[k]) return true
  }
  // Capability tag arrays: shallow ordered compare.
  return arraysDiffer(edit.domains, original.value.domains)
    || arraysDiffer(edit.roles, original.value.roles)
})

// ---- Loaders --------------------------------------------------------------

async function loadAgent(id: string): Promise<void> {
  loading.value = true
  loadError.value = null
  try {
    // Exact lookup by id (GET /admin/agents/{id}). The old filtered-fetch
    // fallback was unreliable: AgentListQueryDto has no `id` filter, so it
    // returned the first agent rather than the requested one.
    const found = await bridge.agents.getById(id)
    if (!found) {
      loadError.value = t('detail.loadError')
      return
    }
    agent.value = found
    hydrateEdit(found)
  } catch (e) {
    loadError.value = (e as Error).message ?? t('detail.loadError')
  } finally {
    loading.value = false
  }
}

function hydrateEdit(src: AgentDto): void {
  const next: EditState = {
    name: src.name,
    description: src.description ?? '',
    instructions: src.instructions ?? '',
    provider: src.provider,
    model: src.model ?? '',
    isEnabled: src.isEnabled,
    temperature: src.temperature ?? null,
    maxTokens: src.maxTokens ?? null,
    domains: [...(src.domains ?? [])],
    roles: [...(src.roles ?? [])],
    qualityTier: src.qualityTier ?? 3,
    latencyTier: src.latencyTier ?? 3,
    costTier: src.costTier ?? 3,
  }
  Object.assign(edit, next)
  original.value = JSON.parse(JSON.stringify(next))
}

async function loadProviderOptions(): Promise<void> {
  providerOptionsLoading.value = true
  try {
    providerOptionList.value = await bridge.providers.getOptions()
  } catch {
    providerOptionList.value = []
  } finally {
    providerOptionsLoading.value = false
  }
}

/** Load the model dropdown for a provider name (resolves its id → live /v1/models + fallback). */
async function loadModels(providerName: string | null | undefined): Promise<void> {
  if (!providerName) { modelList.value = []; return }
  const provider = providerOptionList.value.find((p) => p.name === providerName)
  if (!provider) { modelList.value = []; return }
  modelsLoading.value = true
  try {
    const result = await bridge.providers.listModels(provider.id)
    modelList.value = result.models ?? []
  } catch {
    modelList.value = []
  } finally {
    modelsLoading.value = false
  }
}

function onProviderChange(name: string | null): void {
  // Reload the model dropdown for the newly selected provider; keep the current
  // model value (tag mode lets it stay selectable even if absent from the list).
  void loadModels(name)
}

async function loadKnowledgeBases(): Promise<void> {
  knowledgeBasesLoading.value = true
  try {
    const result = await bridge.knowledge.fetch({
      pageIndex: 1, pageSize: 200, sortField: 'name', sortOrder: 'asc' as const, searchText: '', filters: {},
    })
    knowledgeBaseList.value = result.items
  } catch {
    knowledgeBaseList.value = []
  } finally {
    knowledgeBasesLoading.value = false
  }
}

async function loadSkills(): Promise<void> {
  skillsLoading.value = true
  try {
    const result = await bridge.skills.fetch({
      pageIndex: 1, pageSize: 500, sortField: 'name', sortOrder: 'asc' as const, searchText: '', filters: {},
    })
    skillList.value = result.items
  } catch {
    skillList.value = []
  } finally {
    skillsLoading.value = false
  }
}

async function loadToolGroups(): Promise<void> {
  toolGroupsLoading.value = true
  try {
    toolGroupList.value = await bridge.agents.getToolGroups()
  } catch {
    toolGroupList.value = []
  } finally {
    toolGroupsLoading.value = false
  }
}

async function loadRecentRuns(id: string): Promise<void> {
  runsError.value = null
  try {
    const result = await bridge.agentRuns.fetch({
      pageIndex: 1,
      pageSize: 8,
      sortField: 'creationTime',
      sortOrder: 'desc',
      searchText: '',
      filters: { agentId: id },
    })
    recentRuns.value = result.items
  } catch (e) {
    runsError.value = (e as Error).message ?? 'Failed to load runs'
  }
}

// ---- Memory (admin-curated, agent-bound) ----------------------------------
const memoryRows = ref<AgentMemoryDto[]>([])
const memoryLoading = ref(false)
const memoryLoaded = ref(false)
const memorySaving = ref(false)
interface MemoryModalState {
  show: boolean
  id: string | null
  content: string
  category: string
  importance: number
}
const memoryModal = reactive<MemoryModalState>({ show: false, id: null, content: '', category: '', importance: 0.5 })

// Preset memory categories (tag mode still allows custom values).
const memoryCategoryOptions = ['preference', 'fact', 'decision', 'pattern', 'instruction']
  .map((c) => ({ label: c, value: c }))

const memoryColumns = computed<DataTableColumns<AgentMemoryDto>>(() => [
  {
    key: 'content',
    title: t('detail.memory.content'),
    minWidth: 240,
    render: (row) => row.content,
  },
  {
    key: 'category',
    title: t('detail.memory.category'),
    width: 120,
    render: (row) => row.category || EMPTY_DASH,
  },
  {
    key: 'importance',
    title: t('detail.memory.importance'),
    width: 150,
    render: (row) =>
      h('div', { class: 'flex items-center gap-8px' }, [
        h(NProgress, {
          type: 'line',
          percentage: Math.round(row.importance * 100),
          showIndicator: false,
          height: 6,
          style: 'flex:1',
        }),
        h('span', { class: 'font-mono text-12px text-muted' }, row.importance.toFixed(2)),
      ]),
  },
  {
    key: 'accessCount',
    title: t('detail.memory.accessCount'),
    width: 90,
    align: 'right',
    render: (row) => row.accessCount,
  },
  {
    key: 'actions',
    title: t('detail.memory.actions'),
    width: 130,
    render: (row) =>
      can('ai.agent.update')
        ? h('div', { class: 'flex items-center gap-6px' }, [
            h(NButton, { size: 'tiny', tertiary: true, onClick: () => openMemoryEdit(row) }, { default: () => t('detail.memory.edit') }),
            h(
              NPopconfirm,
              { onPositiveClick: () => removeMemory(row) },
              {
                trigger: () => h(NButton, { size: 'tiny', type: 'error', ghost: true }, { default: () => t('detail.memory.delete') }),
                default: () => t('detail.memory.deleteConfirm'),
              },
            ),
          ])
        : null,
  },
])

async function loadMemory(id: string): Promise<void> {
  memoryLoading.value = true
  try {
    const result = await bridge.agents.getMemory(id, { pageIndex: 1, pageSize: 200, skip: 0, take: 200 })
    memoryRows.value = result.items
    memoryLoaded.value = true
  } catch (e) {
    toast('err', (e as Error).message ?? t('detail.memory.loadError'))
  } finally {
    memoryLoading.value = false
  }
}

function openMemoryCreate(): void {
  memoryModal.id = null
  memoryModal.content = ''
  memoryModal.category = ''
  memoryModal.importance = 0.5
  memoryModal.show = true
}

function openMemoryEdit(row: AgentMemoryDto): void {
  memoryModal.id = row.id
  memoryModal.content = row.content
  memoryModal.category = row.category ?? ''
  memoryModal.importance = row.importance
  memoryModal.show = true
}

async function saveMemory(): Promise<void> {
  if (!can('ai.agent.update')) return
  const id = currentRouteId()
  if (!id || !memoryModal.content.trim()) return
  memorySaving.value = true
  try {
    if (memoryModal.id) {
      await bridge.agents.updateMemory(id, memoryModal.id, {
        content: memoryModal.content.trim(),
        category: memoryModal.category.trim() || null,
        importance: memoryModal.importance,
      })
    } else {
      await bridge.agents.createMemory(id, {
        content: memoryModal.content.trim(),
        category: memoryModal.category.trim() || undefined,
        importance: memoryModal.importance,
      })
    }
    memoryModal.show = false
    await loadMemory(id)
  } catch (e) {
    toast('err', (e as Error).message ?? t('detail.memory.saveError'))
  } finally {
    memorySaving.value = false
  }
}

async function removeMemory(row: AgentMemoryDto): Promise<void> {
  if (!can('ai.agent.update')) return
  const id = currentRouteId()
  if (!id) return
  try {
    await bridge.agents.deleteMemory(id, row.id)
    await loadMemory(id)
  } catch (e) {
    toast('err', (e as Error).message ?? t('detail.memory.deleteError'))
  }
}

// ---- Versions -------------------------------------------------------------

async function loadVersions(id: string): Promise<void> {
  versionsLoading.value = true
  versionsError.value = null
  try {
    const result = await bridge.agents.getVersions(id, { pageIndex: 1, pageSize: 50, skip: 0, take: 50 })
    versions.value = result.items ?? []
    versionsLoaded.value = true
  } catch (e) {
    versionsError.value = (e as Error).message ?? t('detail.versions.loadError')
  } finally {
    versionsLoading.value = false
  }
}

function reloadVersions(): void {
  const id = currentRouteId()
  if (id) void loadVersions(id)
}

function toggleSnapshot(versionId: string): void {
  expandedVersion.value = expandedVersion.value === versionId ? null : versionId
}

function formatSnapshot(snapshot: string | null | undefined): string {
  if (!snapshot) return ''
  try {
    return JSON.stringify(JSON.parse(snapshot), null, 2)
  } catch {
    // Not JSON - show the raw stored snapshot verbatim.
    return snapshot
  }
}

async function handleRollback(version: number): Promise<void> {
  if (!can('ai.agent.update')) return
  const id = currentRouteId()
  if (!id) return
  rollingBackTo.value = version
  try {
    const updated = await bridge.agents.rollbackToVersion(id, version)
    agent.value = updated
    hydrateEdit(updated)
    toast('ok', t('detail.versions.rolledBack'))
    // Refresh detail + version history so the new snapshot row appears.
    await Promise.all([loadAgent(id), loadVersions(id)])
  } catch (e) {
    toast('err', (e as Error).message ?? t('detail.versions.rollbackError'))
  } finally {
    rollingBackTo.value = null
  }
}

// ---- A/B test -------------------------------------------------------------

async function handleStartAbTest(): Promise<void> {
  if (!can('ai.agent.update')) return
  const id = currentRouteId()
  if (!id || !abTestValid.value) return
  abStarting.value = true
  abStatus.value = null
  try {
    const updated = await bridge.agents.configureAbTest(id, {
      versionA: abTest.versionA as number,
      versionB: abTest.versionB as number,
      trafficPercentB: abTest.trafficPercentB,
    })
    agent.value = updated
    hydrateEdit(updated)
    abStatus.value = { kind: 'ok', message: t('detail.abtest.started') }
    toast('ok', t('detail.abtest.started'))
  } catch (e) {
    const msg = (e as Error).message ?? t('detail.abtest.startError')
    abStatus.value = { kind: 'err', message: msg }
    toast('err', msg)
  } finally {
    abStarting.value = false
  }
}

async function handleStopAbTest(): Promise<void> {
  if (!can('ai.agent.update')) return
  const id = currentRouteId()
  if (!id) return
  abStopping.value = true
  abStatus.value = null
  try {
    const updated = await bridge.agents.stopAbTest(id)
    agent.value = updated
    hydrateEdit(updated)
    abStatus.value = { kind: 'ok', message: t('detail.abtest.stopped') }
    toast('ok', t('detail.abtest.stopped'))
  } catch (e) {
    const msg = (e as Error).message ?? t('detail.abtest.stopError')
    abStatus.value = { kind: 'err', message: msg }
    toast('err', msg)
  } finally {
    abStopping.value = false
  }
}

// ---- Validation -----------------------------------------------------------

async function runValidation(): Promise<void> {
  const id = currentRouteId()
  if (!id) return
  validating.value = true
  validationError.value = null
  try {
    validation.value = await bridge.agents.validate(id)
  } catch (e) {
    validationError.value = (e as Error).message ?? t('detail.health.error')
  } finally {
    validating.value = false
  }
}

function resetEdits(): void {
  if (agent.value) hydrateEdit(agent.value)
}

async function handleSave(): Promise<void> {
  if (!can('ai.agent.update')) return
  if (!agent.value || !isDirty.value) return
  saving.value = true
  saveStatus.value = null
  try {
    // PATCH-style: only emit keys whose value differs from the loaded snapshot.
    // Persona is applied immediately via the Persona panel (not this save-bar).
    const patch: UpdateAgentDto = {}
    const o = original.value
    if (edit.name !== o.name) patch.name = edit.name
    if (edit.description !== o.description) patch.description = edit.description || null
    if (edit.instructions !== o.instructions) patch.instructions = edit.instructions || null
    if (edit.provider !== o.provider) patch.provider = edit.provider
    if (edit.model !== o.model) patch.model = edit.model || null
    if (edit.isEnabled !== o.isEnabled) patch.isEnabled = edit.isEnabled
    if (edit.temperature !== o.temperature) patch.temperature = edit.temperature
    if (edit.maxTokens !== o.maxTokens) patch.maxTokens = edit.maxTokens
    if (edit.qualityTier !== o.qualityTier) patch.qualityTier = edit.qualityTier
    if (edit.latencyTier !== o.latencyTier) patch.latencyTier = edit.latencyTier
    if (edit.costTier !== o.costTier) patch.costTier = edit.costTier
    if (arraysDiffer(edit.domains, o.domains)) patch.domains = [...edit.domains]
    if (arraysDiffer(edit.roles, o.roles)) patch.roles = [...edit.roles]
    const updated = await bridge.agents.update(agent.value.id, patch)
    agent.value = updated
    hydrateEdit(updated)
    saveStatus.value = { kind: 'ok', message: t('detail.saved') }
  } catch (e) {
    saveStatus.value = { kind: 'err', message: (e as Error).message ?? 'Save failed' }
  } finally {
    saving.value = false
  }
}

async function savePersona(content: string): Promise<void> {
  // Persona persists immediately (like resource assignments), not via the Basic
  // Info save-bar. Empty string clears the inline soul content.
  personaSaving.value = true
  try {
    await persistResource({ persona: content }, t('detail.persona.saved'))
  } finally {
    personaSaving.value = false
  }
}

function runStatusType(status: unknown): 'success' | 'error' | 'warning' | 'info' | 'default' {
  switch (String(status)) {
    case 'Completed': return 'success'
    case 'Failed': return 'error'
    case 'Cancelled': return 'warning'
    case 'Running':
    case 'Pending':
    case 'AwaitingApproval':
    case 'RequiresClarification': return 'info'
    default: return 'default'
  }
}

/**
 * i18n label for an AgentRunStatus member name. The backend serializes the enum
 * as its PascalCase name; `t` humanises the key on a dictionary miss so the
 * label reads (e.g. "Awaiting Approval") even before locale entries land.
 */
function runStatusLabel(status: unknown): string {
  const s = String(status ?? '')
  if (!s) return ''
  return t(`runStatus.${s.charAt(0).toLowerCase()}${s.slice(1)}`)
}

function formatTime(v?: string | Date | null): string {
  if (!v) return ''
  try { return new Date(v).toLocaleString() } catch { return '' }
}

function currentRouteId(): string | null {
  const raw = route.params?.id
  if (Array.isArray(raw)) return raw[0] ?? null
  return typeof raw === 'string' && raw.length > 0 ? raw : null
}

onMounted(async () => {
  const id = currentRouteId()
  if (!id) {
    loadError.value = 'Missing agent id in route'
    return
  }
  // Catalogs (providers/knowledge/skills/tool-groups) don't depend on the
  // agent - load in parallel with the agent fetch to keep first-paint snappy.
  await Promise.all([
    loadAgent(id),
    loadProviderOptions(),
    loadKnowledgeBases(),
    loadSkills(),
    loadToolGroups(),
  ])
  if (agent.value) {
    void loadRecentRuns(id)
    // Populate the model dropdown for the agent's current provider.
    void loadModels(agent.value.provider)
    // Deep-link landing directly on a lazy section (?section=versions|validation|memory)
    // - the activeSection watcher only fires on change, so kick the loader once.
    maybeLoadSection(activeSection.value)
  }
})

watch(() => route.params?.id, async (next, prev) => {
  if (next === prev) return
  const id = currentRouteId()
  if (!id) return
  // Clear per-agent lazy state so the new agent doesn't show the prior one's
  // version history / validation result before its own loads.
  versions.value = []
  versionsLoaded.value = false
  versionsError.value = null
  expandedVersion.value = null
  validation.value = null
  validationError.value = null
  abStatus.value = null
  await loadAgent(id)
  if (agent.value) {
    void loadRecentRuns(id)
    maybeLoadSection(activeSection.value)
  }
})

// Test affordance - mirror TDetailLayout's `defineExpose({ onSection })` so the
// integration test can drive section activation + the production-grade handlers
// (rollback / A-B / validation) without simulating naive-ui's internal tab DOM.
defineExpose({
  sections,
  activeSection,
  setSection: (k: string) => { activeSection.value = k },
  handleRollback,
  handleStartAbTest,
  handleStopAbTest,
  runValidation,
  reloadVersions,
})
</script>

<style scoped>
/* Right content panel - fills TDetailLayout's white (overflow:hidden) panel.
   Mirrors UserCenter: the panel never scrolls; each section owns a fixed
   header bar (title + section actions) above a body that claims the residual
   height and scrolls. */
.t-agent-detail__panel {
  flex: 1 1 auto;
  min-height: 0;
  display: flex;
  flex-direction: column;
}
.t-agent-detail__panel :deep(.n-spin-container),
.t-agent-detail__panel :deep(.n-spin-content) {
  display: flex;
  flex-direction: column;
  flex: 1 1 auto;
  min-height: 0;
}
/* Section chrome (header bar + body + save bar) now lives in the shared
   TDetailSection component - keeps every section pixel-identical and fixes the
   scoped-CSS-doesn't-cross-components bug that broke the resource pickers. */
/* Sub-group heading inside the Basic Info page (Identity / Model / Capabilities). */
.t-agent-detail__subhead {
  font-size: 11px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.6px;
  color: var(--tnzi-base-text-muted, #888);
  margin: 0 0 10px;
  padding-bottom: 6px;
  border-bottom: 1px solid var(--tnzi-border);
}
.t-agent-detail__subhead:not(:first-child) {
  margin-top: 20px;
}
.t-agent-detail__memory-table {
  flex: 1 1 auto;
  min-height: 0;
}
/* Knowledge retrieval-test panel (prepend slot of the KB picker). */
.t-agent-detail__kb-test {
  display: flex;
  flex-direction: column;
  gap: 10px;
  padding: 12px;
  margin-bottom: 14px;
  border: 1px solid var(--tnzi-border);
  border-radius: var(--tnzi-admin-radius-md, 8px);
  background: var(--tnzi-bg-deep, #f6f8fa);
}
.t-agent-detail__kb-hits {
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.t-agent-detail__kb-hit {
  padding: 8px 10px;
  border: 1px solid var(--tnzi-border);
  border-radius: 6px;
  background: var(--tnzi-admin-card-bg, var(--tnzi-container-bg, #fff));
}
.t-agent-detail__kb-hit-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  margin-bottom: 6px;
}
.t-agent-detail__kb-hit-src {
  font-size: 12px;
  font-weight: 500;
  color: var(--tnzi-base-text);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.t-agent-detail__kb-hit-score {
  font-size: 12px;
  font-weight: 600;
  color: var(--tnzi-primary, #6d5ce7);
  flex-shrink: 0;
}
.t-agent-detail__kb-hit-text {
  margin-top: 6px;
  font-size: 12px;
  line-height: 1.5;
  color: var(--tnzi-base-text-muted, #555);
  white-space: pre-wrap;
  word-break: break-word;
  display: -webkit-box;
  -webkit-line-clamp: 3;
  -webkit-box-orient: vertical;
  overflow: hidden;
}
/* Pinned save-feedback bar at the panel bottom. */
.t-agent-detail__status-bar {
  flex-shrink: 0;
  padding: 8px 16px;
  border-top: 1px solid var(--tnzi-border);
  font-size: 12px;
}
.t-agent-detail__status-bar[data-state="ok"] { color: var(--tnzi-success); }
.t-agent-detail__status-bar[data-state="err"] { color: var(--tnzi-error); }
.t-agent-detail__head {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
}
.t-agent-detail__head-meta {
  font-size: 12px;
  color: var(--tnzi-base-text-muted, #888);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.t-agent-detail__name {
  font-size: 16px;
  font-weight: 600;
  color: var(--tnzi-base-text);
}
.t-agent-detail__hint {
  color: var(--tnzi-base-text-muted);
  font-size: 12px;
}
.t-agent-detail__runs-list {
  list-style: none;
  padding: 0;
  margin: 0;
  max-height: 480px;
  overflow: auto;
}
.t-agent-detail__runs-list li {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 4px 0;
  font-size: 12px;
}
.t-agent-detail__runs-time {
  margin-left: auto;
  color: var(--tnzi-base-text-muted);
}
.t-agent-detail__error {
  color: var(--tnzi-error);
  font-size: 13px;
  margin: 12px 0;
}
.t-agent-detail__placeholder {
  color: var(--tnzi-base-text-muted);
  text-align: center;
  padding: 60px 16px;
}
.t-agent-detail__status {
  margin-top: 12px;
  font-size: 12px;
}
.t-agent-detail__status[data-state="ok"] {
  color: var(--tnzi-success);
}
.t-agent-detail__status[data-state="err"] {
  color: var(--tnzi-error);
}

/* Versions / A-B / Validation sections */
.t-agent-detail__version {
  display: flex;
  flex-direction: column;
  gap: 6px;
  width: 100%;
}
.t-agent-detail__version-head {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 12px;
}
.t-agent-detail__version-note {
  color: var(--tnzi-base-text);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.t-agent-detail__version-time {
  margin-left: auto;
  color: var(--tnzi-base-text-muted);
  flex-shrink: 0;
}
.t-agent-detail__version-actions {
  display: flex;
  align-items: center;
  gap: 8px;
}
.t-agent-detail__snapshot {
  margin: 4px 0 0;
  padding: 8px 10px;
  background: var(--tnzi-layout-bg);
  border-radius: 3px;
  font-size: 11px;
  max-height: 240px;
  overflow: auto;
  white-space: pre-wrap;
  word-break: break-all;
}
.t-agent-detail__ab-traffic {
  display: flex;
  align-items: center;
  gap: 12px;
  width: 100%;
}
.t-agent-detail__check {
  display: flex;
  align-items: flex-start;
  gap: 10px;
  width: 100%;
}
.t-agent-detail__check-body {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}
.t-agent-detail__check-name {
  font-size: 13px;
  font-weight: 500;
  color: var(--tnzi-base-text);
}
.t-agent-detail__check-details {
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
}
</style>
