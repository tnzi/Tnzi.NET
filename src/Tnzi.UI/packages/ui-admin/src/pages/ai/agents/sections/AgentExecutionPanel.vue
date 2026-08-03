<template>
  <!--
    AgentExecutionPanel - the Execution section: does this agent run on the
    framework's built-in middleware pipeline, or on an external coding CLI
    (Claude Code, or any ACP-speaking runtime)?

    The whole section is a two-state form because that is exactly what the
    backend models: a binding row exists (external) or it does not (built-in).
    There is no third state and no per-agent flag - deleting the binding is what
    returns an agent to built-in execution.
  -->
  <TDetailSection
    :title="t('detail.panels.execution')"
    :icon="icon"
    :hint="t('detail.execution.hint')"
  >
    <NSpin :show="loading">
      <div class="t-agent-exec">
        <!-- Mode picker. Disabled with an explanation rather than hidden when
             no runtime is available: an admin who cannot find the option needs
             to be told why, not left guessing. -->
        <NRadioGroup v-model:value="mode" :disabled="!canEdit || !hasRuntimes" size="small">
          <NRadioButton value="builtIn">{{ t('detail.execution.modeBuiltIn') }}</NRadioButton>
          <NRadioButton value="external">{{ t('detail.execution.modeExternal') }}</NRadioButton>
        </NRadioGroup>

        <p class="t-agent-exec__lead">
          {{ mode === 'external' ? t('detail.execution.externalLead') : t('detail.execution.builtInLead') }}
        </p>

        <!-- No runtimes registered: the section is unusable, so say what to do
             about it instead of showing an empty dropdown. -->
        <NAlert
          v-if="!hasRuntimes"
          type="info"
          :closable="false"
          :title="t('detail.execution.noRuntimes')"
        >
          {{ t('detail.execution.noRuntimesHint') }}
        </NAlert>

        <NForm v-else-if="mode === 'external'" label-placement="left" label-width="150px">
          <NFormItem :label="t('detail.execution.runtime')" required>
            <NSelect
              v-model:value="form.cliRuntimeId"
              :options="runtimeOptions"
              :disabled="!canEdit"
              :placeholder="t('detail.execution.runtimePlaceholder')"
            />
          </NFormItem>

          <NFormItem :label="t('detail.execution.model')">
            <NInput
              v-model:value="form.model"
              :disabled="!canEdit"
              clearable
              :placeholder="t('detail.execution.modelPlaceholder')"
            />
          </NFormItem>

          <NFormItem :label="t('detail.execution.thinkingLevel')">
            <div class="t-agent-exec__field">
              <NInput
                v-model:value="form.thinkingLevel"
                :disabled="!canEdit"
                clearable
                :placeholder="t('detail.execution.thinkingLevelPlaceholder')"
              />
              <!-- Free text on purpose: each runtime has its own effort vocabulary
                   and the framework round-trips the value verbatim rather than
                   flattening them into a shared enum. -->
              <span class="t-agent-exec__hint">{{ t('detail.execution.thinkingLevelHint') }}</span>
            </div>
          </NFormItem>

          <div class="t-agent-exec__subhead">{{ t('detail.execution.contextGroup') }}</div>

          <NFormItem :label="t('detail.execution.injectInstructions')">
            <div class="t-agent-exec__field">
              <NSwitch v-model:value="form.injectAgentInstructions" :disabled="!canEdit" />
              <span class="t-agent-exec__hint">{{ t('detail.execution.injectInstructionsHint') }}</span>
            </div>
          </NFormItem>

          <NFormItem :label="t('detail.execution.materializeSkills')">
            <div class="t-agent-exec__field">
              <NSwitch v-model:value="form.materializeSkills" :disabled="!canEdit" />
              <span class="t-agent-exec__hint">{{ t('detail.execution.materializeSkillsHint') }}</span>
            </div>
          </NFormItem>

          <NFormItem :label="t('detail.execution.workDirectoryMode')">
            <div class="t-agent-exec__field">
              <NSelect
                v-model:value="form.workDirectoryMode"
                :options="workDirectoryOptions"
                :disabled="!canEdit"
                class="w-220px"
              />
              <span class="t-agent-exec__hint">{{ workDirectoryHint }}</span>
            </div>
          </NFormItem>

          <NFormItem
            v-if="form.workDirectoryMode === CliWorkDirectoryMode.UserProvided"
            :label="t('detail.execution.userWorkDirectory')"
            required
          >
            <NInput
              v-model:value="form.userWorkDirectory"
              :disabled="!canEdit"
              class="font-mono"
              :placeholder="t('detail.execution.userWorkDirectoryPlaceholder')"
            />
          </NFormItem>

          <div class="t-agent-exec__subhead">{{ t('detail.execution.advancedGroup') }}</div>

          <NFormItem :label="t('detail.execution.customArgs')">
            <div class="t-agent-exec__field">
              <NDynamicTags v-model:value="form.customArgs" :disabled="!canEdit" />
              <span class="t-agent-exec__hint">{{ t('detail.execution.customArgsHint') }}</span>
            </div>
          </NFormItem>

          <NFormItem :label="t('detail.execution.idleWatchdog')">
            <div class="t-agent-exec__field">
              <NInputNumber
                v-model:value="idleWatchdogMinutes"
                :disabled="!canEdit"
                :min="1"
                clearable
                class="w-140px"
              />
              <span class="t-agent-exec__hint">{{ t('detail.execution.idleWatchdogHint') }}</span>
            </div>
          </NFormItem>

          <NFormItem :label="t('detail.execution.mcpConfig')">
            <div class="t-agent-exec__field">
              <NInput
                v-model:value="form.mcpConfigJson"
                type="textarea"
                :rows="5"
                :disabled="!canEdit"
                class="font-mono"
                :placeholder="MCP_PLACEHOLDER"
              />
              <span v-if="mcpConfigError" class="t-agent-exec__error">{{ mcpConfigError }}</span>
              <span v-else class="t-agent-exec__hint">{{ t('detail.execution.mcpConfigHint') }}</span>
            </div>
          </NFormItem>
        </NForm>
      </div>
    </NSpin>

    <template #savebar>
      <!-- Unbinding is the only destructive action here and it is not a form
           field: it removes the binding row outright, so it gets its own
           confirm rather than riding on Save. -->
      <NPopconfirm v-if="canEdit && isBound" @positive-click="emit('unbind')">
        <template #trigger>
          <NButton size="small" type="error" ghost :loading="saving">
            {{ t('detail.execution.unbind') }}
          </NButton>
        </template>
        {{ t('detail.execution.unbindConfirm') }}
      </NPopconfirm>
      <NButton size="small" :disabled="!isDirty" @click="reset">{{ t('detail.reset') }}</NButton>
      <NButton
        v-if="canEdit"
        size="small"
        type="primary"
        :loading="saving"
        :disabled="!canSave"
        @click="save"
      >
        {{ t('detail.save') }}
      </NButton>
    </template>
  </TDetailSection>
