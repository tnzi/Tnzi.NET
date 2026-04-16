<template>
  <!--
    AgentDetail — Phase 5 Task 5.3 master-detail page.
    NOT a TCrudPage. Single-record view loaded by route :id, with NTabs for
    nested aspects (overview editor + versions/runs/skills sub-views).

    Lessons applied:
      - 1: setActivePinia in tests (translatePageKey reads admin app store)
      - 2: real DTOs from @tnzi/core/services/ai (AgentDto / UpdateAgentDto)
      - 7: inline status banner instead of useMessage
      - 10: toUpdateDto adapter so partial Overview edits don't lose fields
      - 12: do NOT invent missing bridge methods (versions tab is placeholder)
      - 14: provider stays free-text — no enum lockdown

    NEW lesson (15): single-record fetch via filtered list — when the bridge
    contract has no getById, use bridge.<resource>.fetch with filters:{id} and
    pick items[0]; do NOT add a stub method to the bridge for one page.
  -->
  <div class="t-agent-detail">
    <header class="t-agent-detail__header">
      <router-link to="/admin/ai/agents" class="t-agent-detail__back">
        {{ t('detail.backToList') }}
      </router-link>
      <h2 class="t-agent-detail__title">
        {{ agent?.name ?? '—' }}
      </h2>
      <span
        v-if="agent"
        class="t-agent-detail__badge"
        :class="{ 'is-enabled': agent.isEnabled }"
      >
        {{ agent.isEnabled ? 'Enabled' : 'Disabled' }}
      </span>
    </header>

    <div v-if="loadError" class="t-agent-detail__error" role="alert">
      {{ loadError }}
    </div>
    <div v-else-if="loading" class="t-agent-detail__loading">Loading…</div>

    <NTabs v-else-if="agent" v-model:value="activeTab" type="line">
      <NTabPane name="overview" :tab="t('detail.tabs.overview')">
        <div class="t-agent-detail__overview">
          <TFormSchemaRenderer
            :schema="agentOverviewSchema"
            :model="overviewModelTyped"
            :readonly="false"
          />
          <div v-if="saveStatus" class="t-agent-detail__status" :data-state="saveStatus.kind">
            {{ saveStatus.message }}
          </div>
          <button
            type="button"
            class="t-agent-detail__save"
            :disabled="saving"
            @click="handleSave"
          >
            {{ saving ? '…' : t('detail.save') }}
          </button>
        </div>
      </NTabPane>

      <NTabPane name="versions" :tab="t('detail.tabs.versions')">
        <div class="t-agent-detail__placeholder">
          {{ t('detail.versionsPlaceholder') }}
        </div>
      </NTabPane>

      <NTabPane name="runs" :tab="t('detail.tabs.runs')">
        <div class="t-agent-detail__runs">
          <div v-if="runsError" class="t-agent-detail__error">{{ runsError }}</div>
          <ul v-else-if="recentRuns.length" class="t-agent-detail__runs-list">
            <li v-for="run in recentRuns" :key="run.id">
              <!-- TODO(phase-5.4): link to RunMonitor route once Task 5.4 lands -->
              <router-link :to="`/admin/ai/runs/${run.id}`">{{ run.id }}</router-link>
              <span>{{ run.status }}</span>
            </li>
          </ul>
          <div v-else class="t-agent-detail__placeholder">No recent runs.</div>
        </div>
      </NTabPane>

      <NTabPane name="skills" :tab="t('detail.tabs.skills')">
        <div class="t-agent-detail__skills">
          <div v-if="skillsError" class="t-agent-detail__error">{{ skillsError }}</div>
          <ul v-else-if="agentSkills.length" class="t-agent-detail__skills-list">
            <li v-for="skill in agentSkills" :key="skill.id">{{ skill.name ?? skill.id }}</li>
          </ul>
          <div v-else class="t-agent-detail__placeholder">
            {{ t('detail.skillsPlaceholder') }}
          </div>
        </div>
      </NTabPane>
    </NTabs>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted, watch } from 'vue'
import { useRoute } from 'vue-router'
import { NTabs, NTabPane } from 'naive-ui'
import TFormSchemaRenderer from '../../_shared/form-schema'
import { translatePageKey } from '../../_shared/translate'
import { createAiBridge } from '../../../services/bridges/ai-bridge'
import { useAdminClient } from '../../../plugin/client'
import { agentDetailTabs, agentOverviewSchema } from './agent-detail-config'
import type { AgentDto, UpdateAgentDto, AgentRunDto } from '@tnzi/core/services/ai'

// Suppress the "tabs is exported but unused" lint — referenced by the locale
// key sweep in Task 5.16 and kept here so the i18n keys are discoverable.
void agentDetailTabs

