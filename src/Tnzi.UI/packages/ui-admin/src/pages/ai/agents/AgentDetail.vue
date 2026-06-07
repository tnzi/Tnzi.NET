<template>
  <TDetailLayout
    layout="tabs"
    :sections="sections"
    v-model:active-section="activeSection"
    :back="'/admin/ai/agents'"
    :translate="t"
  >
    <template #title>
      <span class="t-agent-detail__name">{{ agent?.name ?? '—' }}</span>
      <NTag
        v-if="agent"
        size="small"
        :type="agent.isEnabled ? 'success' : 'warning'"
        :bordered="false"
      >
        {{ agent.isEnabled ? t('detail.enabled') : t('detail.disabled') }}
      </NTag>
    </template>

    <template #actions>
      <NButton size="small" :disabled="!isDirty" @click="resetEdits">
        {{ t('detail.reset') }}
      </NButton>
      <NButton size="small" type="primary" :loading="saving" :disabled="!isDirty" @click="handleSave">
        {{ t('detail.save') }}
      </NButton>
    </template>

    <template #default="{ section }">
      <NSpin :show="loading">
        <div v-if="loadError" class="t-agent-detail__error" role="alert">{{ loadError }}</div>
        <div v-else-if="!agent" class="t-agent-detail__placeholder">—</div>
        <template v-else>
          <!-- Identity -->
          <NCard v-if="section === 'identity'" size="small" :bordered="false">
            <NForm label-placement="left" label-width="100px">
              <NFormItem :label="t('form.name')" required>
                <NInput v-model:value="edit.name" />
              </NFormItem>
              <NFormItem :label="t('form.description')">
                <NInput v-model:value="edit.description" type="textarea" :rows="2" />
              </NFormItem>
              <NFormItem :label="t('form.isEnabled')">
                <NSwitch v-model:value="edit.isEnabled" />
              </NFormItem>
            </NForm>
          </NCard>

          <!-- Provider / Model -->
          <NCard v-else-if="section === 'provider'" size="small" :bordered="false">
            <NForm label-placement="left" label-width="100px">
              <NFormItem :label="t('form.provider')" required>
                <NSelect
                  v-model:value="edit.provider"
                  :options="providerOptions"
                  filterable
                  tag
                  :placeholder="t('detail.providerPlaceholder')"
                />
              </NFormItem>
              <NFormItem :label="t('form.model')">
                <NInput v-model:value="edit.model" :placeholder="t('detail.modelPlaceholder')" />
              </NFormItem>
              <NFormItem :label="t('form.instructions')">
                <NInput
                  v-model:value="edit.instructions"
                  type="textarea"
                  :rows="3"
                  :placeholder="t('detail.instructionsPlaceholder')"
                />
              </NFormItem>
            </NForm>
          </NCard>

          <!-- Persona -->
          <NCard v-else-if="section === 'persona'" size="small" :bordered="false">
            <NSpace vertical size="small">
              <NSelect
                v-model:value="personaSelectValue"
                :options="personaOptions"
                :placeholder="t('detail.personaPlaceholder')"
                clearable
                filterable
                @update:value="onPersonaChange"
              />
              <div v-if="selectedPersona" class="t-agent-detail__persona-preview">
                <div class="t-agent-detail__persona-name">
                  {{ selectedPersona.name }}
                  <NTag v-if="selectedPersona.isSystem" size="tiny" :bordered="false">
                    {{ t('detail.system') }}
                  </NTag>
                </div>
                <code class="t-agent-detail__persona-slug">{{ selectedPersona.slug }}</code>
                <pre v-if="selectedPersona.content" class="t-agent-detail__persona-content">{{ selectedPersona.content }}</pre>
              </div>
              <div v-else class="t-agent-detail__hint">
                {{ t('detail.personaHint') }}
              </div>
            </NSpace>
          </NCard>

          <!-- Tools -->
          <NCard v-else-if="section === 'tools'" size="small" :bordered="false">
            <NForm label-placement="left" label-width="100px">
              <NFormItem :label="t('form.toolGroups')">
                <NDynamicTags v-model:value="toolGroupsModel" />
              </NFormItem>
              <NFormItem :label="t('form.temperature')">
                <NInputNumber
                  v-model:value="edit.temperature"
                  :step="0.1"
                  :min="0"
                  :max="2"
                  clearable
                  class="w-140px"
                />
              </NFormItem>
              <NFormItem :label="t('form.maxTokens')">
                <NInputNumber v-model:value="edit.maxTokens" :min="0" clearable class="w-140px" />
              </NFormItem>
            </NForm>
          </NCard>

          <!-- Recent Runs -->
          <NCard v-else-if="section === 'runs'" size="small" :bordered="false">
            <div v-if="runsError" class="t-agent-detail__error">{{ runsError }}</div>
            <ul v-else-if="recentRuns.length" class="t-agent-detail__runs-list">
              <li v-for="run in recentRuns" :key="run.id">
                <router-link :to="`/admin/ai/agents/${agent?.id}/runs/${run.id}`">
                  <code>{{ run.id.slice(0, 8) }}</code>
                </router-link>
                <NTag size="tiny" :type="runStatusType(run.status)" :bordered="false">
                  {{ run.status }}
                </NTag>
                <span class="t-agent-detail__runs-time">{{ formatTime(run.creationTime) }}</span>
              </li>
            </ul>
            <div v-else class="t-agent-detail__hint">{{ t('detail.noRuns') }}</div>
          </NCard>
        </template>
      </NSpin>

      <div v-if="saveStatus" class="t-agent-detail__status" :data-state="saveStatus.kind">
        {{ saveStatus.message }}
      </div>
    </template>
  </TDetailLayout>