</template>

<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import {
  NAlert,
  NButton,
  NDynamicTags,
  NForm,
  NFormItem,
  NInput,
  NInputNumber,
  NPopconfirm,
  NRadioButton,
  NRadioGroup,
  NSelect,
  NSpin,
  NSwitch,
} from 'naive-ui'
import TDetailSection from '../../../../components/detail/TDetailSection.vue'
import { makePageTranslator } from '../../../../i18n/translate'
import {
  CliWorkDirectoryMode,
  type CliAgentBindingDto,
  type CliRuntimeDto,
  type UpsertCliAgentBindingDto,
} from '../../../../services/bridges/cli-agent-bridge'

const MCP_PLACEHOLDER = '{\n  "mcpServers": {\n    "github": { "command": "gh-mcp", "args": ["serve"] }\n  }\n}'

interface Props {
  /** Current binding, or `null` when the agent runs on the built-in pipeline. */
  binding: CliAgentBindingDto | null
  /** Registered external runtimes to choose from. */
  runtimes: CliRuntimeDto[]
  loading?: boolean
  saving?: boolean
  /** Whether the viewer holds `ai.cliBinding.update`. */
  canEdit?: boolean
  icon?: string
}
const props = withDefaults(defineProps<Props>(), {
  loading: false,
  saving: false,
  canEdit: false,
  icon: undefined,
})

