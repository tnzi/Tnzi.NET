<template>
  <TDetailBlock class="t-uld" icon="mdi:tune-variant" :title="t('defaults.title')" :hint="t('defaults.hint')">
    <NSpin :show="loading">
      <TEmpty v-if="!loading && !data" :text="t('loadFailed')" size="small" />
      <div v-else-if="data && draft" class="t-uld__body">
        <p class="t-uld__windows">
          {{
            t('defaults.windows', {
              session: formatWindowLength(data.configuration.sessionWindowMinutes, t),
              weekly: formatWindowLength(data.configuration.weeklyWindowMinutes, t),
              threshold: Math.round(data.configuration.warningThreshold * 100),
            })
          }}
        </p>

        <TUsageLimitPolicyForm
          v-model="draft"
          :inherited="inherited"
          :catalog="data.catalog"
          :inherit-label="t('mode.inheritConfiguration')"
          :session-window-minutes="data.configuration.sessionWindowMinutes"
          :weekly-window-minutes="data.configuration.weeklyWindowMinutes"
          :readonly="!canManage"
        />

        <p class="t-uld__effective">
          <span class="t-uld__effective-label">{{ t('policy.effective') }}</span>
          {{ effectiveText }}
        </p>

        <div v-if="canManage" class="t-uld__actions">
          <span v-if="problem" class="t-uld__problem">{{ t(problem) }}</span>
          <NButton :disabled="!dirty || saving" @click="discard">{{ t('policy.discard') }}</NButton>
          <NButton type="primary" :loading="saving" :disabled="!dirty || !!problem" @click="save">
            {{ t('policy.save') }}
          </NButton>
        </div>
      </div>
    </NSpin>
  </TDetailBlock>
</template>

<script setup lang="ts">
/**
 * `TUsageLimitDefaultsPanel` - everyone's usage limits and model policy: the
 * 5-hour session window and the weekly window (API-equivalent USD), the models
 * users may pick, and the one they start on.
 *
 * Applies to every user without settings of their own. An item set to "Follow
 * configuration" uses `AI:UsageLimits` from the deployment configuration, and
 * the form says what that currently is. Reads ride `ai.quota.view`, writes
 * `ai.quota.update`, which the panel checks itself.
 *
 * Hostable with no props (an "AI assistant" settings page, say): strings come
 * from the `ai.quota` namespace of the admin locale bundle, and it draws no
 * section chrome of its own beyond one block.
 */
import { computed, ref } from 'vue'
import { NButton, NSpin } from 'naive-ui'
import { TEmpty, useSafeMessage } from '@tnzi/ui'
import type { SaveUsagePolicyDto, UsageLimitDefaultsDto } from '@tnzi/core/services/ai'
import TDetailBlock from '../detail/TDetailBlock.vue'
import TUsageLimitPolicyForm, { type InheritedUsagePolicy } from './TUsageLimitPolicyForm.vue'
import { createUsageLimitBridge, type UsageLimitBridge } from '../../services/bridges/usage-limit-bridge'
import { useAdminClient } from '../../plugin/client'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import { interpolate, translatePageKey } from '../../i18n/translate'
import { formatUsd, formatWindowLength, policyProblem, samePolicy, toDraft, toPayload } from './usage-limit-format'

const props = defineProps<{
  /** A bridge to reuse (the host's own); one is created from the admin client otherwise. */
  bridge?: UsageLimitBridge
}>()

const emit = defineEmits<{
  /** After a successful save, with what the server stored. */
  saved: [defaults: UsageLimitDefaultsDto]
}>()

defineOptions({ name: 'TUsageLimitDefaultsPanel' })

const t = (key: string, params?: Record<string, unknown>): string => interpolate(translatePageKey('ai.quota', key), params)
const message = useSafeMessage()
const { can } = usePermissionGuard()
const canManage = computed(() => can('ai.quota.update'))

const bridge = props.bridge ?? createUsageLimitBridge({ client: useAdminClient() })

const loading = ref(true)
const saving = ref(false)
const data = ref<UsageLimitDefaultsDto | null>(null)
const draft = ref<SaveUsagePolicyDto | null>(null)

async function load(): Promise<void> {
  loading.value = true
  try {
    apply(await bridge.getDefaults())
  } catch (err) {
    data.value = null
    message.error(t('loadFailedWith', { message: err instanceof Error ? err.message : String(err) }))
  } finally {
    loading.value = false
  }
}
void load()

function apply(next: UsageLimitDefaultsDto): void {
  data.value = next
  draft.value = toDraft(next.policy)
}

const inherited = computed<InheritedUsagePolicy>(() => {
  const cfg = data.value?.configuration
  return {
    sessionLimitUsd: cfg?.sessionLimitUsd ?? null,
    weeklyLimitUsd: cfg?.weeklyLimitUsd ?? null,
    allowedModels: cfg?.allowedModels ?? [],
    defaultModel: cfg?.defaultModel ?? null,
  }
})

const dirty = computed(() => !!data.value && !!draft.value && !samePolicy(draft.value, toDraft(data.value.policy)))

/** Ids a default model may be taken from: the chosen list, else the configuration's (empty = any). */
const problem = computed(() => {
  if (!draft.value) return null
  return policyProblem(draft.value, draft.value.allowedModels ?? inherited.value.allowedModels)
})

const effectiveText = computed(() => {
  const e = data.value?.effective
  if (!e) return ''
  return t('policy.effectiveSummary', {
    session: e.sessionLimitUsd == null ? t('mode.unlimited') : formatUsd(e.sessionLimitUsd),
    weekly: e.weeklyLimitUsd == null ? t('mode.unlimited') : formatUsd(e.weeklyLimitUsd),
    models: e.allowedModels.length === 0
      ? t('policy.modelsNone')
      : e.allowedModels.map((m) => m.displayName || m.id).join(', '),
  })
})

function discard(): void {
  if (data.value) draft.value = toDraft(data.value.policy)
}

async function save(): Promise<void> {
  if (!draft.value || problem.value) return
  saving.value = true
  try {
    const next = await bridge.saveDefaults(toPayload(draft.value))
    apply(next)
    message.success(t('policy.saved'))
    emit('saved', next)
  } catch (err) {
    message.error(t('policy.saveFailed', { message: err instanceof Error ? err.message : String(err) }))
  } finally {
    saving.value = false
  }
}

defineExpose({ reload: load })
</script>

<style scoped>
.t-uld__body {
  display: flex;
  flex-direction: column;
  gap: 14px;
}
.t-uld__windows {
  margin: 0;
  font-size: 12px;
  color: var(--tnzi-base-text-muted, #888);
}
.t-uld__effective {
  margin: 0;
  padding: 8px 12px;
  border-radius: 8px;
  background: var(--tnzi-layout-bg);
  font-size: 13px;
  color: var(--tnzi-base-text);
}
.t-uld__effective-label {
  margin-right: 6px;
  font-weight: 500;
}
.t-uld__actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: flex-end;
  gap: 8px 12px;
}
.t-uld__problem {
  margin-right: auto;
  font-size: 12px;
  color: var(--tnzi-error);
}
</style>