</template>

<script setup lang="ts">
import { computed, reactive, ref, watch, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  NSpin, NForm, NFormItem, NInput, NInputNumber, NSwitch, NSelect, NTag, NButton,
  NSpace, NDynamicTags, NCard,
} from 'naive-ui'
import TDetailLayout from '../../../components/detail/TDetailLayout.vue'
import type { DetailSection } from '../../../headless/useDetail'
import { translatePageKey } from '../../_shared/translate'
import { createAiBridge } from '../../../services/bridges/ai-bridge'
import { useAdminClient } from '../../../plugin/client'
import type {
  AgentDto, UpdateAgentDto, AgentRunDto, AgentPersonaDto, ProviderDto,
} from '@tnzi/core/services/ai'

const route = useRoute()
const router = useRouter()
const bridge = createAiBridge({ client: useAdminClient() })
const t = (key: string) => translatePageKey('ai.agents', key)

// ---- Sections ----------------------------------------------------------------

const sections: DetailSection[] = [
  { key: 'identity', label: t('detail.panels.identity'), icon: 'mdi:information-outline' },
  { key: 'provider', label: t('detail.panels.provider'), icon: 'mdi:tune' },
  { key: 'persona', label: t('detail.panels.persona'), icon: 'mdi:account-circle-outline' },
  { key: 'tools', label: t('detail.panels.tools'), icon: 'mdi:wrench-outline' },
  { key: 'runs', label: t('detail.panels.runs'), icon: 'mdi:play-circle-outline' },
]

const activeSection = ref<string>((route.query.section as string) || 'identity')

watch(activeSection, (k) => {
  void router.replace({ query: { ...route.query, section: k } })
})

// ---- State ----------------------------------------------------------------

const agent = ref<AgentDto | null>(null)
const personas = ref<AgentPersonaDto[]>([])
const providers = ref<ProviderDto[]>([])
const recentRuns = ref<AgentRunDto[]>([])
const loading = ref(false)
const saving = ref(false)
const loadError = ref<string | null>(null)
const runsError = ref<string | null>(null)
const saveStatus = ref<{ kind: 'ok' | 'err'; message: string } | null>(null)

interface EditState {
  name: string
  description: string
  instructions: string
  provider: string
  model: string
  isEnabled: boolean
  temperature: number | null
  maxTokens: number | null
  toolGroups: string[]
  personaId: string | null
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
    toolGroups: [],
    personaId: null,
  }
}

const edit = reactive<EditState>(blankEdit())
const original = ref<EditState>(blankEdit())

// NDynamicTags needs a non-readonly array; sync via a computed getter/setter.
const toolGroupsModel = computed<string[]>({
  get: () => edit.toolGroups,
  set: (next) => { edit.toolGroups = [...next] },
})

// NSelect rejects null — represent "no persona" with empty string and translate
// it back to null on writes.
const personaSelectValue = computed<string>({
  get: () => edit.personaId ?? '',
  set: (next) => { edit.personaId = next === '' ? null : next },
})

const providerOptions = computed(() =>
  providers.value.map((p) => ({ label: p.name, value: p.name })),
)

const personaOptions = computed(() => {
  const opts: Array<{ label: string; value: string }> = []
  for (const p of personas.value) {
    opts.push({ label: p.isSystem ? `★ ${p.name}` : p.name, value: p.id })
  }
  return opts
})

const selectedPersona = computed(() =>
  edit.personaId ? personas.value.find((p) => p.id === edit.personaId) ?? null : null,
)