const emit = defineEmits<{
  /** Create or update the binding. */
  save: [input: UpsertCliAgentBindingDto]
  /** Remove the binding - the agent returns to built-in execution. */
  unbind: []
}>()

const t = makePageTranslator('ai.agents')

type ExecutionMode = 'builtIn' | 'external'

interface ExecutionForm {
  cliRuntimeId: string | null
  model: string | null
  thinkingLevel: string | null
  customArgs: string[]
  mcpConfigJson: string
  workDirectoryMode: CliWorkDirectoryMode
  userWorkDirectory: string | null
  injectAgentInstructions: boolean
  materializeSkills: boolean
  idleWatchdog: string | null
}

const mode = ref<ExecutionMode>('builtIn')
const form = reactive<ExecutionForm>(emptyForm())
let baseline = snapshot(mode.value, form)

function emptyForm(): ExecutionForm {
  return {
    cliRuntimeId: null,
    model: null,
    thinkingLevel: null,
    customArgs: [],
    mcpConfigJson: '',
    workDirectoryMode: CliWorkDirectoryMode.PerThread,
    userWorkDirectory: null,
    injectAgentInstructions: true,
    materializeSkills: true,
    idleWatchdog: null,
  }
}

function applyBinding(binding: CliAgentBindingDto | null): void {
  const next = emptyForm()
  if (binding) {
    next.cliRuntimeId = binding.cliRuntimeId
    next.model = binding.model ?? null
    next.thinkingLevel = binding.thinkingLevel ?? null
    next.customArgs = [...(binding.customArgs ?? [])]
    next.mcpConfigJson = binding.mcpConfigJson ?? ''
    next.workDirectoryMode = binding.workDirectoryMode
    next.userWorkDirectory = binding.userWorkDirectory ?? null
    next.injectAgentInstructions = binding.injectAgentInstructions
    next.materializeSkills = binding.materializeSkills
    next.idleWatchdog = binding.idleWatchdog ?? null
  }

  Object.assign(form, next)
  mode.value = binding ? 'external' : 'builtIn'
  baseline = snapshot(mode.value, form)
}

watch(() => props.binding, applyBinding, { immediate: true })

// A fresh binding needs a runtime; pre-selecting the only one there is saves a
// click without ever guessing between several.
watch(mode, (value) => {
  const only = props.runtimes.length === 1 ? props.runtimes[0] : null
  if (value === 'external' && !form.cliRuntimeId && only) {
    form.cliRuntimeId = only.id
  }
})

function snapshot(currentMode: ExecutionMode, value: ExecutionForm): string {
  return JSON.stringify({ mode: currentMode, ...value })
}

const hasRuntimes = computed(() => props.runtimes.length > 0)
const isBound = computed(() => props.binding != null)
const isDirty = computed(() => snapshot(mode.value, form) !== baseline)

const runtimeOptions = computed(() =>
  props.runtimes.map((runtime) => ({
    label: runtime.providerDisplayName
      ? `${runtime.name} · ${runtime.providerDisplayName}`
      : runtime.name,
    value: runtime.id,
  })),
)

const workDirectoryOptions = computed(() => [
  { label: t('detail.execution.workDirectoryPerThread'), value: CliWorkDirectoryMode.PerThread },
  { label: t('detail.execution.workDirectoryUserProvided'), value: CliWorkDirectoryMode.UserProvided },
  { label: t('detail.execution.workDirectoryPerRun'), value: CliWorkDirectoryMode.PerRun },
])

// The hint carries the part an admin cannot infer from the label: this setting is
// what decides whether the agent remembers the previous turn at all.
const workDirectoryHintKeys: Record<string, string> = {
  [CliWorkDirectoryMode.PerThread]: 'detail.execution.workDirectoryPerThreadHint',
  [CliWorkDirectoryMode.UserProvided]: 'detail.execution.workDirectoryUserProvidedHint',
  [CliWorkDirectoryMode.PerRun]: 'detail.execution.workDirectoryPerRunHint',
}