const route = useRoute()
const bridge = createAiBridge({ client: useAdminClient() })

const t = (key: string) => translatePageKey('ai.agents', key)

const agent = ref<AgentDto | null>(null)
const loading = ref(false)
const loadError = ref<string | null>(null)
const activeTab = ref<string>('overview')

// Mutable model bound to the Overview form. Kept separate from `agent` so the
// pristine fetched copy can be diffed when building the partial UpdateAgentDto.
const overviewModel = reactive<Record<string, unknown>>({})
// Lesson 6 alias — Vue templates can't use TS `as` casts; expose a
// pre-typed reference so the template binding stays plain.
const overviewModelTyped = overviewModel as Record<string, unknown>

const saving = ref(false)
const saveStatus = ref<{ kind: 'ok' | 'err'; message: string } | null>(null)

const recentRuns = ref<AgentRunDto[]>([])
const runsError = ref<string | null>(null)
const agentSkills = ref<Array<{ id: string; name?: string | null }>>([])
const skillsError = ref<string | null>(null)

function syncOverviewModel(src: AgentDto) {
  for (const key of Object.keys(overviewModel)) delete overviewModel[key]
  Object.assign(overviewModel, {
    name: src.name,
    description: src.description ?? '',
    provider: src.provider,
    model: src.model ?? '',
    instructions: src.instructions ?? '',
    isEnabled: src.isEnabled,
  })
}

async function loadAgent(id: string) {
  loading.value = true
  loadError.value = null
  try {
    // Lesson 15: bridge has no agents.getById — degrade to filtered fetch.
    const result = await bridge.agents.fetch({
      pageIndex: 1,
      pageSize: 1,
      sortField: undefined,
      sortOrder: undefined,
      searchText: '',
      filters: { id },
    })
    const found = result.items.find((a) => a.id === id) ?? result.items[0] ?? null
    if (!found) {
      loadError.value = t('detail.loadError')
      return
    }
    agent.value = found
    syncOverviewModel(found)
  } catch (e) {
    loadError.value = (e as Error).message ?? t('detail.loadError')
  } finally {
    loading.value = false
  }
}

async function loadRecentRuns(id: string) {
  runsError.value = null
  try {
    const result = await bridge.agentRuns.fetch({
      pageIndex: 1,
      pageSize: 10,
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

async function loadSkills() {
  skillsError.value = null
  try {
    // bridge.skills.fetch has no agentId filter on the backend yet — show top
    // skills as a placeholder list. Real per-agent filtering arrives in a
    // future phase; until then this tab is informational only.
    const result = await bridge.skills.fetch({
      pageIndex: 1,
      pageSize: 20,
      sortField: undefined,
      sortOrder: undefined,
      searchText: '',
      filters: {},
    })
    agentSkills.value = result.items.map((s) => ({
      id: (s as { id: string }).id,
      name: (s as { name?: string }).name,
    }))
  } catch (e) {
    skillsError.value = (e as Error).message ?? 'Failed to load skills'
  }
}

/**
 * Lesson 10 adapter — produce the partial UpdateAgentDto from the Overview
 * model so we don't accidentally clobber server-managed fields (executionMode,
 * tiers, timeoutSeconds, etc.) when saving.
 */
function toUpdateDto(model: Record<string, unknown>): UpdateAgentDto {
  return {
    name: model.name as string,
    description: (model.description as string) || null,
    provider: model.provider as string,
    model: (model.model as string) || null,
    instructions: (model.instructions as string) || null,
    isEnabled: model.isEnabled as boolean,
  } as UpdateAgentDto
}

async function handleSave() {
  if (!agent.value) return
  saving.value = true
  saveStatus.value = null
  try {
    const updated = await bridge.agents.update(agent.value.id, toUpdateDto(overviewModel))
    agent.value = updated
    syncOverviewModel(updated)
    saveStatus.value = { kind: 'ok', message: t('detail.saved') }
  } catch (e) {
    saveStatus.value = { kind: 'err', message: (e as Error).message ?? 'Save failed' }
  } finally {
    saving.value = false
  }
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
  await loadAgent(id)
  if (agent.value) {
    // Fire sub-tab loads in parallel — they don't block the parent shell.
    void loadRecentRuns(id)
    void loadSkills()
  }
})

// If the route id changes (in-app navigation between two agents), refetch.
watch(
  () => route.params?.id,
  async (next, prev) => {
    if (next === prev) return
    const id = currentRouteId()
    if (!id) return
    await loadAgent(id)
    if (agent.value) {
      void loadRecentRuns(id)
      void loadSkills()
    }
  },
)
</script>