const isDirty = computed(() => {
  const keys: (keyof EditState)[] = [
    'name', 'description', 'instructions', 'provider', 'model',
    'isEnabled', 'temperature', 'maxTokens', 'personaId',
  ]
  for (const k of keys) {
    if (edit[k] !== original.value[k]) return true
  }
  // Tool groups: shallow array compare (order matters in backend).
  const a = edit.toolGroups
  const b = original.value.toolGroups
  if (a.length !== b.length) return true
  for (let i = 0; i < a.length; i++) {
    if (a[i] !== b[i]) return true
  }
  return false
})

// ---- Loaders --------------------------------------------------------------

async function loadAgent(id: string): Promise<void> {
  loading.value = true
  loadError.value = null
  try {
    // bridge.agents has no getById — degrade to filtered fetch + pick.
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
    toolGroups: [...(src.toolGroups ?? [])],
    personaId: src.personaId ?? null,
  }
  Object.assign(edit, next)
  original.value = JSON.parse(JSON.stringify(next))
}

async function loadProviders(): Promise<void> {
  try {
    const result = await bridge.providers.fetch({
      pageIndex: 1,
      pageSize: 100,
      sortField: 'name',
      sortOrder: 'asc' as const,
      searchText: '',
      filters: {},
    })
    // bridge.providers.fetch returns ProviderRow (an alias of ProviderDto in
    // ai-bridge) — the shape is structurally compatible with ProviderDto for
    // the (name) field we read for the picker.
    providers.value = result.items as unknown as ProviderDto[]
  } catch {
    providers.value = []
  }
}

async function loadPersonas(): Promise<void> {
  try {
    const result = await bridge.personas.fetch({
      pageIndex: 1,
      pageSize: 200,
      sortField: 'name',
      sortOrder: 'asc' as const,
      searchText: '',
      filters: {},
    })
    personas.value = result.items
  } catch {
    personas.value = []
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

function resetEdits(): void {
  if (agent.value) hydrateEdit(agent.value)
}

async function handleSave(): Promise<void> {
  if (!agent.value || !isDirty.value) return
  saving.value = true
  saveStatus.value = null
  try {
    // PATCH-style: only emit keys whose value differs from the loaded snapshot.
    // PersonaId uses Guid.Empty as the "clear" sentinel (the backend accepts
    // empty as null). The other optional fields use a regular `undefined`-skip.
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
    if (toolGroupsChanged()) patch.toolGroups = [...edit.toolGroups]
    if (edit.personaId !== o.personaId) {
      patch.personaId = edit.personaId ?? '00000000-0000-0000-0000-000000000000'
    }
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

function toolGroupsChanged(): boolean {
  const a = edit.toolGroups
  const b = original.value.toolGroups
  if (a.length !== b.length) return true
  for (let i = 0; i < a.length; i++) {
    if (a[i] !== b[i]) return true
  }
  return false
}

function onPersonaChange(_v: string | null): void {
  // The computed setter already syncs edit.personaId; nothing extra to do here.
}

function runStatusType(status: unknown): 'success' | 'error' | 'warning' | 'info' | 'default' {
  switch (status) {
    case 'Completed': return 'success'
    case 'Failed': return 'error'
    case 'Cancelled': return 'warning'
    case 'Running':
    case 'Pending': return 'info'
    default: return 'default'
  }
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
  // Providers + personas don't depend on the agent — load in parallel with the
  // agent fetch to keep first-paint snappy.
  await Promise.all([loadAgent(id), loadProviders(), loadPersonas()])
  if (agent.value) void loadRecentRuns(id)
})

watch(() => route.params?.id, async (next, prev) => {
  if (next === prev) return
  const id = currentRouteId()
  if (!id) return
  await loadAgent(id)
  if (agent.value) void loadRecentRuns(id)
})
</script>

<style scoped>
.t-agent-detail__name {
  font-size: 16px;
  font-weight: 600;
  color: var(--tnzi-base-text);
}
.t-agent-detail__hint {
  color: var(--tnzi-base-text-muted);
  font-size: 12px;
}
.t-agent-detail__persona-preview {
  padding: 8px 10px;
  border: 1px dashed var(--tnzi-border);
  border-radius: var(--tnzi-admin-radius-md, 4px);
}
.t-agent-detail__persona-name {
  font-weight: 500;
  display: flex;
  align-items: center;
  gap: 6px;
}
.t-agent-detail__persona-slug {
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
  font-size: 11px;
  color: var(--tnzi-base-text-muted);
}
.t-agent-detail__persona-content {
  margin: 6px 0 0;
  padding: 6px 8px;
  background: var(--tnzi-layout-bg);
  border-radius: 3px;
  font-size: 11px;
  max-height: 140px;
  overflow: auto;
  white-space: pre-wrap;
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
</style>