const workDirectoryHint = computed(() =>
  t(workDirectoryHintKeys[form.workDirectoryMode] ?? 'detail.execution.workDirectoryPerThreadHint'),
)

/**
 * Idle watchdog exposed in minutes, stored on the wire as a `hh:mm:ss` TimeSpan.
 *
 * The backend clamps a per-agent value to the deployment-wide one - it can only
 * tighten, never loosen - so this is a request, not an override.
 */
const idleWatchdogMinutes = computed<number | null>({
  get: () => parseMinutes(form.idleWatchdog),
  set: (minutes) => {
    form.idleWatchdog = minutes && minutes > 0 ? formatTimeSpan(minutes) : null
  },
})

function parseMinutes(value: string | null): number | null {
  if (!value) return null
  const parts = value.split(':')
  if (parts.length < 3) return null
  const hours = Number(parts[0])
  const minutes = Number(parts[1])
  if (Number.isNaN(hours) || Number.isNaN(minutes)) return null
  return hours * 60 + minutes
}

function formatTimeSpan(minutes: number): string {
  const whole = Math.floor(minutes)
  const hh = String(Math.floor(whole / 60)).padStart(2, '0')
  const mm = String(whole % 60).padStart(2, '0')
  return `${hh}:${mm}:00`
}

/**
 * Malformed MCP JSON is rejected here rather than at run time: the backend
 * fails closed to "no managed MCP servers", which looks like the agent silently
 * losing its tools an hour later instead of a typo in this box.
 */
const mcpConfigError = computed(() => {
  const raw = form.mcpConfigJson.trim()
  if (!raw) return null
  try {
    JSON.parse(raw)
    return null
  } catch {
    return t('detail.execution.mcpConfigInvalid')
  }
})

const canSave = computed(() => {
  if (!isDirty.value || props.saving) return false
  if (mode.value === 'builtIn') {
    // Switching back to built-in is an unbind; Save only means something when
    // there is a binding to remove.
    return isBound.value
  }

  if (!form.cliRuntimeId) return false
  if (mcpConfigError.value) return false
  if (
    form.workDirectoryMode === CliWorkDirectoryMode.UserProvided
    && !form.userWorkDirectory?.trim()
  ) {
    return false
  }

  return true
})

function reset(): void {
  applyBinding(props.binding)
}

function save(): void {
  if (mode.value === 'builtIn') {
    emit('unbind')
    return
  }

  emit('save', {
    cliRuntimeId: form.cliRuntimeId!,
    model: nullIfBlank(form.model),
    thinkingLevel: nullIfBlank(form.thinkingLevel),
    customArgs: form.customArgs.length ? [...form.customArgs] : null,
    mcpConfigJson: nullIfBlank(form.mcpConfigJson),
    workDirectoryMode: form.workDirectoryMode,
    userWorkDirectory: nullIfBlank(form.userWorkDirectory),
    injectAgentInstructions: form.injectAgentInstructions,
    materializeSkills: form.materializeSkills,
    idleWatchdog: form.idleWatchdog,
  })
}

function nullIfBlank(value: string | null): string | null {
  const trimmed = value?.trim()
  return trimmed ? trimmed : null
}
</script>

<style scoped>
.t-agent-exec {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.t-agent-exec__lead {
  margin: 0;
  font-size: 12.5px;
  color: var(--tnzi-base-text-muted, #888);
}

.t-agent-exec__subhead {
  margin-top: 4px;
  margin-bottom: 8px;
  font-size: 12px;
  font-weight: 600;
  color: var(--tnzi-text-3);
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

.t-agent-exec__field {
  display: flex;
  flex-direction: column;
  gap: 4px;
  width: 100%;
}

.t-agent-exec__hint {
  font-size: 12px;
  color: var(--tnzi-base-text-muted, #888);
}

.t-agent-exec__error {
  font-size: 12px;
  color: var(--tnzi-error);
}
</style>
